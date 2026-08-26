using Menus.Abstract;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Data.Repositories
{
    public abstract class EfRepositoryWithRestaurantCacheUpdate<TEntity, TInterface> : EfRepository<TEntity, TInterface> where TEntity : class, TInterface, new()
    {
        protected EfRepositoryWithRestaurantCacheUpdate(DbContext context, ITaskRunner taskRunner) : base(context)
        {
            _taskRunner = taskRunner;
        }

        private ITaskRunner _taskRunner;

        public async override Task Add(TInterface entity)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entity);
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Add(entity);
        }

        public async override Task Add(IEnumerable<TInterface> entities)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entities.First());
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Add(entities);
        }

        public async override Task Remove(TInterface entity)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entity);
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Remove(entity);
        }

        public async override Task Remove(IEnumerable<TInterface> entities)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entities.First());
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Remove(entities);
        }

        public async override Task Update(TInterface entity)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entity);
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Update(entity);
        }

        public async override Task Update(IEnumerable<TInterface> entities)
        {
            var restaurantId = await GetRestaurantIdFromEntity(entities.First());
            if (!string.IsNullOrEmpty(restaurantId))
            {
                _taskRunner.RunUpdateRestaurantIframeCache(restaurantId);
            }
            await base.Update(entities);
        }

        private async Task<string> GetRestaurantIdFromEntity(TInterface entity)
        {
            if (entity.GetType() == typeof(Restaurant))
            {
                return (entity as Restaurant).Id;
            }
            if (entity.GetType() == typeof(Dish))
            {
                return (entity as Dish).RestaurantId;
            }
            if (entity.GetType() == typeof(MenuCategory))
            {
                return (entity as MenuCategory).RestaurantId;
            }
            if (entity.GetType() == typeof(RestaurantSeo))
            {
                return (entity as RestaurantSeo).ItemId;
            }
            if (entity.GetType() == typeof(RestaurantInfo))
            {
                return (entity as RestaurantInfo).RestaurantId;
            }
            else return null;
        }
    }
}
