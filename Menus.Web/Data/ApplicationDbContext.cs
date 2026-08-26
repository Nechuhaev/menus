// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Data;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Data.Repositories;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Menus.Web.Data
{
    public class ApplicationDbContext : IDbContext
    {
        public ApplicationDbContext(EfDbContext efContext, ITaskRunner taskRunner)
        {
            _userRepository = new UserRepository(efContext);
            _localeRepository = new LocaleRepository(efContext);
            _restaurantRepository = new RestaurantRepository(efContext, taskRunner);
            _restaurantRolesRepository = new RestaurantRoleRepository(efContext);
            _fileRepository = new DbFileRepository(efContext);
            _menuCategoryRepository = new MenuCategoryRepository(efContext, taskRunner);
            _dishRepository = new DishRepository(efContext, taskRunner);
            _dishCategoryRepository = new DishCategoryRespository(efContext);
            _dishSeoRepository = new DishSeoRepository(efContext);
            _restaurantSeoRepository = new RestaurantSeoRepository(efContext, taskRunner);
            _restaurantInfoRepository = new RestaurantInfoRepository(efContext, taskRunner);
            _ratingRepository = new RatingRepository(efContext);
            _anonymousRatingRepository = new AnonymousRatingRepository(efContext);
            _cityRepository = new CityRepository(efContext);
            _anonymousRatingResultsgRepository = new AnonymousRatingResultsRepository(efContext,
                "CALL menus.GetAnonymousRatingValidation(@deviceId, @ipAddress, @dishId, @restaurantId)",
                "CALL menus.GetAnonymousRatingValidation(@deviceId, @ipAddress, @dishId, @restaurantId)");
            _dishWithSeoAndRatingsRepository = new DishWithSeoAndRatingsRepository(efContext,
                "CALL menus.GetDishesForRestaurantWithSeoAndRatings(@restaurantId, @userId)",
                "CALL menus.GetDishWithSeoAndRatings(@dishId, @userId)");
            _dishRatingWithUserRepository = new DishRatingWithUserRepository(efContext,
                "CALL GetRatingsForDishWithUsers(@dishId)");
            _userRatingRepository = new UserRatingRepository(efContext,
               "CALL GetUserRatings(@userId, @status)");
            _dishVariationRepository = new DishVariationRepository(efContext);
            _typoRequestRepository = new TypoRequestRepository(efContext);
            _searchDishRepository = new SearchDishRepository(efContext);
            _dishPhotoExtendedRepository = new DishPhotoExtendedRepository(efContext);
            _dishPhotoRepository = new DishPhotoRepository(efContext);
            _logItemRepository = new LogItemRepository(efContext);
            _pageRepository = new PageRepository(efContext);
            _menuPhotoRepository = new MenuPhotoRepository(efContext);
            _menuCategoryTypeRepository = new MenuCategoryTypeRepository(efContext);
            _completeRestaurantRepository = new CompleteRestaurantRepository(efContext);
            _autocompleteDishRepository = new AutocompleteDishRepository(efContext);
            _completeDishRepository = new CompleteDishRepository(efContext);
            _customerRequestRepository = new CustomerRequestRepository(efContext);
            _menuCategoryRelations = new MenuCategoryRelationRepository(efContext);
            _promoRepository = new DefaultEfRepository<Promo, IPromo>(efContext);
        }


        private readonly UserRepository _userRepository;
        private readonly LocaleRepository _localeRepository;
        private readonly RestaurantRepository _restaurantRepository;
        private readonly RestaurantRoleRepository _restaurantRolesRepository;
        private readonly DbFileRepository _fileRepository;
        private readonly MenuCategoryRepository _menuCategoryRepository;
        private readonly DishRepository _dishRepository;
        private readonly DishCategoryRespository _dishCategoryRepository;
        private readonly DishSeoRepository _dishSeoRepository;
        private readonly RestaurantSeoRepository _restaurantSeoRepository;
        private readonly RestaurantInfoRepository _restaurantInfoRepository;
        private readonly RatingRepository _ratingRepository;
        private readonly AnonymousRatingRepository _anonymousRatingRepository;
        private readonly AnonymousRatingResultsRepository _anonymousRatingResultsgRepository;
        private readonly CityRepository _cityRepository;
        private readonly DishWithSeoAndRatingsRepository _dishWithSeoAndRatingsRepository;
        private readonly DishVariationRepository _dishVariationRepository;
        private readonly TypoRequestRepository _typoRequestRepository;
        private readonly DishRatingWithUserRepository _dishRatingWithUserRepository;
        private readonly UserRatingRepository _userRatingRepository;
        private readonly SearchDishRepository _searchDishRepository;
        private readonly DishPhotoRepository _dishPhotoRepository;
        private readonly DishPhotoExtendedRepository _dishPhotoExtendedRepository;
        private readonly LogItemRepository _logItemRepository;
        private readonly PageRepository _pageRepository;
        private readonly MenuPhotoRepository _menuPhotoRepository;
        private readonly MenuCategoryTypeRepository _menuCategoryTypeRepository;
        private readonly CompleteRestaurantRepository _completeRestaurantRepository;
        private readonly AutocompleteDishRepository _autocompleteDishRepository;
        private readonly CompleteDishRepository _completeDishRepository;
        private readonly CustomerRequestRepository _customerRequestRepository;
        private readonly MenuCategoryRelationRepository _menuCategoryRelations;
        private readonly DefaultEfRepository<Promo, IPromo> _promoRepository;


        public IRepository<IUser> Users => _userRepository;
        public IRepository<ILocale> Locales => _localeRepository;
        public IRepository<IRestaurant> Restaurants => _restaurantRepository;
        public IRepository<IRestaurantRole> RestaurantRoles => _restaurantRolesRepository;
        public IRepository<IDbFile> Files => _fileRepository;
        public IRepository<IMenuCategory> MenuCategories => _menuCategoryRepository;
        public IRepository<IDish> Dishes => _dishRepository;
        public IRepository<IDishCategory> DishCategories => _dishCategoryRepository;
        public IRepository<IDishSeo> DishSeo => _dishSeoRepository;
        public IRepository<IRestaurantSeo> RestaurantSeo => _restaurantSeoRepository;
        public IRepository<IRestaurantInfo> RestaurantInfo => _restaurantInfoRepository;
        public IRepository<IRating> Ratings => _ratingRepository;
        public IRepository<IAnonymousRating> AnonymousRatings => _anonymousRatingRepository;
        public IRepository<ICity> Cities => _cityRepository;
        public IRepository<ITypoRequest> TypoRequests => _typoRequestRepository;
        public IRepository<ISearchDish> SearchDishes => _searchDishRepository;
        public IRepository<IDishPhotoExtended> DishPhotoExtendeds => _dishPhotoExtendedRepository;
        public IRepository<IMenuCategoryRelation> MenuCategoryRelations => _menuCategoryRelations;
        public IRepository<IDishPhoto> DishPhotos => _dishPhotoRepository;
        public IRepository<ILogItem> LogItems => _logItemRepository;
        public IRepository<IPage> Pages => _pageRepository;
        public IRepository<IMenuPhoto> MenuPhotos => _menuPhotoRepository;
        public IRepository<ICustomerRequest> CustomerRequests => _customerRequestRepository;
        public IRepository<IMenuCategoryType> MenuCategoryTypes => _menuCategoryTypeRepository;
        public IRepository<IPromo> Promos => _promoRepository;
        public IProcedureRepository<IAnonymousRatingResults> AnonymousRatingResults => _anonymousRatingResultsgRepository;
        public IProcedureRepository<IDishWithSeoAndRatings> DishesWithSeoAndRatings => _dishWithSeoAndRatingsRepository;
        public IRepository<IDishVariation> DishVariations => _dishVariationRepository;
        public IProcedureRepository<IDishRatingWithUser> DishRatingWithUsers => _dishRatingWithUserRepository;
        public IProcedureRepository<IUserRating> UserRatings => _userRatingRepository;
        public ISqlRepository<ICompleteRestaurant> CompleteRestaurants => _completeRestaurantRepository;
        public ISqlRepository<IAutocompleteDish> AutocompleteDishes => _autocompleteDishRepository;
        public ISqlRepository<ICompleteDish> CompleteDishes => _completeDishRepository;
    }

    public static class ApplicationDbContextExtensions
    {
        public static IServiceCollection AddMenusDomain(this IServiceCollection serviceCollection, IConfiguration config)
        {
            serviceCollection.AddScoped<IDbContext, ApplicationDbContext>();
            serviceCollection.AddSingleton<CoreSettings>();
            serviceCollection.AddScoped<Core>();
            serviceCollection.AddScoped<LocaleService>();
            serviceCollection.AddScoped<UserService>();
            serviceCollection.AddScoped<AbTestService>();
            serviceCollection.AddScoped<DishTagsService>();
            serviceCollection.AddScoped<ITaskRunner, TaskRunnerService>();
            return serviceCollection;
        }
    }
}
