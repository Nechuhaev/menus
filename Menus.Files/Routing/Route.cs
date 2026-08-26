namespace Menus.Files.Routing
{
    public class Route : IRoute
    {
        public Route(string url, string method)
        {
            Url = url;
            Method = method;
        }

        public string Url { get; set; }

        public string Method { get; set; }
    }
}
