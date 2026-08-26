using System;
using System.Collections.Generic;
using Menus.Files.Routes;
using Menus.Files.Routing;
using Menus.Files.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Menus.Files
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddImagesRouteHandler();
            services.AddIframesRouteHandler();
            services.AddBackupsRouteHandler();
            services.AddScoped<DigitalOceanService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouter(new Dictionary<IRoute, Type>
            {
                { new Route("/api/image", "PUT"), typeof(ImagesRouteHandler) },
                { new Route("/api/backups", "POST"), typeof(BackupsRouteHandler) },
                { new Route("/api/iframes", "GET"), typeof(IframesRouteHandler) }
            });
        }
    }
}
