// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class LogItem : ILogItem
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string DeviceId { get; set; }
        public string Event { get; set; }
        public string EventData { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
