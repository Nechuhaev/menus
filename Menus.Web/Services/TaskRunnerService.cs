using Hangfire;
using Menus.Abstract;
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

        public void RunUpdateRestaurantIframeCache(string restaurantId)
        {
            RunTaskByUrl($"https://localhost:5001/ru/sitemap/RegenerateRestaurantIframeCache?id={restaurantId}");
        }
    }
}
