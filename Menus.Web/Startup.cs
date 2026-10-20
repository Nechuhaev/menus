// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Hangfire;
using Hangfire.MySql;
using Menus.Web.Data;
using Menus.Web.Helpers;
using Menus.Web.Middleware;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WebEncoders;
using SharpRaven;

namespace Menus.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        private static bool _isSitemapInited = false;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddDbContext<EfDbContext>(options =>
                options.UseMySql(Configuration.GetConnectionString("DefaultConnection")));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<EfDbContext>()
                .AddDefaultTokenProviders();

            services.AddAuthentication()
                .AddFacebook(fboptions => {
                    fboptions.AppId = Configuration["Facebook:AppId"];
                    fboptions.AppSecret = Configuration["Facebook:AppSecret"];
                    fboptions.CallbackPath = PathString.FromUriComponent("/Account/FbSignIn");
                })
                .AddGoogle(googleOptions =>
                {
                    googleOptions.ClientId = Configuration["Google:ClientId"];
                    googleOptions.ClientSecret = Configuration["Google:ClientSecret"];
                    googleOptions.CallbackPath = PathString.FromUriComponent("/Account/GoogleSignIn");
                });

            services.Configure<IdentityOptions>(options =>
            {
                // Password settings
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;

                // Lockout settings
                options.Lockout.MaxFailedAccessAttempts = 10;

                // User settings
                options.User.RequireUniqueEmail = false;
            });

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/";
            });

            services.AddMenusDomain(Configuration);

            services.AddSingleton(x => new RavenClient(Configuration["Sentry:Dsn"]));
            services.AddSingleton(x => new TwilioService(Configuration["Twilio:Phone"]));
            services.AddScoped<EmailService>();
            services.AddScoped<UrlGenerateService>();
            services.AddScoped<RestaurantRegistrationService>();

            services.Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = 5000000; // 5 MB
                x.MultipartBodyLengthLimit = 5000000; // 5 MB
            });

            // X-Frame-Options виставляємо самі в middleware: antiforgery у 2.1 перезаписував його на SAMEORIGIN і для iframe
            services.AddAntiforgery(o => o.SuppressXFrameOptionsHeader = true);

            //services.AddMiniProfiler(options =>
            //{
            //    options.ShouldProfile = request =>
            //    {
            //        var cookie = request.Cookies[".Menus.UseProfiler"];
            //        return !string.IsNullOrEmpty(cookie);
            //    };

            //}).AddEntityFramework();

            Img.Configure(Configuration["Media:BaseUrl"]);

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
                    .Concat(new[] { "image/svg+xml", "application/xml", "text/xml" });
            });

            services.AddRouting(options => options.LowercaseUrls = true);
            services.AddMvc(options => { options.ModelBinderProviders.Insert(0, new FormDataJsonBinderProvider()); })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                .AddViewLocalization(options => options.ResourcesPath = "Resources");

            services.AddDataProtection()
                .SetApplicationName("Menus.Web")
                .PersistKeysToFileSystem(new DirectoryInfo("Keys"))
                .SetDefaultKeyLifetime(TimeSpan.FromDays(365));

            if (Configuration["UpdateSitemap"] == "true")
            {
                services.AddHangfire(x => x.UseStorage(
                    new MySqlStorage(Configuration.GetConnectionString("DefaultConnection"), new MySqlStorageOptions
                    {
                        TransactionIsolationLevel = IsolationLevel.ReadCommitted,
                        QueuePollInterval = TimeSpan.FromSeconds(15),
                        JobExpirationCheckInterval = TimeSpan.FromHours(1),
                        CountersAggregateInterval = TimeSpan.FromMinutes(5),
                        PrepareSchemaIfNecessary = true,
                        DashboardJobListLimit = 50000,
                        TransactionTimeout = TimeSpan.FromMinutes(1),
                        TablesPrefix = "Hangfire"
                    })));
                services.AddHangfireServer();

                Task.Run(() =>
                {
                    Thread.Sleep(10000);
                    RecurringJob.Trigger("UpdateSitemap");
                });
            }

            services.Configure<WebEncoderOptions>(options =>
            {
                options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
            });
        }

        private async Task<HttpResponseMessage> InitSitemap(string url)
        {
            var client = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(60)
            };

            var result = await client.GetAsync(url);

            client.Dispose();

            return result;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider provider)
        {
            //env.EnvironmentName = EnvironmentName.Staging;
var forwardedHeadersOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto }; forwardedHeadersOptions.KnownNetworks.Clear(); forwardedHeadersOptions.KnownProxies.Clear(); app.UseForwardedHeaders(forwardedHeadersOptions);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
                //app.UseMiniProfiler();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseMiddleware<ErrorLoggingMiddleware>();
                app.UseHsts();
                //app.UseMiniProfiler();
                // Сторінка помилки з мовним префіксом і малими літерами: інакше редірект на нижній регістр
                // і редірект на мовну версію перетворювали 404 на 301 (soft 404 для пошукових систем)
                app.UseStatusCodePages(ReExecuteStatusCodePage);
            }

            //app.UseMiniProfiler();
            //app.UseStatusCodePagesWithReExecute("/Home/StatusCodeError/{0}");

            app.UseHttpsRedirection();

            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path == "/robots.txt")
                {
                    ctx.Request.Path = new PathString($"/robots.{env.EnvironmentName}.txt");
                }

                await next.Invoke();
            });

            // Sitemap init
            //if (!env.IsDevelopment())
            //{
            //    app.Use(async (ctx, next) =>
            //    {
            //        if (!_isSitemapInited)
            //        {
            //            _isSitemapInited = true;

            //            var crq = ctx.Request;
            //            if (crq.Path.ToString().Contains("sitemap"))
            //            {
            //                await InitSitemap($"http{(crq.IsHttps ? "s" : "")}://{crq.Host}/ru/sitemap");
            //            }
            //            else InitSitemap($"http{(crq.IsHttps ? "s" : "")}://{crq.Host}/ru/sitemap");
            //        }

            //        await next.Invoke();
            //    });
            //}


            app.UseResponseCompression();

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    // Файли з asp-append-version (?v=hash) змінюють URL при зміні вмісту - кешуємо на рік
                    var isVersioned = ctx.Context.Request.Query.ContainsKey("v");
                    ctx.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = isVersioned
                        ? "public,max-age=31536000,immutable"
                        : "public,max-age=" + 60 * 60 * 24 * 7;
                }
            });
            app.UseCookiePolicy();
            app.UseAuthentication();

            app.Use(async (ctx, next) =>
            {
                if (string.IsNullOrEmpty(ctx.Request.Cookies[".Menus.DeviceId"]))
                {
                    ctx.Response.Cookies.Append(
                        ".Menus.DeviceId",
                        Guid.NewGuid().ToString(),
                        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(10), IsEssential = true, HttpOnly = true, Secure = ctx.Request.IsHttps }
                    );
                }

                if (ctx.Request.Method == "GET" && ctx.Request.Path.Value.Any(char.IsUpper))
                {
                    var redirectPathString = new PathString(ctx.Request.Path.Value.ToLowerInvariant());
                    ctx.Response.Redirect($"{redirectPathString}{ctx.Request.QueryString}", true);
                    return;
                }

                // Сторінки /iframe/ вбудовуються на сайти ресторанів, решта - захист від clickjacking
                if (!ctx.Request.Path.Value.Contains("/iframe/"))
                {
                    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                }

                await next.Invoke();
            });

            //app.Use(async (ctx, next) =>
            //{
            //    if(!ctx.Response.Headers.Any(x => x.Key == "Last-Modified")) ctx.Response.Headers.Add("Last-Modified", DateTime.Parse(Configuration["LastModified"]).ToString("R"));
            //    await next.Invoke();
            //});

            if (!env.IsDevelopment())
            {
                app.UseHangfireDashboard("/hangfireWDAyULeoRJ34k8xUitnV");
            }

            var dbContext = provider.GetService<EfDbContext>();
            dbContext.Database.Migrate();

            CreateDefaultLocale(provider).Wait();
            UseLocales(provider, app).Wait();

            //app.UseMvc(routes =>
            //{
            //    routes.MapRoute(
            //        name: "restaurant",
            //        template: "restaurant/{id?}",
            //        defaults: new { controller = "Home", action = "Restaurant" });

            //    routes.MapRoute(
            //        name: "dish",
            //        template: "restaurant/{id?}/{dishId?}",
            //        defaults: new { controller = "Home", action = "Dish" });

            //    routes.MapRoute(
            //        name: "areas",
            //        template: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

            //    routes.MapRoute(
            //        name: "default",
            //        template: "{controller=Home}/{action=Index}/{id?}");
            //});

            CreateRolesAndSU(provider).Wait();
        }

        private static readonly Regex CulturePrefix = new Regex("^/(ru|uk|en)(?=/|$)", RegexOptions.IgnoreCase);

        // Аналог UseStatusCodePagesWithReExecute, але шлях залежить від мови вихідного запиту
        private static async Task ReExecuteStatusCodePage(StatusCodeContext context)
        {
            var http = context.HttpContext;
            var originalPath = http.Request.Path;
            var originalQuery = http.Request.QueryString;
            var match = CulturePrefix.Match(originalPath.Value ?? "");
            var culture = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "uk";

            http.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
            {
                OriginalPathBase = http.Request.PathBase.Value,
                OriginalPath = originalPath.Value,
                OriginalQueryString = originalQuery.HasValue ? originalQuery.Value : null
            });
            http.Request.Path = new PathString($"/{culture}/home/statuscodeerror/{http.Response.StatusCode}");
            http.Request.QueryString = QueryString.Empty;
            try
            {
                await context.Next(http);
            }
            finally
            {
                http.Request.Path = originalPath;
                http.Request.QueryString = originalQuery;
                http.Features.Set<IStatusCodeReExecuteFeature>(null);
            }
        }

        private async Task UseLocales(IServiceProvider serviceProvider, IApplicationBuilder app)
        {
            var dbContext = serviceProvider.GetRequiredService<EfDbContext>();
            var locales = await dbContext.Locales.ToListAsync();
            var defaultLocale = locales.FirstOrDefault(x => x.Default);

            var supportedCultures = locales.Select(x => new CultureInfo(x.Id)).ToArray();

            var localizationOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(new CultureInfo(defaultLocale.Id)),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };
            var requestProvider = new RouteDataRequestCultureProvider();
            localizationOptions.RequestCultureProviders.Clear();
            localizationOptions.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
            localizationOptions.RequestCultureProviders.Insert(0, requestProvider);

            string custureRoutePath = "{culture="+ defaultLocale.Id + "}";

            app.UseRouter(routes =>
            {
                routes.MapMiddlewareRoute(custureRoutePath  +  "/{*mvcRoute}", subApp =>
                {
                    subApp.UseRequestLocalization(localizationOptions);

                    subApp.UseMvc(mvcRoutes =>
                    {
                        mvcRoutes.MapRoute(
                            name: "files",
                            template: "/files/{id?}",
                            defaults: new { controller = "Home", action = "File" });

                        mvcRoutes.MapRoute(
                            name: "restaurant",
                            template: custureRoutePath + "/{city=kyiv}/restaurant/{id?}",
                            defaults: new { controller = "Home", action = "Restaurant" });

                        mvcRoutes.MapRoute(
                            name: "iframe",
                            template: custureRoutePath + "/{city=kyiv}/iframe/{id?}",
                            defaults: new { controller = "Iframe", action = "Restaurant" });

                        mvcRoutes.MapRoute(
                            name: "iframe-clean",
                            template: custureRoutePath + "/{city=kyiv}/iframe/clean/{id?}",
                            defaults: new { controller = "Iframe", action = "Clean" });

                        mvcRoutes.MapRoute(
                            name: "iframe-cache",
                            template: custureRoutePath + "/{city=kyiv}/iframe/cache/{id?}",
                            defaults: new { controller = "Iframe", action = "Cache" });

                        mvcRoutes.MapRoute(
                            name: "content",
                            template: custureRoutePath + "/content/{id?}",
                            defaults: new { controller = "Home", action = "ContentPage" });

                        mvcRoutes.MapRoute(
                            name: "news",
                            template: custureRoutePath + "/news/{id?}",
                            defaults: new { controller = "Home", action = "NewsPage" });

                        mvcRoutes.MapRoute(
                            name: "dish",
                            template: custureRoutePath + "/{city=kyiv}/restaurant/{id?}/{dishId?}",
                            defaults: new { controller = "Home", action = "Dish" });

                        mvcRoutes.MapRoute(
                            name: "restaurantAreaWithCultureAndRestId",
                            template: custureRoutePath + "/{area:exists}/{restaurantId:guid:required}/{controller=Home}/{action=Index}/{id?}");

                        mvcRoutes.MapRoute(
                            name: "areasCulture",
                            template: custureRoutePath + "/{area:exists}/{controller=Home}/{action=Index}/{id?}");

                        mvcRoutes.MapRoute(
                            name: "index",
                            template: "{culture:regex(^ru|en|uk$)}",
                            defaults: new { controller = "Home", action = "Index" });

                        mvcRoutes.MapRoute(
                            name: "defaultCulture",
                            template: custureRoutePath + "/{controller=Home}/{action=Index}/{id?}");

                        mvcRoutes.MapRoute(
                            name: "areas",
                            template: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

                        mvcRoutes.MapRoute(
                            name: "default",
                            template: "{controller=Home}/{action=Index}/{id?}");
                    });
                });
            });

            //app.UseRequestLocalization(new RequestLocalizationOptions
            //{
            //    DefaultRequestCulture = new RequestCulture(defaultLocale.Id),
            //    // Formatting numbers, dates, etc.
            //    SupportedCultures = supportedCultures,
            //    // UI strings that we have localized.
            //    SupportedUICultures = supportedCultures,
            //    RequestCultureProviders = new List<IRequestCultureProvider> {
            //        new CookieRequestCultureProvider(),
            //        new AcceptLanguageHeaderRequestCultureProvider()
            //    }
            //});
        }

        private async Task CreateDefaultLocale(IServiceProvider serviceProvider)
        {
            var dbContext = serviceProvider.GetRequiredService<EfDbContext>();
            var locales = dbContext.Locales;
            var existingLocale = await locales.FirstOrDefaultAsync(x => x.Default);
            if (existingLocale == null)
            {
                await locales.AddAsync(new Locale {
                    Id = "ru",
                    Default = true,
                    Name = "русский",
                    ShortName = "рус"
                });
                await locales.AddAsync(new Locale
                {
                    Id = "en",
                    Default = false,
                    Name = "english",
                    ShortName = "eng"
                });
                await locales.AddAsync(new Locale
                {
                    Id = "uk",
                    Default = false,
                    Name = "українська",
                    ShortName = "укр"
                });
                await dbContext.SaveChangesAsync();
            }
        }

        private async Task CreateRolesAndSU(IServiceProvider serviceProvider)
        {
            var RoleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var UserManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            string[] roleNames = { "admin", "manager", "restaurant", "restaurant-manager" };

            foreach (var roleName in roleNames)
            {
                var roleExist = await RoleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await RoleManager.CreateAsync(new IdentityRole(roleName));
                }
            }


            var poweruser = new ApplicationUser
            {

                Id = "1a950ec5-7fd3-4d82-adca-d27fda6025b5",
                NormalizedUserName = "1A950EC5-7FD3-4D82-ADCA-D27FDA6025B5",
                EmailConfirmed = true,
                UserName = "1a950ec5-7fd3-4d82-adca-d27fda6025b5",
                Email = "admin@vvertigoo.pp.ua",
                PhoneNumber = "+380678946096"
            };

            string userPWD = "!QNtfBhgYaA0DVdZ6Gb3v";
            var _user = await UserManager.FindByEmailAsync("admin@vvertigoo.pp.ua");

            if (_user == null)
            {
                var createPowerUser = await UserManager.CreateAsync(poweruser, userPWD);
                if (createPowerUser.Succeeded)
                {
                    await UserManager.AddToRoleAsync(poweruser, "admin");
                }
            }
        }
    }
}
