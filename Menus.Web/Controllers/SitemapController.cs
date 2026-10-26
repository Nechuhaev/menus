using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Dapper;
using Hangfire;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Files.Services;
using Menus.Web.Data;
using Menus.Web.Helpers;
using Menus.Web.Models;
using Menus.Web.Models.Internal;
using Menus.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Menus.Web.Controllers
{
    public class SitemapController : Controller
    {
        // Пароль службових ендпоінтів (sitemap, бекап, перегенерація кешу) - з конфігу/змінної оточення Sitemap__Password
        private string SitemapPassword => _config["Sitemap:Password"];

        // Публічна адреса сайту для sitemap: не з Request.Host, бо генерацію запускає завдання Hangfire
        // через внутрішню адресу (127.0.0.1:5001), і вона потрапляла в <loc>
        private string SiteBaseUrl => (_config["Site:BaseUrl"] ?? "https://tipmymenu.com").TrimEnd('/');

        private bool IsValidPwd(string pwd) =>
            !string.IsNullOrEmpty(SitemapPassword) && pwd == SitemapPassword;

        private readonly LocaleService _locales;
        private readonly Core _core;
        private readonly IDbContext _db;
        private const int NUMBER_OF_RECORDS = 40000;
        private readonly ILogger _logger;
        private readonly UrlGenerateService _urls;
        private readonly IConfiguration _config;
        private readonly IDbConnection _dapper;
        private readonly EfDbContext _efContext;
        private readonly AbTestService _abTests;

        public SitemapController(
            LocaleService locales,
            Core core, IDbContext db,
            ILogger<SitemapController> logger,
            UrlGenerateService urls,
            IConfiguration config,
            EfDbContext efContext, AbTestService abTests)
        {
            _locales = locales;
            _core = core;
            _db = db;
            _logger = logger;
            _urls = urls;
            _config = config;
            _dapper = efContext.Database.GetDbConnection();
            _efContext = efContext;
            _abTests = abTests;
        }

        public async Task<IActionResult> Index(string pwd)
        {
            if(IsValidPwd(pwd)) await UpdateJob();
            return Ok();
        }

        public async Task<IActionResult> ProcessPhotos(string pwd, int take = 10)
        {
            if (IsValidPwd(pwd))
            {
                var files = _db.Files.Where(x => string.IsNullOrEmpty(x.CdnLink)).OrderBy(x => x.Id).Take(take).ToList();
                foreach (var dbFile in files)
                {
                    var base64 = $"{dbFile.Id};data:image/{dbFile.Mime};base64,{Convert.ToBase64String(dbFile.Data)}";
                    var http = new HttpClient();
                    var newUrl = await (await http.PutAsync("http://localhost:5100/api/image", new StringContent(base64))).Content.ReadAsStringAsync();
                    http.Dispose();
                    dbFile.CdnLink = newUrl;
                    await _db.Files.Update(dbFile);
                }
            }
            return Ok();
        }

        public async Task<IActionResult> RegenerateDishSeo(string pwd)
        {
            if (IsValidPwd(pwd)) await RegenerateDishSeo();
            return Ok();
        }

        private async Task UpdateJob()
        {
            await UpdateMainSitemap();
            await UpdatePagesSitemap();
            await UpdateRestaurantSitemap();
            await UpdateDishSitemap();
        }

        public IActionResult RunUpdateJob(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var crq = Request;
            var url = $"http{ (crq.IsHttps ? "s" : "")}://{ crq.Host}/ru/sitemap?pwd={SitemapPassword}";
            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
            return Ok();
        }

        public IActionResult RunRegenerateDishSeoJob(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var url = $"{InternalUrl.Base}/ru/sitemap/RegenerateDishSeo?pwd={SitemapPassword}";
            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
            return Ok();
        }

        public IActionResult RunRegenerateRestaurantIframesCacheJob(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var url = $"{InternalUrl.Base}/ru/sitemap/RegenerateRestaurantIframesCache?pwd={SitemapPassword}";
            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
            return Ok();
        }

        public IActionResult CronUpdateJob(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var crq = Request;
            var url = $"http{ (crq.IsHttps ? "s" : "")}://{ crq.Host}/ru/sitemap?pwd={SitemapPassword}";
            RecurringJob.AddOrUpdate<SitemapUpdateService>("UpdateSitemap", x => x.Update(url), Cron.Daily);
            return Ok();
        }

        public IActionResult CronBackupJob(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var crq = Request;
            var url = $"http{ (crq.IsHttps ? "s" : "")}://{ crq.Host}/ru/sitemap/RunDbBackup?pwd={SitemapPassword}";
            RecurringJob.AddOrUpdate<SitemapUpdateService>("RunDbBackup", x => x.Update(url), Cron.Daily(3));
            return Ok();
        }

        public IActionResult RunSetDisplayAddresses(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var url = $"{InternalUrl.Base}/ru/sitemap/SetDisplayAddresses?pwd={SitemapPassword}";
            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
            return Ok();
        }

        public IActionResult RunSetDishesSearchTerms(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();
            var url = $"{InternalUrl.Base}/ru/sitemap/SetDishesSearchTermsDp?pwd={SitemapPassword}";
            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
            return Ok();
        }

        public async Task<IActionResult> RegenerateRestaurantIframesCache(string pwd)
        {
            if (IsValidPwd(pwd)) await RegenerateRestaurantIframesCache();
            return Ok();
        }

        public async Task<IActionResult> RegenerateRestaurantIframeCache(string id)
        {
            await RegenerateRestaurantIframeCacheById(id);
            return Ok();
        }
        
        public async Task<IActionResult> RunDbBackup(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();

            var client = new HttpClient();
            await client.PostAsync(_core.Settings.BackupServiceUrl, new StringContent(""));
            client.Dispose();

            return Ok();
        }

        public async Task<IActionResult> SetDisplayAddresses(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();

            var restaurants = _db.Restaurants.ToList();
            var locales = GetLocales();

            foreach (var restaurant in restaurants)
            {
                var info = await _core.Restaurants.GetRestaurantInfo(restaurant.Id);
                if (string.IsNullOrWhiteSpace(restaurant.Address) || info == null) continue;

                info.DisplayAddress = new LocalizableString
                {
                    Values = locales.Select(x =>
                    {
                        return new LocalizableStringValue
                        {
                            Locale = x.Id,
                            Value = x.Id == "en" ? UrlGenerateService.TranslitWithCaseSave(restaurant.Address) : restaurant.Address
                        };
                    }).ToList()
                };
                await _core.Restaurants.UpdateRestaurantInfo(info);
            }

            return Ok();
        }

        public async Task<IActionResult> SetDishesSearchTerms(string pwd, string restaurantId)
        {
            if (!IsValidPwd(pwd)) return NotFound();

            var dishes = await Task.Run(() => string.IsNullOrEmpty(restaurantId) ? _db.Dishes.ToList() : _db.Dishes.Where(x => x.RestaurantId == restaurantId).ToList());
            var dishSeosList = await _db.DishSeo.FromSql(@"SELECT
              Id,
              ItemId,
              Url,
              Title,
              Description,
              Keywords,
              SeoText,
              RestaurantId,
              '' as SearchTerms
            FROM DishSeo", new Dictionary<string, object>(), false) as List<IDishSeo>;

            var resultDishSeos = new ConcurrentBag<IDishSeo>();

            Parallel.ForEach(dishes, dish =>
            {
                var seo = dishSeosList.Find(x => x.ItemId == dish.Id);
                if (seo == null || string.IsNullOrWhiteSpace(dish.Name.Get()) ||
                    string.IsNullOrWhiteSpace(dish.Name.Get("ru").Trim())) return;

                seo.SearchTerms = dish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(dish.Name.Get("ru"));
                resultDishSeos.Add(seo);
            });

            var resultSeosByRestaurant = resultDishSeos.GroupBy(x => x.RestaurantId).ToList();
            Parallel.ForEach(resultSeosByRestaurant, x => _db.DishSeo.Update(x).Wait());

            return Ok();
        }

        public async Task<IActionResult> SetDishesSearchTermsDp(string pwd)
        {
            if (!IsValidPwd(pwd)) return NotFound();

            var dishes = await _dapper.QueryAsync<DishNameAndSeoId>(@"SELECT Dishes.Name AS NameString, DishSeo.Id AS SeoId FROM DishSeo INNER JOIN Dishes ON DishSeo.ItemId = Dishes.Id");
            var results = new ConcurrentBag<Tuple<string, string>>();

            Parallel.ForEach(dishes, dish =>
            {
                if (!string.IsNullOrWhiteSpace(dish.Name.Get()))
                {
                    var searchTerms = $"{dish.Name.GetAll()} {UrlGenerateService.TranslitWithCaseSave(dish.Name.Get("ru"))}";
                    searchTerms = searchTerms.Replace("'", @"\'");
                    results.Add(new Tuple<string, string>(dish.SeoId, searchTerms));
                }
            });

            var queryBuilder = new StringBuilder();
            var processed = 0;
            var command = _dapper.CreateCommand();
            var currentBatch = 0;
            var resultsCount = results.Count;

            _dapper.Open();
            foreach (var result in results)
            {
                currentBatch++;
                queryBuilder.AppendLine($"UPDATE DishSeo SET SearchTerms = '{result.Item2}' WHERE Id = '{result.Item1}';");

                if (currentBatch == 5000 || processed + currentBatch == resultsCount)
                {
                    processed += currentBatch;
                    currentBatch = 0;

                    command.CommandText = queryBuilder.ToString();
                    command.ExecuteNonQuery();
                    queryBuilder = queryBuilder.Clear();
                }
            }
            
            _dapper.Close();

            return Ok(processed);
        }

        private async Task UpdateMainSitemap()
        {
            var crq = Request;
            var document = await System.IO.File.ReadAllTextAsync("wwwroot/sitemap.xml");
            var xml = XDocument.Parse(document);
            if (xml.Root == null) throw new InvalidOperationException("Sitemap is empty");

            xml.Root.RemoveAll();
            foreach (var locale in GetLocales())
            {
                string defaultSiteMapElement = "<sitemap></sitemap>";


                var sitemapElem = XElement.Parse(defaultSiteMapElement);
                var sitemapElemRest = XElement.Parse(defaultSiteMapElement);
                var sitemapElemDish = XElement.Parse(defaultSiteMapElement);
                var locElem = XElement.Parse("<loc></loc>");
                var lastModElem = XElement.Parse("<lastmod></lastmod>");
                var locElemRest = XElement.Parse("<loc></loc>");
                var lastModElemRest = XElement.Parse("<lastmod></lastmod>");
                var locElemDish = XElement.Parse("<loc></loc>");
                var lastModElemDish = XElement.Parse("<lastmod></lastmod>");

                lastModElem.Value = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                lastModElemRest.Value = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                lastModElemDish.Value = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

                locElem.Value = $"{SiteBaseUrl}/sitemap{ locale.Id}.xml";
                locElemRest.Value = $"{SiteBaseUrl}/sitemap{ locale.Id}restaurants.xml";

                int dishesCount = await _core.Dishes.GetAllDishesCount();
                int numberOfFiles = dishesCount / NUMBER_OF_RECORDS;

                sitemapElem.Add(locElem, lastModElem);
                sitemapElemRest.Add(locElemRest, lastModElemRest);


                xml.Root.Add(sitemapElem, sitemapElemRest);

                for (int dishFilesCounter = 0; dishFilesCounter <= numberOfFiles; dishFilesCounter++)
                {
                    sitemapElemDish = XElement.Parse(defaultSiteMapElement);
                    locElemDish = XElement.Parse("<loc></loc>");
                    locElemDish.Value = $"{SiteBaseUrl}/sitemap{ locale.Id}dishes_{ dishFilesCounter}.xml";
                    sitemapElemDish.Add(locElemDish, lastModElemDish);
                    xml.Root.Add(sitemapElemDish);
                }
            }

            document = replaceEmptyNamespace(xml.ToString());
            await System.IO.File.WriteAllTextAsync("wwwroot/sitemap.xml", document);
        }

        private async Task UpdatePagesSitemap()
        {
            var crq = Request;
            string defaultSiteMapElement = "<url></url>";

            foreach (var locale in GetLocales())
            {
                var document = await System.IO.File.ReadAllTextAsync($"wwwroot/sitemap{ locale.Id}.xml");
                var xml = XDocument.Parse(document);
                if (xml.Root == null) throw new InvalidOperationException("Sitemap is empty");
                xml.Root.RemoveAll();

                List<string> mainPages = new List<string>
                {
                    $"<loc>{SiteBaseUrl}/{ locale.Id}</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/articles/aboutus</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/articles/forowners</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/content/agreement</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/content/privacy</loc>",

                    $"<loc>{SiteBaseUrl}/{ locale.Id}/home/newest</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/home/featured</loc>",
                    $"<loc>{SiteBaseUrl}/{ locale.Id}/home/bestdishes</loc>"
                };


                // сторінки «страва + місто»: хаб кожного міста зі сторінками і самі сторінки
                var cities = (await _core.Restaurants.GetCities()).ToList();
                var dishCityPages = await new DishCityPageService(_efContext).GetAllPublished();
                foreach (var cityPages in dishCityPages.GroupBy(x => x.CityId))
                {
                    var citySlug = cities.FirstOrDefault(x => x.Id == cityPages.Key)?.Name.Get("en").ToLower();
                    if (citySlug == null) continue;
                    mainPages.Add($"<loc>{SiteBaseUrl}/{ locale.Id}/{citySlug}/dishes</loc>");
                    mainPages.AddRange(cityPages.Select(x => $"<loc>{SiteBaseUrl}/{ locale.Id}/{citySlug}/dishes/{x.Slug}</loc>"));
                }

                foreach (string page in mainPages)
                {
                    var urlElem = XElement.Parse(defaultSiteMapElement);
                    var locElem = XElement.Parse(page);
                    var lastModElem = XElement.Parse($"<lastmod>{DateTime.Now:yyyy-MM-dd}</lastmod>");
                    var changeFreqElem = XElement.Parse("<changefreq>weekly</changefreq>");
                    var priorityElem = XElement.Parse("<priority>0.8</priority>");

                    urlElem.Add(locElem, lastModElem, changeFreqElem, priorityElem);
                    xml.Root.Add(urlElem);
                }

                document = replaceEmptyNamespace(xml.ToString());
                await System.IO.File.WriteAllTextAsync($"wwwroot/sitemap{ locale.Id}.xml", document);
            }
        }

        private async Task UpdateDishSitemap()
        {
            var crq = Request;
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Select(x => new { x.Id, x.Url, x.City }).ToList();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            // тонкі напої закриті від індексації (noindex) - у sitemap їх немає
            var noindex = await new SeoNoindexService(_efContext).GetIds();
            // кількість файлів у sitemap.xml рахується від усіх страв - зайві файли записуються порожніми
            int indexedFiles = await _core.Dishes.GetAllDishesCount() / NUMBER_OF_RECORDS;

            foreach (var locale in GetLocales())
            {
                int itemCounter = 0;
                int dishFilesCounter = 0;
                int restarauntCounter = 0;

                string defaultSiteMapElement = "<url></url>";

                string defaultXmlFileContent = @"
                        <urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">

                        </urlset>
                    ";

                string filePath = $"wwwroot/sitemap{ locale.Id}dishes_{ dishFilesCounter}.xml";
                string document = string.Empty;
                if (System.IO.File.Exists(filePath))
                {
                    document = await System.IO.File.ReadAllTextAsync(filePath);
                }
                else
                {
                    document = defaultXmlFileContent;
                }

                var xml = XDocument.Parse(document);
                if (xml.Root == null) throw new InvalidOperationException("Sitemap is empty");
                xml.Root.RemoveAll();

                for(int restCounter = 0; restCounter < restaurants.Count; restCounter++)
                {
                    var restaurant = restaurants[restCounter];


                    var dishes = await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restaurant.Id, null);
                    foreach (var dish in dishes)
                    {
                        if (noindex.Contains(dish.Id)) continue;

                        if (itemCounter > NUMBER_OF_RECORDS)
                        {
                            itemCounter = 0;
                            dishFilesCounter++;

                            document = replaceEmptyNamespace(xml.ToString());
                            await System.IO.File.WriteAllTextAsync(filePath, document);

                            filePath = $"wwwroot/sitemap{ locale.Id}dishes_{ dishFilesCounter}.xml";
                            document = defaultXmlFileContent;

                            xml = XDocument.Parse(document);
                            if (xml.Root == null) throw new InvalidOperationException("Sitemap is empty");
                            xml.Root.RemoveAll();
                        }

                        // страва на межі файлів теж іде в sitemap (раніше пропускалась)
                        var urlElem = XElement.Parse(defaultSiteMapElement);
                        var locElem = XElement.Parse($"<loc>{SiteBaseUrl}/{ locale.Id}/{ cities.First(x => x.Id == restaurant.City).Name.Get("en").ToLower()}/restaurant/{ restaurant.Url}/{ dish.Url}</loc>");
                        var lastModElem = XElement.Parse($"<lastmod>{DateTime.Now:yyyy-MM-dd}</lastmod>");
                        var changeFreqElem = XElement.Parse("<changefreq>weekly</changefreq>");
                        var priorityElem = XElement.Parse("<priority>0.8</priority>");

                        urlElem.Add(locElem, lastModElem, changeFreqElem, priorityElem);
                        xml.Root.Add(urlElem);

                        itemCounter++;
                    }


                    //System.Diagnostics.Debug.WriteLine(string.Format("Dish Counter: {1}, Item Counter: {0}, restaraunt: {2}, restCounter: {3}", itemCounter, dishFilesCounter, restarauntCounter, restCounter));
                    restarauntCounter++;
                }

                document = replaceEmptyNamespace(xml.ToString());
                await System.IO.File.WriteAllTextAsync(filePath, document);

                for (int emptyFile = dishFilesCounter + 1; emptyFile <= indexedFiles; emptyFile++)
                {
                    await System.IO.File.WriteAllTextAsync($"wwwroot/sitemap{ locale.Id}dishes_{ emptyFile}.xml",
                        replaceEmptyNamespace(XDocument.Parse(defaultXmlFileContent).ToString()));
                }
            }
        }

        private async Task UpdateRestaurantSitemap()
        {
            var crq = Request;
            // порожні заклади закриті від індексації (noindex) - у sitemap їх немає
            var noindex = await new SeoNoindexService(_efContext).GetIds();
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Select(x => new { x.Id, x.Url, x.City }).ToList()
                .Where(x => !noindex.Contains(x.Id)).ToList();
            var cities = (await _core.Restaurants.GetCities()).ToList();

            string defaultSiteMapElement = "<url></url>";

            foreach (var locale in GetLocales())
            {
                var document = await System.IO.File.ReadAllTextAsync($"wwwroot/sitemap{ locale.Id}restaurants.xml");
                var xml = XDocument.Parse(document);
                if (xml.Root == null) throw new InvalidOperationException("Sitemap is empty");
                xml.Root.RemoveAll();

                foreach (var restaurant in restaurants)
                {
                    var urlElem = XElement.Parse(defaultSiteMapElement);
                    var locElem = XElement.Parse($"<loc>{SiteBaseUrl}/{ locale.Id}/{ cities.First(x => x.Id == restaurant.City).Name.Get("en").ToLower()}/restaurant/{ restaurant.Url}</loc>");
                    var lastModElem = XElement.Parse($"<lastmod>{DateTime.Now:yyyy-MM-dd}</lastmod>");
                    var changeFreqElem = XElement.Parse("<changefreq>weekly</changefreq>");
                    var priorityElem = XElement.Parse("<priority>0.8</priority>");

                    urlElem.Add(locElem, lastModElem, changeFreqElem, priorityElem);
                    xml.Root.Add(urlElem);
                }

                document = replaceEmptyNamespace(xml.ToString());
                await System.IO.File.WriteAllTextAsync($"wwwroot/sitemap{ locale.Id}restaurants.xml", document);
            }
        }

        private async Task RegenerateRestaurantIframeCacheById(string id)
        {
            var restaurant = _core.Restaurants.GetCompleteRestaurants().FirstOrDefault(x => x.Id == id);
            if (restaurant == null) return;

            var cities = (await _core.Restaurants.GetCities()).ToList();
            var digitalOceanSrvc = new DigitalOceanService(_config);
            var httpClient = new SitemapUpdateService();

            // Дані ресторану змінились - сторінка ресторану в кеші Cloudflare застаріла
            var citySlug = cities.First(x => x.Id == restaurant.City).Name.Get("en").ToLower();
            await EdgeCachePurge.Purge(_config,
                _locales.GetLocales().Select(locale => $"{SiteBaseUrl}/{locale.Id}/{citySlug}/restaurant/{restaurant.Url}"));

            foreach (var locale in _locales.GetLocales())
            {
                await ProcessRestaurantIframe(locale, cities, restaurant.City, restaurant.Url, httpClient, digitalOceanSrvc);
            }
        }

        private async Task RegenerateRestaurantIframesCache()
        {
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Select(x => new { x.Url, x.City }).ToList();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var digitalOceanSrvc = new DigitalOceanService(_config);
            var httpClient = new SitemapUpdateService();

            foreach (var locale in _locales.GetLocales())
            {
                foreach (var restaurant in restaurants)
                {
                    await ProcessRestaurantIframe(locale, cities, restaurant.City, restaurant.Url, httpClient, digitalOceanSrvc);
                }
            }
        }

        private static async Task ProcessRestaurantIframe(ILocale locale, List<ICity> cities, string restaurantCity, string restaurantUrl,
            SitemapUpdateService httpClient, DigitalOceanService digitalOceanSrvc)
        {
            var url =
                $"/{locale.Id}/{cities.First(x => x.Id == restaurantCity).Name.Get("en").ToLower()}/iframe/clean/{restaurantUrl}";

            var html = await httpClient.Get($"{InternalUrl.Base}{url}");
            var htmlMobile = await httpClient.Get($"{InternalUrl.Base}{url}?device=mobile", RequestDeviceType.Mobile);

            html = html.Replace("https://tipmymenu.com/ru/articles/forowners", "###FOR-OWNERS###");
            html = html.Replace(InternalUrl.Base, "https://frm.tipmymenu.com").Replace("localhost:5001", "frm.tipmymenu.com");
            html = html.Replace("clean/", "");
            html = html.Replace("https://tipmymenu.com", Img.MediaBaseUrl);
            html = html.Replace("###FOR-OWNERS###", "https://tipmymenu.com/ru/articles/forowners");

            htmlMobile = htmlMobile.Replace("https://tipmymenu.com/ru/articles/forowners", "###FOR-OWNERS###");
            htmlMobile = htmlMobile.Replace(InternalUrl.Base, "https://frm.tipmymenu.com").Replace("localhost:5001", "frm.tipmymenu.com");
            htmlMobile = htmlMobile.Replace("clean/", "");
            htmlMobile = htmlMobile.Replace("https://tipmymenu.com", Img.MediaBaseUrl);
            htmlMobile = htmlMobile.Replace("###FOR-OWNERS###", "https://tipmymenu.com/ru/articles/forowners");

            var name =
                $"{locale.Id}/{cities.First(x => x.Id == restaurantCity).Name.Get("en").ToLower()}/iframe/{restaurantUrl}"
                    .Replace("/", "_");

            await digitalOceanSrvc.UploadFile(Encoding.UTF8.GetBytes(html), $"iframes/{name}.html");
            await digitalOceanSrvc.UploadFile(Encoding.UTF8.GetBytes(htmlMobile), $"iframes/{name}_mob.html");
        }

        private async Task RegenerateDishSeo()
        {
            var dishes = await Task.Run(() => _db.Dishes.GroupBy(x => x.RestaurantId).ToList());
            var dishSeos = await _db.DishSeo.FromSql(@"SELECT
              Id,
              ItemId,
              Url,
              Title,
              Description,
              Keywords,
              '' as SeoText,
              RestaurantId,
              SearchTerms
            FROM DishSeo", new Dictionary<string, object>(), false) as List<IDishSeo>;

            var resultDishSeos = new ConcurrentBag<IDishSeo>();

            Parallel.ForEach(dishes, dishesByRestaurant => ProcessDishSeoByRestaurant(resultDishSeos, dishesByRestaurant, dishSeos));

            var resultSeosByRestaurant = resultDishSeos.GroupBy(x => x.RestaurantId).ToList();
            Parallel.ForEach(resultSeosByRestaurant, x => _db.DishSeo.Update(x).Wait());
        }

        private void ProcessDishSeoByRestaurant(ConcurrentBag<IDishSeo> result, IEnumerable<IDish> dishes,
            List<IDishSeo> seos)
        {
            var restaurantSeos = seos.FindAll(x => x.RestaurantId == dishes.First().RestaurantId);
            foreach (var dish in dishes)
            {
                var seo = restaurantSeos.Find(x => x.ItemId == dish.Id);

                if (seo != null)
                {
                    seo.Url = UrlGenerateService.Translit(dish.Name.Get());
                    var possibleAddOnString = seo.Url;
                    var addOnSubstrings =
                        !string.IsNullOrEmpty(possibleAddOnString) ? possibleAddOnString.Split("-") : new string[0];
                    var currentAddOnSubstringIndex = 0;

                    while (true)
                    {
                        var existingSeoUrl = restaurantSeos.Find(x => x.Url == seo.Url && x.RestaurantId == seo.RestaurantId && x.Id != seo.Id);

                        if (existingSeoUrl == null) break;

                        if (addOnSubstrings.Length > 0 && currentAddOnSubstringIndex < addOnSubstrings.Length)
                        {
                            seo.Url += $"-{addOnSubstrings[currentAddOnSubstringIndex]}";
                            currentAddOnSubstringIndex++;
                        }
                        else seo.Url += "1";
                    }

                    result.Add(seo);
                }
            }
        }

        private IEnumerable<ILocale> GetLocales()
        {
            IEnumerable<ILocale> locales = _locales.GetLocales();
            locales = locales.Where(x => !Convert.ToBoolean(_config[$"DisableLanguages:{x.Id}"] != null ? _config[$"DisableLanguages:{x.Id}"].ToLower() : "false")).ToList();

            return locales;
        }

        private string replaceEmptyNamespace(string source)
        {
            if (!string.IsNullOrEmpty(source))
            {
                source = source.Replace(" xmlns=\"\"", "");
            }

            return source;
        }

        

        public ActionResult DishSeo(string pwd)
        {
            if (IsValidPwd(pwd))
            {
                return View();
            }
            else
            {
                return NotFound();
            }
        }

        public ActionResult GetListOfRestaraunts(string pwd)
        {
            if (IsValidPwd(pwd))
            {
                var restaurants = _core.Restaurants.GetCompleteRestaurants().Select(x => new { x.Id }).ToList();
                //var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => x.Id == "0d282a84-2580-4784-9706-98338a647431").Select(x => new { x.Id }).ToList();

                return Ok(restaurants);
            }
            else
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> ProcessDishSeoForRestaraunt(string restarauntId)
        {
            //var restaurants = _core.Restaurants.GetCompleteRestaurants().Select(x => new { x.Id }).ToList();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            var oldSeo = "";

            var restaraunt = await _core.Restaurants.GetRestaurantById(restarauntId);
            var dishes = (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restarauntId, null)).ToList();

            var parameters = new Dictionary<string, object>
            {
                { "@restarauntId", restarauntId }
            };
            
            var dishSeos = await _db.DishSeo.FromSql(@"SELECT
                  Id,
                  ItemId,
                  Url,
                  Title,
                  Description,
                  Keywords,
                  SeoText,
                  RestaurantId,
                  SearchTerms
                FROM DishSeo Where RestaurantId = @restarauntId", parameters, false) as List<IDishSeo>;

            sb.AppendFormat("<b>{1}: {0}</b><br />", restaraunt.Name, restaraunt.Id);

            foreach (var dish in dishes)
            {
                var seo = dishSeos.FirstOrDefault(x => x.ItemId == dish.Id);

                if (seo != null)
                {
                    oldSeo = seo.Url;
                    //Check of url contains numbers or copy
                    //if contains -- run procedure of replacement -- run procedure of new name
                    
                    seo.Url = _urls.GetNewUrlForDish(seo, dish, dishSeos);

                    if (oldSeo != seo.Url)
                    {
                        await _core.Dishes.UpdateSeoWithoutChecks(seo);
                        sb.AppendFormat("{0}: Old - {1} New - {2}<br />", dish.Name.Get("ru"), oldSeo, seo.Url);
                    }
                }
            }

            return Ok(sb.ToString());
        }
    }
}