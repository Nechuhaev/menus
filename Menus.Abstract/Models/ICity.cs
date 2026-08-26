// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface ICity
    {
        string Id { get; set; }
        ILocalizableString Name { get; set; }
        string Latitude { get; set; }
        string Longitude { get; set; }
    }
}
