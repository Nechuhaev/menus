using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Menus.Files.Routing;
using Menus.Files.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Menus.Files.Routes
{
    public class IframesRouteHandler : IRouteHandler
    {
        private readonly ILogger _logger;
        private readonly DigitalOceanService _digitalOceanService;

        public IframesRouteHandler(
            DigitalOceanService digitalOceanService,
            ILogger<ImagesRouteHandler> logger)
        {
            _digitalOceanService = digitalOceanService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext.Request.Method == "GET") await Get(httpContext);
            if (httpContext.Request.Method == "PUT") await Put(httpContext);
        }

        public async Task Get(HttpContext httpContext)
        {
            var url = httpContext != null ? GetIframeUrl(httpContext) : throw new ArgumentNullException(nameof(httpContext));

            var name = url.Replace("/", "_");

            if (IsMobileUser(httpContext.Request))
            {
                name += "_mob";
            }

            _logger.LogTrace("Downloading html from CDN");
            var html = await GetHtmlFile(name);
            _logger.LogTrace("Html from CDN downloaded");

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html";
            httpContext.Response.Headers.Add("X-Frame-Options", "AllowAll");
            await httpContext.Response.WriteAsync(html);
        }

        public async Task Put(HttpContext httpContext)
        {
            var url = httpContext != null ? GetIframeUrl(httpContext) : throw new ArgumentNullException(nameof(httpContext));
            
            var name = url.Replace("/", "_");

            _logger.LogTrace("Downloading html");
            var html = await DownloadHtml(url);
            _logger.LogTrace("Html downloaded");

            html = html.Replace("https://tipmymenu.com/", "/");

            _logger.LogTrace("Uploading html file");
            await _digitalOceanService.UploadFile(Encoding.UTF8.GetBytes(html), $"iframes/{name}.html");
            _logger.LogTrace("Html file uploaded");

            httpContext.Response.StatusCode = StatusCodes.Status201Created;
        }

        private static string GetIframeUrl(HttpContext httpContext)
        {
            return httpContext.Request.Path.ToString().Replace("/api/iframes/", "");
        }

        private async Task<string> GetHtmlFile(string name)
        {
            var client = new HttpClient();

            var response = await client.GetStringAsync($"https://cdn-tipmymenus-com.fra1.digitaloceanspaces.com/iframes/{name}.html");

            client.Dispose();
            return response;
        }

        private async Task<string> DownloadHtml(string url)
        {
            var client = new HttpClient();

            var response = await client.GetStringAsync($"https://tipmymenu.com/{url}");

            client.Dispose();
            return response;
        }

        private bool IsMobileUser(HttpRequest request)
        {
            var userAgent = request.Headers["User-Agent"].FirstOrDefault();
            if (string.IsNullOrEmpty(userAgent)) userAgent = "";
            var macosPlatforms = new List<string> { "Macintosh", "MacIntel", "MacPPC", "Mac68K" };
            var windowsPlatforms = new List<string> { "Win32", "Win64", "Windows", "WinCE" };
            var iosPlatforms = new List<string> { "iPhone", "iPad", "iPod" };
            var device = "";

            if (macosPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "desktop";
            }
            else if (iosPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "mobile";
            }
            else if (windowsPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "desktop";
            }
            else if (userAgent.Contains("Android"))
            {
                device = "mobile";
            }
            else if (userAgent.Contains("Linux"))
            {
                device = "desktop";
            }

            return device == "mobile";
        }
    }

    public static class IframesRouteHandlerServiceCollectionExtensions
    {
        public static void AddIframesRouteHandler(this IServiceCollection services)
        {
            services.AddScoped<IframesRouteHandler, IframesRouteHandler>();
        }
    }
}
