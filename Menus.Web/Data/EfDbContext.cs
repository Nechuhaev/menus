// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data
{
    public class EfDbContext : IdentityDbContext<ApplicationUser>
    {
        public EfDbContext(DbContextOptions<EfDbContext> options)
            : base(options)
        {
        }

        public DbSet<Locale> Locales { get; set; }
        public DbSet<Restaurant> Restaurants { get; set; }
        public DbSet<RestaurantRole> RestaurantRoles { get; set; }
        public DbSet<DbFile> Files { get; set; }
        public DbSet<MenuCategory> MenuCategories { get; set; }
        public DbSet<Dish> Dishes { get; set; }
        public DbSet<DishCategory> DishCategories { get; set; }
        public DbSet<DishSeo> DishSeo { get; set; }
        public DbSet<RestaurantSeo> RestaurantSeo { get; set; }
        public DbSet<RestaurantInfo> RestaurantInfo { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<AnonymousRating> AnonymousRatings { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<DishVariation> DishVariations { get; set; }
        public DbSet<TypoRequest> TypoRequests { get; set; }
        public DbSet<DishPhoto> DishPhotos { get; set; }
        public DbSet<LogItem> LogItems { get; set; }
        public DbSet<Page> Pages { get; set; }
        public DbSet<MenuPhoto> MenuPhotos { get; set; }
        public DbSet<MenuCategoryType> MenuCategoryTypes { get; set; }
        public DbSet<CustomerRequest> CustomerRequests { get; set; }
        public DbSet<MenuCategoryRelation> MenuCategoryRelations { get; set; }
        public DbSet<AbTest> AbTests { get; set; }
        public DbSet<AbTestParameter> AbTestParameters { get; set; }
        public DbSet<Promo> Promos { get; set; }

        //public DbSet<SearchDish> SearchDishes { get; set; }
        public virtual DbQuery<AnonymousRatingResults> AnonymousRatingResults { get; set; }
        public virtual DbQuery<DishWithSeoAndRatings> DishesWithSeoAndRatings { get; set; }
        public virtual DbQuery<DishRatingWithUser> DishRatingWithUsers { get; set; }
        public virtual DbQuery<UserRating> UserRatings { get; set; }
        public virtual DbQuery<CompleteRestaurant> CompleteRestaurants { get; set; }
        public virtual DbQuery<AutocompleteDish> AutocompleteDishes { get; set; }
        public virtual DbQuery<CompleteDish> CompleteDishes { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<MenuCategoryRelation>().HasKey(x => new {x.ParentId, x.ChildId});

            builder.Entity<AbTest>().HasMany(x => x.Parameters).WithOne(x => x.Test).HasForeignKey(x => x.TestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<MenuCategory>().HasMany(x => x.ChildCategoriesRelations).WithOne(x => x.Parent)
                .HasForeignKey(x => x.ParentId);
            builder.Entity<MenuCategory>().HasMany(x => x.ParentCategoriesRelations).WithOne(x => x.Child)
                .HasForeignKey(x => x.ChildId);

            builder.Entity<Rating>().HasOne(x => x.Parent).WithMany(x => x.ChildRatings).HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Query<SearchDish>().ToView("SearchDishes");
            builder.Query<DishPhotoExtended>().ToView("DishPhotoExtended");

            Promo.OnModelCreating(builder);
        }
    }
}
