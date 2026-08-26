// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Microsoft.AspNetCore.Identity;

namespace Menus.Web.Models
{
    public class ApplicationUser : IdentityUser, IUser
    {
        public UserStatus Status { get; set; }
        public UserRole Role { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneConfirmationCode { get; set; }
        public string ImageUrl { get; set; }
    }
}
