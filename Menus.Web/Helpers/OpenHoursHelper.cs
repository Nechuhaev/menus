using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Helpers
{
    public static class OpenHoursHelper
    {
        // OpenHours зберігається як JSON {"perDay":bool,"data":[{from,to},...]},
        // але імпорт і content-bot записують текст (Google, OSM). Відомий текстовий формат
        // перетворюється на графік по днях, невідомий повертається як є.
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
                var week = ParseText(raw);
                return week != null ? (week.ToString(Formatting.None), true) : (raw.Trim(), false);
            }
        }

        private static readonly string[] GoogleDays = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

        // Скорочення днів: OSM, українські, російські
        private static readonly Dictionary<string, int> DayTokens = new Dictionary<string, int>
        {
            ["mo"] = 0, ["tu"] = 1, ["we"] = 2, ["th"] = 3, ["fr"] = 4, ["sa"] = 5, ["su"] = 6,
            ["пн"] = 0, ["вт"] = 1, ["ср"] = 2, ["чт"] = 3, ["пт"] = 4, ["сб"] = 5, ["нд"] = 6, ["вс"] = 6
        };

        private const string Time = @"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?";
        private static readonly Regex RangeRe = new Regex("^" + Time + @"\s*[-–—]\s*" + Time + "$", RegexOptions.IgnoreCase);

        // Текстовий графік -> тиждень з понеділка [{active, from, to}]; null, якщо формат невідомий
        // або в дні кілька проміжків (перерву в одному проміжку не показати).
        // Формати: Google «Monday: 12:00 PM – 12:00 AM, ...», OSM «Mo-Fr 10:00-22:00; Sa,Su off», «Пн-Нд 10:00-22:00», «24/7».
        public static JArray ParseText(string raw)
        {
            var text = Regex.Replace(raw ?? "", @"[\u00A0\u2009\u202F]", " ").Trim();
            if (text.Length == 0) return null;
            if (text == "24/7") return Week(Enumerable.Repeat<(string From, string To)?>(("00:00", "24:00"), 7).ToArray());

            var days = new (string From, string To)?[7];
            var seen = new bool[7];

            var google = Regex.Matches(text, @"(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s*:\s*(.*?)(?=[,;\n]?\s*(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s*:|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (google.Count > 0)
            {
                foreach (Match m in google)
                {
                    var i = Array.IndexOf(GoogleDays, m.Groups[1].Value.ToLowerInvariant());
                    if (!TryHours(m.Groups[2].Value.Trim().TrimEnd(',', ';'), out var hours)) return null;
                    days[i] = hours;
                    seen[i] = true;
                }
                return seen.All(x => x) ? Week(days) : null;
            }

            foreach (var rule in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(rule.Trim(), @"^(?:([a-zа-яії]{2}(?:\s*[-–,]\s*[a-zа-яії]{2})*)\s*:?\s+)?(.+)$", RegexOptions.IgnoreCase);
                if (!m.Success || !TryHours(m.Groups[2].Value.Trim(), out var hours)) return null;
                var targets = m.Groups[1].Success ? DaysOf(m.Groups[1].Value) : Enumerable.Range(0, 7).ToList();
                if (targets == null) return null;
                foreach (var i in targets) { days[i] = hours; seen[i] = true; }
            }
            return seen.Any(x => x) ? Week(days) : null;
        }

        private static List<int> DaysOf(string spec)
        {
            var result = new List<int>();
            foreach (var part in spec.ToLowerInvariant().Split(','))
            {
                var ends = Regex.Split(part.Trim(), @"\s*[-–]\s*");
                if (ends.Length > 2 || ends.Any(e => !DayTokens.ContainsKey(e))) return null;
                var from = DayTokens[ends[0]];
                var to = DayTokens[ends[ends.Length - 1]];
                for (var i = from; ; i = (i + 1) % 7)
                {
                    result.Add(i);
                    if (i == to) break;
                }
            }
            return result;
        }

        // null-значення в hours означає вихідний
        private static bool TryHours(string value, out (string From, string To)? hours)
        {
            hours = null;
            var v = value.ToLowerInvariant();
            if (v == "closed" || v == "off" || v == "вихідний" || v == "выходной") return true;
            if (v == "open 24 hours" || v == "24 hours" || v == "цілодобово" || v == "круглосуточно")
            {
                hours = ("00:00", "24:00");
                return true;
            }
            var m = RangeRe.Match(value);
            if (!m.Success) return false;
            var from = Clock(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            var to = Clock(m.Groups[4].Value, m.Groups[5].Value, m.Groups[6].Value);
            if (from == null || to == null) return false;
            hours = (from, to);
            return true;
        }

        private static string Clock(string hour, string minute, string ampm)
        {
            var h = int.Parse(hour);
            var min = string.IsNullOrEmpty(minute) ? 0 : int.Parse(minute);
            if (!string.IsNullOrEmpty(ampm))
            {
                if (h < 1 || h > 12) return null;
                h = h % 12 + (ampm.ToLowerInvariant() == "pm" ? 12 : 0);
            }
            if (h > 24 || min > 59 || (h == 24 && min > 0)) return null;
            return $"{h:00}:{min:00}";
        }

        private static JArray Week((string From, string To)?[] days) =>
            new JArray(days.Select(d => d.HasValue
                ? new JObject { ["active"] = true, ["from"] = d.Value.From, ["to"] = d.Value.To }
                : new JObject { ["active"] = false, ["from"] = "", ["to"] = "" }));
    }
}
