using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Menus.Files.Routing
{
    public class Router
    {
        private readonly RequestDelegate _next;
        private readonly Dictionary<IRoute, Type> _routeSettings;

        public Router(RequestDelegate next, Dictionary<IRoute, Type> routeSettings)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _routeSettings = routeSettings ?? throw new ArgumentNullException(nameof(routeSettings));
        }

        public async Task Invoke(HttpContext context, IServiceProvider serviceProvider)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            Type possibleRouteHandlerType = GetPossibleRouteHandler(context);

            if (possibleRouteHandlerType != null)
            {
                var routeHandler = serviceProvider.GetService(possibleRouteHandlerType) as IRouteHandler;
                if (routeHandler != null)
                {
                    try
                    {
                        await routeHandler.InvokeAsync(context);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"{context.Request.Method} - {context.Request.Path.ToString()}", ex);
                    }
                }
                else await _next(context);
            }
            else
            {
                await _next(context);
            }
        }

        private Type GetPossibleRouteHandler(HttpContext context)
        {
            var url = context.Request.Path.ToString();
            var method = context.Request.Method;
            var possibleRouteHandlerType = _routeSettings
                .Where(x => url.StartsWith(x.Key.Url))
                .Where(x => x.Key.Method == method)
                .Select(x => x.Value)
                .FirstOrDefault();
            return possibleRouteHandlerType;
        }
    }

    public static class RouterMiddlewareExtensions
    {
        public static IApplicationBuilder UseRouter(this IApplicationBuilder builder, Dictionary<IRoute, Type> routeSettings)
        {
            return builder.UseMiddleware<Router>(routeSettings);
        }
    }
}
