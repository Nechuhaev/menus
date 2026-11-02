using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Dapper;
using Menus.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Controllers
{
    // /sitemap-recrawl.xml (Startup переписує адресу сюди): сторінки, що зникли з видачі й знову відкриваються,
    // з датою повернення - щоб Google переобійшов їх швидше. Список щодня пише content-bot/recrawl_pages.py
    public class SeoRecrawlController : Controller
    {
        private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        private readonly IDbConnection _connection;

        public SeoRecrawlController(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private class Row
        {
            public string Page { get; set; }
            public DateTime ReturnedAt { get; set; }
        }

        public async Task<IActionResult> Sitemap()
        {
            List<Row> rows;
            try
            {
                rows = (await _connection.QueryAsync<Row>("SELECT Page, ReturnedAt FROM SeoRecrawl ORDER BY Impressions DESC")).ToList();
            }
            catch (Exception)
            {
                rows = new List<Row>(); // таблицю ще не створив бот
            }

            var urlset = new XElement(Ns + "urlset", rows.Select(r => new XElement(Ns + "url",
                new XElement(Ns + "loc", r.Page),
                new XElement(Ns + "lastmod", r.ReturnedAt.ToString("yyyy-MM-dd")))));
            return Content(new XDocument(new XDeclaration("1.0", "utf-8", null), urlset).Declaration + "\n" + urlset,
                "application/xml; charset=utf-8");
        }
    }
}
