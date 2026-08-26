// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface ILogItem
    {
        string Id { get; set; }
        string UserId { get; set; }
        string DeviceId { get; set; }
        string Event { get; set; }
        string EventData { get; set; }
        DateTime Timestamp { get; set; }
    }
}
