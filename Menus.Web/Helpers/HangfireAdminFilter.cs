using Hangfire.Dashboard;

namespace Menus.Web.Helpers
{
    // Панель Hangfire - лише для адмінів сайту (вхід через адмінку). Без фільтра Hangfire пускав тільки
    // запити з самого сервера, тож ззовні панель була недоступна
    public class HangfireAdminFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var user = context.GetHttpContext().User;
            return user?.Identity?.IsAuthenticated == true && user.IsInRole("admin");
        }
    }
}
