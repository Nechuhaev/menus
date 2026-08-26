// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Domain.Dishes;
using Menus.Domain.Files;
using Menus.Domain.LogItems;
using Menus.Domain.Pages;
using Menus.Domain.Ratings;
using Menus.Domain.Restaurants;
using Menus.Domain.Typos;
using Menus.Domain.Users;
using Microsoft.Extensions.Configuration;

namespace Menus.Domain
{
    public class Core
    {
        public Core(IDbContext dbContext, CoreSettings settings)
        {
            _db = dbContext;
            Settings = settings;
            Users = new CoreUsers(dbContext, this);
            Restaurants = new CoreRestaurants(dbContext, this);
            Files = new CoreFiles(dbContext, this);
            Dishes = new CoreDishes(dbContext, this);
            Ratings = new CoreRatings(dbContext, this);
            Typos = new CoreTypos(dbContext, this);
            LogItems = new CoreLogItems(dbContext, this);
            Pages = new CorePages(dbContext, this);
        }

        private IDbContext _db;

        public readonly CoreSettings Settings;

        public CoreUsers Users { get; }
        public CoreRestaurants Restaurants { get; }
        public CoreFiles Files { get; }
        public CoreDishes Dishes { get; }
        public CoreRatings Ratings { get; }
        public CoreTypos Typos { get; }
        public CoreLogItems LogItems { get; }
        public CorePages Pages { get; }
    }

    public class CoreSettings
    {
        public CoreSettings(IConfiguration config)
        {
            ImgServiceUrl = config["ImgServiceUrl"];
            BackupServiceUrl = config["BackupServiceUrl"];
            IframeCacheServiceUrl = config["IframeCacheServiceUrl"];
        }
        public string ImgServiceUrl { get; set; }
        public string BackupServiceUrl { get; set; }
        public string IframeCacheServiceUrl { get; set; }
    }
}
