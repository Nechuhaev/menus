// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface IUser
    {
        DateTimeOffset? LockoutEnd { get; set; }
        bool TwoFactorEnabled { get; set; }
        bool PhoneNumberConfirmed { get; set; }
        string PhoneNumber { get; set; }
        string ImageUrl { get; set; }
        string ConcurrencyStamp { get; set; }
        string SecurityStamp { get; set; }
        string PasswordHash { get; set; }
        bool EmailConfirmed { get; set; }
        string NormalizedEmail { get; set; }
        string Email { get; set; }
        string NormalizedUserName { get; set; }
        string UserName { get; set; }
        string Id { get; set; }
        bool LockoutEnabled { get; set; }
        UserStatus Status { get; set; }
        UserRole Role { get; set; }
        string FirstName { get; set; }
        string LastName { get; set; }
        string PhoneConfirmationCode { get; set; }
    }


    public enum UserStatus {
        Active = 0,
        Pending = 1,
        Blocked = 2
    }

    public enum UserRole
    {
        User = 0,
        RestaurantManager = 1,
        Restaurant = 2,
        Manager = 3,
        Admin = 4
    }

    public enum UserMode
    {
        User = 0,
        Restaurant = 1,
    }
}
