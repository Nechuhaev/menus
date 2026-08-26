using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Menus.Web.Services
{
    public class SitemapUpdateService
    {
        public void Update(string url)
        {
            GetRequest(url).Wait();
        }

        public async Task<string> Get(string url, RequestDeviceType device = RequestDeviceType.Desktop)
        {
            var result = await GetRequest(url, device);
            return await result.Content.ReadAsStringAsync();
        }

        private async Task<HttpResponseMessage> GetRequest(string url, RequestDeviceType device = RequestDeviceType.Desktop)
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback =
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(60)
            };

            if (device == RequestDeviceType.Mobile)
            {
                client.DefaultRequestHeaders.Remove("UserAgent");
                client.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Linux; Android 8.0.0; Pixel 2 XL Build/OPD1.170816.004) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.97 Mobile Safari/537.36");
            }
            var result = await client.GetAsync(url);

            client.Dispose();
            handler.Dispose();

            return result;
        }
    }

    public enum RequestDeviceType
    {
        Desktop,
        Mobile
    }
}
