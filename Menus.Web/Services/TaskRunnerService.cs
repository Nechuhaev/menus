using System;
using System.Collections.Concurrent;
using Hangfire;
using Menus.Abstract;
using Menus.Web.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace Menus.Web.Services
{
    public class TaskRunnerService : ITaskRunner
    {
        private readonly IHostingEnvironment _env;

        public TaskRunnerService(IHostingEnvironment env)
        {
            _env = env;
        }

        public void RunTaskByUrl(string url)
        {
            if (_env.IsDevelopment()) return;

            BackgroundJob.Enqueue<SitemapUpdateService>(x => x.Update(url));
        }

        // Кожна зміна страви чи ресторану просить перебудувати iframe ресторану. Раніше - окрема задача на кожну
        // зміну: застосування 50 страв давало 50 однакових задач, вони паралельно рендерили сторінки й писали той
        // самий файл, забирали всі з'єднання з базою, і сайт лягав (02.11.2026: 528 задач за годину).
        // Тепер - одна відкладена задача на ресторан: вона виконується через IframeDelay і бачить усі зміни за цей час
        private static readonly TimeSpan IframeDelay = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, DateTime> IframeScheduled = new ConcurrentDictionary<string, DateTime>();

        public void RunUpdateRestaurantIframeCache(string restaurantId)
        {
            if (_env.IsDevelopment() || string.IsNullOrEmpty(restaurantId)) return;

            var now = DateTime.UtcNow;
            var runAt = now + IframeDelay;
            var scheduled = IframeScheduled.AddOrUpdate(restaurantId, runAt, (_, existing) => existing > now ? existing : runAt);
            if (scheduled != runAt) return; // задача для цього ресторану вже чекає й підхопить і цю зміну

            var url = $"{InternalUrl.Base}/ru/sitemap/RegenerateRestaurantIframeCache?id={restaurantId}";
            BackgroundJob.Schedule<SitemapUpdateService>(x => x.Update(url), IframeDelay);
        }
    }
}
