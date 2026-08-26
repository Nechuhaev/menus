using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Menus.Files.Routing
{
    public interface IRouteHandler
    {
        Task InvokeAsync(HttpContext context);
    }
}
