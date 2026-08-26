using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Menus.Files.Routing;
using Menus.Files.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Menus.Files.Routes
{
    public class BackupsRouteHandler : IRouteHandler
    {
        private readonly ILogger _logger;
        private readonly DigitalOceanService _digitalOceanService;

        public BackupsRouteHandler(
            DigitalOceanService digitalOceanService,
            ILogger<ImagesRouteHandler> logger)
        {
            _digitalOceanService = digitalOceanService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext.Request.Method == "POST") await Post(httpContext);
        }

        public async Task Post(HttpContext httpContext)
        {
            var connectionString = "server=localhost;userid=root;pwd=Gp7C1MSfbmZvGDKsKvyB;port=3307;database=menus;sslmode=none;Allow User Variables=true";
            var currentDirectory = Directory.GetCurrentDirectory();

            var filename =  Path.Combine(currentDirectory, "Backup", "backup.sql");

            if (!Directory.Exists(Path.Combine(currentDirectory, "Backup")))
            {
                Directory.CreateDirectory(Path.Combine(currentDirectory, "Backup"));
            }

            if (File.Exists(filename))
            {
                File.Delete(filename);
            }

            var file = File.CreateText(filename);
            file.Write("/* placeholder */");
            file.Close();
            file.Dispose();

            _logger.LogTrace("Starting backup process");

            await Task.Run(() =>
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    using (MySqlCommand cmd = new MySqlCommand())
                    {
                        using (MySqlBackup mb = new MySqlBackup(cmd))
                        {
                            cmd.Connection = conn;
                            conn.Open();
                            mb.ExportToFile(filename);
                            conn.Close();
                        }
                    }
                }
            });

            _logger.LogTrace("Backup done");
            _logger.LogTrace("Creating zip");

            var zipFilename = Path.Combine(currentDirectory, "backup.zip");

            if (File.Exists(zipFilename))
            {
                File.Delete(zipFilename);
            }

            ZipFile.CreateFromDirectory(Path.Combine(currentDirectory, "Backup"), zipFilename);

            _logger.LogTrace("Zip created");
            _logger.LogTrace("Uploading zip");

            await _digitalOceanService.UploadFile(zipFilename, $"backups/backup-{DateTime.Now:yyyy-MM-dd}.zip");

            _logger.LogTrace("Zip uploaded");

            httpContext.Response.StatusCode = StatusCodes.Status201Created;
        }
    }

    public static class BackupsRouteHandlerServiceCollectionExtensions
    {
        public static void AddBackupsRouteHandler(this IServiceCollection services)
        {
            services.AddScoped<BackupsRouteHandler, BackupsRouteHandler>();
        }
    }
}
