using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Menus.Files.Routing;
using Menus.Files.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Menus.Files.Routes
{
    public class ImagesRouteHandler : IRouteHandler
    {
        private readonly ILogger _logger;
        private readonly DigitalOceanService _digitalOceanService;

        public ImagesRouteHandler(
            DigitalOceanService digitalOceanService,
            ILogger<ImagesRouteHandler> logger)
        {
            _digitalOceanService = digitalOceanService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            _logger.LogTrace("Decoding input");
            var inputString = httpContext != null ? await GetRequestBodyAsString(httpContext) : throw new ArgumentNullException(nameof(httpContext));
            _logger.LogTrace("Input decoded");

            _logger.LogTrace("Getting guid");
            var name = GetGuidFromRequest(inputString);
            _logger.LogTrace("Guid decoded");

            if (await CheckNameForDuplicate(name))
            {
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                await httpContext.Response.WriteAsync(name);
                return;
            }

            _logger.LogTrace("Getting file from base64");
            var image = GetFileFromBase64(inputString);
            var mime = GetMimeFromBase64(inputString);
            _logger.LogTrace("Base64 decoded");

            _logger.LogTrace("Getting smaller images");
            byte[] imageLarge;
            byte[] imageMedium;
            byte[] imageSmall;
            try
            {
                imageLarge = GetLargeImage(image);
                imageMedium = GetMediumImage(image);
                imageSmall = GetSmallImage(image);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError("File can't be processed (wrong format or different reason)");

                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(name);
                return;
            }
            _logger.LogTrace("Smaller images processed");

            _logger.LogTrace("Uploading images");
            await Task.WhenAll(
                _digitalOceanService.UploadFile(image, $"files/{name}.{(mime == "image/png" ? "png" : "jpg")}"),
                _digitalOceanService.UploadFile(imageLarge, $"files/{name}-large.jpg"),
                _digitalOceanService.UploadFile(imageMedium, $"files/{name}-medium.jpg"),
                _digitalOceanService.UploadFile(imageSmall, $"files/{name}-small.jpg")
            );
            _logger.LogTrace("Images uploaded");

            httpContext.Response.StatusCode = StatusCodes.Status201Created;
            await httpContext.Response.WriteAsync(name);
        }

        // Перевірка в самому бакеті, а не за публічною адресою: не залежить від CDN і старих доменів
        private Task<bool> CheckNameForDuplicate(string name)
        {
            return _digitalOceanService.Exists($"files/{name}-small.jpg");
        }

        private byte[] GetSmallImage(byte[] image)
        {
            return ResizeImage(image, width: 300, height: 300);
        }

        private byte[] GetMediumImage(byte[] image)
        {
            return ResizeImage(image, width: 600, height: 600);
        }

        private byte[] GetLargeImage(byte[] image)
        {
            return ResizeImage(image, width: 1000, height: 1000);
        }

        private byte[] ResizeImage(byte[] image, int width, int height)
        {
            var inputStream = new MemoryStream(image);
            var outputStream = new MemoryStream();

            using (Image<Rgba32> processingImage = Image.Load(inputStream))
            {
                processingImage.Mutate(x => x
                    .Resize(0, height));
                processingImage.SaveAsJpeg(outputStream, new JpegEncoder
                {
                    Quality = 80
                });
            }

            var result = outputStream.ToArray();
            inputStream.Dispose();
            outputStream.Dispose();

            return result;
        }

        private byte[] GetFileFromBase64(string inputString)
        {
            inputString = inputString.Substring(inputString.IndexOf(',') + 1);
            inputString = inputString.Trim('\0');
            return Convert.FromBase64String(inputString);
        }

        private string GetMimeFromBase64(string inputString)
        {
            return Regex.Match(inputString, "data:(.+?);").Groups[1].Value;
        }

        private string GetGuidFromRequest(string inputString)
        {
            return inputString.Substring(0, 36);
        }

        private async Task<string> GetRequestBodyAsString(HttpContext context)
        {
            string result;
            using (var streamReader = new StreamReader(context.Request.Body))
            {
                result = await streamReader.ReadToEndAsync();
            }

            return result;
        }
    }

    public static class ImagesRouteHandlerServiceCollectionExtensions
    {
        public static void AddImagesRouteHandler(this IServiceCollection services)
        {
            services.AddScoped<ImagesRouteHandler, ImagesRouteHandler>();
        }
    }
}
