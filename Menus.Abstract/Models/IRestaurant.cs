// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

//TODO: Last updated date

using System;

namespace Menus.Abstract.Models
{
    public interface IRestaurant : ICloneable
    {
        string Id { get; set; }
        string Name { get; set; }
        string Email { get; set; }
        string Phone { get; set; }
        string Address { get; set; }
        string City { get; set; }
        string RegistrationLink { get; set; }
        string LicenseLink { get; set; }
        RestaurantStatus Status { get; set; }
        DateTime UpdateDate { get; set; }
        DateTime CreateDate { get; set; }
        bool Moderated { get; set; }
        bool IsModeratable { get; set; }
    }

    public enum RestaurantStatus
    {
        Active = 0,
        Pending = 1,
        Blocked = 2,
        Closed = 3
    }

    public enum RestaurantCreatedBy
    {
        Admin = 0,
        User = 1
    }
}
