using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Dapper;
using Newtonsoft.Json;
using System.Data.SqlClient;

namespace Library
{
    public class ApiLogHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string referenceId = Guid.NewGuid().ToString();

            var apiLogEntry = CreateApiLogEntryWithRequestData(request, referenceId);

            if (request.Content != null)
            {
                await request.Content.ReadAsStringAsync()
                    .ContinueWith(task => apiLogEntry.RequestContentBody = task.Result, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken).ContinueWith(task =>
              {
                  var response = task.Result;

                  apiLogEntry.ResponseStatusCode = (int)response.StatusCode;
                  apiLogEntry.ResponseTimestamp = DateTime.Now;

                  if (response.Content != null)
                  {
                      apiLogEntry.ResponseContentBody = response.Content.ReadAsStringAsync().Result;
                      apiLogEntry.ResponseContentType = response.Content.Headers.ContentType.MediaType;
                      apiLogEntry.ResponseHeaders = GetHeadersAsDictionary(response.Content.Headers);
                  }

                  SaveLogToDb(apiLogEntry);

                  return response;
              }, cancellationToken);
        }

        private void SaveLogToDb(ApiLogEntry apiLogEntry)
        {
            string content = JsonConvert.SerializeObject(apiLogEntry);

            using (var connection = new SqlConnection(ConfigurationManager.ConnectionStrings["LogsDbConnectionString"].ConnectionString))
            {
                connection.Execute(
                    "insert into [dbo].[logs] (LogContent,ReferenceId) values (@LogContent,@ReferenceId)",
                    new { @LogContent = content, @ReferenceId = apiLogEntry.LogReferenceId, @CreationDate = DateTime.Now });
            }
        }

        private ApiLogEntry CreateApiLogEntryWithRequestData(HttpRequestMessage request, string referenceId)
        {
            var context = ((HttpContextBase)request.Properties["MS_HttpContext"]);
            var routeData = request.GetRouteData();

            var apiLogEntry = new ApiLogEntry();
            apiLogEntry.Application = "WebAPI";

            if (context.User != null)
            {
                apiLogEntry.User = context.User.Identity.Name;
            }

            apiLogEntry.Machine = Environment.MachineName;

            apiLogEntry.LogReferenceId = referenceId;

            if (context.Request != null)
            {
                apiLogEntry.RequestContentType = context.Request.ContentType;

                if (routeData != null)
                {
                    apiLogEntry.RequestRouteTemplate = routeData.Route.RouteTemplate;
                    apiLogEntry.RequestRouteData = routeData;
                }

                apiLogEntry.RequestIpAddress = context.Request.UserHostAddress;
            }
            if (request.Method != null)
            {
                apiLogEntry.RequestMethod = request.Method.Method;
            }
            if (request.Headers != null)
            {
                apiLogEntry.RequestHeaders = GetHeadersAsDictionary(request.Headers);
            }

            apiLogEntry.RequestTimestamp = DateTime.Now;

            if (request.RequestUri != null)
            {
                apiLogEntry.RequestUri = request.RequestUri.AbsoluteUri;
            }

            return apiLogEntry;
        }

        private Dictionary<string, string> GetHeadersAsDictionary(HttpHeaders headers)
        {
            var dict = new Dictionary<string, string>();

            foreach (var item in headers.ToList())
            {
                if (item.Value == null) continue;

                var header = item.Value.Aggregate(String.Empty, (current, value) => current + (value + " "));

                header = header.TrimEnd(" ".ToCharArray());

                dict.Add(item.Key, header);
            }
            return dict;
        }

    }
}
