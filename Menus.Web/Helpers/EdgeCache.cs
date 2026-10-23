using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Menus.Web.Helpers
{
    // Кеш HTML у Cloudflare для гостей. Сторінка з цим атрибутом віддається анонімам з
    // Cloudflare-CDN-Cache-Control: max-age=N і без Set-Cookie (інакше Cloudflare не кешує, а кука
    // одного відвідувача дісталась би всім). Антифорджері-токен такої сторінки
    // підміняється свіжим з /antiforgery/token (див. _Layout).
    public class EdgeCacheAttribute : ResultFilterAttribute
    {
        public const string ItemKey = "EdgeCache";

        private readonly int _seconds;

        public EdgeCacheAttribute(int seconds)
        {
            _seconds = seconds;
        }

        public override void OnResultExecuting(ResultExecutingContext context)
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)) return;
            if (http.User?.Identity?.IsAuthenticated == true) return;

            http.Items[ItemKey] = true;
            http.Response.OnStarting(() =>
            {
                if (http.Response.StatusCode == StatusCodes.Status200OK)
                {
                    http.Response.Headers.Remove(HeaderNames.SetCookie);
                    http.Response.Headers.Remove(HeaderNames.Pragma);
                    // Термін для Cloudflare - окремим заголовком (Cloudflare його прибирає), браузеру - no-store:
                    // інакше браузер показував збережену гостьову копію після входу (зокрема кнопкою «Назад»)
                    http.Response.Headers["Cloudflare-CDN-Cache-Control"] = $"max-age={_seconds}";
                    http.Response.Headers[HeaderNames.CacheControl] = "no-store";
                }
                return Task.CompletedTask;
            });
        }
    }

    // Очищення кешу Cloudflare за URL. Кеш розділений за типом пристрою (Cache by device type),
    // тому кожен URL очищається для desktop, mobile і tablet.
    // Змінні в /root/menus.env: Cloudflare__ZoneId, Cloudflare__PurgeToken (лише право Cache Purge).
    public static class EdgeCachePurge
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly string[] DeviceTypes = { "desktop", "mobile", "tablet" };
        private const int MaxFilesPerRequest = 30;

        public static async Task Purge(IConfiguration config, IEnumerable<string> urls)
        {
            var zone = config["Cloudflare:ZoneId"];
            var token = config["Cloudflare:PurgeToken"];
            if (string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(token)) return;

            var files = urls.Distinct()
                .SelectMany(url => DeviceTypes.Select(device => new
                {
                    url,
                    headers = new Dictionary<string, string> { ["CF-Device-Type"] = device }
                }))
                .ToList();

            for (var i = 0; i < files.Count; i += MaxFilesPerRequest)
            {
                var body = JsonConvert.SerializeObject(new { files = files.Skip(i).Take(MaxFilesPerRequest) });
                var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/zones/{zone}/purge_cache")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                try
                {
                    var response = await Client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                        Console.WriteLine($"Cloudflare purge failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Cloudflare purge failed: {e.Message}");
                }
            }
        }
    }
}
