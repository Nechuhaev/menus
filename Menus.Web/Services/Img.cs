using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Menus.Web.Services
{
    public static class Img
    {
        // Публічна адреса сховища медіа (CDN), задається з Media:BaseUrl при старті
        public static string MediaBaseUrl { get; private set; } = "https://cdn-tipmymenus-com.fra1.digitaloceanspaces.com";
        public static string BaseUrl => $"{MediaBaseUrl}/files";

        public static void Configure(string mediaBaseUrl)
        {
            if (!string.IsNullOrWhiteSpace(mediaBaseUrl)) MediaBaseUrl = mediaBaseUrl.Trim().TrimEnd('/');
        }

        public static string Url(string id, Quality quality)
        {
            id = GetGuid(id);

            var suffix = "";
            switch (quality)
            {
                case Quality.High:
                    suffix = "large";
                    break;

                case Quality.Medium:
                    suffix = "medium";
                    break;

                case Quality.Low:
                    suffix = "small";
                    break;
            }
            return $"{BaseUrl}/{id}-{suffix}.jpg";
            //return $"{BaseUrl}/{id}.png";
        }

        private static string GetGuid(string input)
        {
            if (input == null) return "";
            Regex regex = new Regex("[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}");
            Match match = regex.Match(input);
            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }
    }

    public enum Quality
    {
        High,
        Medium,
        Low
    }
}
