namespace Menus.Web.Helpers
{
    public static class InternalUrl
    {
        // Фонові задачі Hangfire звертаються до цього ж застосунку всередині контейнера.
        // У продакшені Kestrel слухає http (deploy.yml: ASPNETCORE_URLS=http://+:5001),
        // тому https://localhost:5001 падав на SSL-рукостисканні
        public const string Base = "http://localhost:5001";
    }
}
