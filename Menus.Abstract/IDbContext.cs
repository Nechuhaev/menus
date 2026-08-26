// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Abstract
{
    public interface IDbContext
    {
        IRepository<IUser> Users { get; }
        IRepository<ILocale> Locales { get; }
        IRepository<IRestaurant> Restaurants { get; }
        IRepository<IRestaurantRole> RestaurantRoles { get; }
        IRepository<IDbFile> Files { get; }
        IRepository<IMenuCategory> MenuCategories { get; }
        IRepository<IDish> Dishes { get; }
        IRepository<IDishCategory> DishCategories { get; }
        IRepository<IDishSeo> DishSeo { get; }
        IRepository<IRestaurantSeo> RestaurantSeo { get; }
        IRepository<IRestaurantInfo> RestaurantInfo { get; }
        IRepository<IRating> Ratings { get; }
        IRepository<IAnonymousRating> AnonymousRatings { get; }
        IRepository<ICity> Cities { get; }
        IRepository<IDishVariation> DishVariations { get; }
        IRepository<ITypoRequest> TypoRequests { get; }
        IRepository<ISearchDish> SearchDishes { get; }
        IRepository<IDishPhotoExtended> DishPhotoExtendeds { get; }
        IRepository<IMenuCategoryRelation> MenuCategoryRelations { get; }
        IRepository<IDishPhoto> DishPhotos { get; }
        IRepository<ILogItem> LogItems { get; }
        IRepository<IPage> Pages { get; }
        IRepository<IMenuPhoto> MenuPhotos { get; }
        IRepository<ICustomerRequest> CustomerRequests { get; }
        IRepository<IMenuCategoryType> MenuCategoryTypes { get; }
        IRepository<IPromo> Promos { get; }
        IProcedureRepository<IAnonymousRatingResults> AnonymousRatingResults { get; }
        IProcedureRepository<IDishWithSeoAndRatings> DishesWithSeoAndRatings { get; }
        IProcedureRepository<IDishRatingWithUser> DishRatingWithUsers { get; }
        IProcedureRepository<IUserRating> UserRatings { get; }
        ISqlRepository<ICompleteRestaurant> CompleteRestaurants { get; }
        ISqlRepository<IAutocompleteDish> AutocompleteDishes { get; }
        ISqlRepository<ICompleteDish> CompleteDishes { get; }
    }
}
