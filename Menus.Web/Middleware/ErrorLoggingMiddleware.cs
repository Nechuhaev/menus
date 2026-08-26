// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Microsoft.AspNetCore.Http;
using SharpRaven;
using System;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SharpRaven.Data;
using System.Linq;

namespace Menus.Web.Middleware
{
    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception e)
            {
                var crq = context.Request;

                await LogEvent("Exception", new
                {
                    url = $"http{(crq.IsHttps ? "s" : "")}://{crq.Host}{crq.Path}{crq.QueryString}",
                    userAgent = context.Request.Headers["User-Agent"],
                    //requestPayload = (await context.Request.ReadFormAsync()).Select(x => new { key = x.Key, value = x.Value.FirstOrDefault() }),
                    exception = e,
                    requestId = context.TraceIdentifier
                }, context);

                var raven = context.RequestServices.GetService<RavenClient>();

                e.Data.Add("Url", $"http{(crq.IsHttps ? "s" : "")}://{crq.Host}{crq.Path}{crq.QueryString}");
                e.Data.Add("RequestId", context.TraceIdentifier);
                await raven.CaptureAsync(new SentryEvent(e));
                throw;
            }
        }

        private async Task LogEvent(string name, object data, HttpContext context)
        {
            var core = context.RequestServices.GetService<Core>();
            var eventData = "No data or data is corrupted";

            try
            {
                eventData = JsonConvert.SerializeObject(data);
            }
            catch (Exception)
            {
            }

            var logItem = new LogItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = context.User.Identity.Name,
                DeviceId = context.Request.Cookies[".Menus.DeviceId"] ?? "system",
                Event = name,
                EventData = eventData,
                Timestamp = DateTime.Now
            };
            await core.LogItems.Create(logItem);
        }
    }
}
