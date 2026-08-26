// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Microsoft.AspNetCore.Http;

namespace Menus.Web.Models.ViewModels
{
    public class RegisterRestaurantModel : Restaurant
    {
        public IFormFile RegistrationFile { get; set; }
        public IFormFile LicenseFile { get; set; }
        public Restaurant GetRestaurant()
        {
            var result = new Restaurant();
            var properties = result.GetType().GetProperties();
            foreach (var prop in properties)
            {
                prop.SetValue(result, prop.GetValue(this));
            }

            return result;
        }
    }
}
