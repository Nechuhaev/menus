using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Helpers
{
    public static class OpenHoursHelper
    {
        // OpenHours зберігається як JSON {"perDay":bool,"data":[{from,to},...]},
        // але content-bot записує текст у форматі OSM ("Mo-Su 10:00-22:00").
        // Для тексту повертаємо його як є, щоб сторінка ресторану не падала з 500.
        public static (string OpenHours, bool SchedulePerDay) Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ("", false);

            try
            {
                var openHours = JsonConvert.DeserializeObject<JObject>(raw);
                var schedulePerDay = openHours["perDay"].Value<bool>();
                var openHoursData = openHours["data"].Value<JArray>();
                if (schedulePerDay) return (openHoursData.ToString(), true);

                var day = openHoursData[0];
                return ($"{day["from"].Value<string>()} - {day["to"].Value<string>()}", false);
            }
            catch (Exception)
            {
                return (raw.Trim(), false);
            }
        }
    }
}
