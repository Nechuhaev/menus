using System;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Menus.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Models
{
    public class Promo : IPromo
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        public string Name { get; set; }
        public string Action { get; set; }


        [FromForm]
        public LocalizableString Image { get; set; } = new LocalizableString();

        [NotMapped]
        ILocalizableString IPromo.Image
        {
            get => Image;
            set => Image = (LocalizableString)value;
        }


        public static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Promo>()
                .Property(x => x.Image)
                .HasJsonConversion();
        }
    }
}