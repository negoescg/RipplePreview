using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RipplePreview.Services;

/// <summary>
/// Renders a resolved view to a string using the current HttpContext.
/// Retries with a child service scope if the request's view buffer scope was disposed
/// under concurrency. Adapted from Umbraco.Community.BlockPreview (MIT, © Rick Butterfield
/// and contributors).
/// </summary>
public interface IRippleViewRenderer
{
    Task<string> RenderViewAsync(ControllerContext controllerContext, ViewEngineResult viewResult, ViewDataDictionary viewData);
}

public class RippleViewRenderer : IRippleViewRenderer
{
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RippleViewRenderer> _logger;

    public RippleViewRenderer(
        ITempDataProvider tempDataProvider,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RippleViewRenderer> logger)
    {
        _tempDataProvider = tempDataProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<string> RenderViewAsync(ControllerContext controllerContext, ViewEngineResult viewResult, ViewDataDictionary viewData)
    {
        if (viewResult.View == null)
            throw new InvalidOperationException($"View '{viewResult.ViewName}' could not be found. Searched: {string.Join(", ", viewResult.SearchedLocations ?? [])}");

        try
        {
            return await RenderCoreAsync(controllerContext.HttpContext, viewResult, viewData);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "RipplePreview: view buffer scope disposed during render of '{ViewName}'; retrying with child scope.", viewResult.ViewName);

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            var httpContext = controllerContext.HttpContext;
            IServiceProvider originalServices = httpContext.RequestServices;
            try
            {
                httpContext.RequestServices = scope.ServiceProvider;
                return await RenderCoreAsync(httpContext, viewResult, viewData);
            }
            finally
            {
                httpContext.RequestServices = originalServices;
            }
        }
    }

    private async Task<string> RenderCoreAsync(Microsoft.AspNetCore.Http.HttpContext httpContext, ViewEngineResult viewResult, ViewDataDictionary viewData)
    {
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        await using var sw = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View!,
            viewData,
            new TempDataDictionary(httpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        await viewResult.View!.RenderAsync(viewContext);
        return sw.ToString();
    }
}
