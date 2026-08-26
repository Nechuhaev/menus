namespace Menus.Abstract
{
    public interface ITaskRunner
    {
        void RunTaskByUrl(string url);
        void RunUpdateRestaurantIframeCache(string restaurantId);
    }
}
