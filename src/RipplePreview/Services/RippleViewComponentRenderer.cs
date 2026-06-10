using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

namespace RipplePreview.Services;

/// <summary>
/// Renders a block through a ViewComponent named after the element type alias, for sites that
/// implement block views as ViewComponents instead of partials. Pattern adapted from
/// Umbraco.Community.BlockPreview (MIT, © Rick Butterfield and contributors).
/// </summary>
public interface IRippleViewComponentRenderer
{
    /// <summary>Returns the rendered markup, or null when no matching ViewComponent exists.</summary>
    Task<string?> TryRenderAsync(string contentAlias, object? model, ControllerContext controllerContext, ViewDataDictionary viewData);
}

public class RippleViewComponentRenderer : IRippleViewComponentRenderer
{
    private readonly IViewComponentSelector _viewComponentSelector;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RippleViewComponentRenderer> _logger;

    public RippleViewComponentRenderer(
        IViewComponentSelector viewComponentSelector,
        ITempDataProvider tempDataProvider,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RippleViewComponentRenderer> logger)
    {
        _viewComponentSelector = viewComponentSelector;
        _tempDataProvider = tempDataProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<string?> TryRenderAsync(string contentAlias, object? model, ControllerContext controllerContext, ViewDataDictionary viewData)
    {
        // ViewComponents may be registered PascalCase (class name) or camelCase (alias).
        ViewComponentDescriptor? descriptor = _viewComponentSelector.SelectComponent(ToPascalCase(contentAlias))
            ?? _viewComponentSelector.SelectComponent(contentAlias);

        if (descriptor == null)
            return null;

        try
        {
            return await RenderCoreAsync(descriptor, model, controllerContext, viewData, controllerContext.HttpContext.RequestServices);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "RipplePreview: view buffer scope disposed rendering ViewComponent '{Alias}'; retrying with child scope.", contentAlias);

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            var httpContext = controllerContext.HttpContext;
            IServiceProvider originalServices = httpContext.RequestServices;
            try
            {
                httpContext.RequestServices = scope.ServiceProvider;
                return await RenderCoreAsync(descriptor, model, controllerContext, viewData, scope.ServiceProvider);
            }
            finally
            {
                httpContext.RequestServices = originalServices;
            }
        }
    }

    private async Task<string> RenderCoreAsync(
        ViewComponentDescriptor descriptor,
        object? model,
        ControllerContext controllerContext,
        ViewDataDictionary viewData,
        IServiceProvider services)
    {
        IViewComponentHelper helper = services.GetRequiredService<IViewComponentHelper>();

        await using var sw = new StringWriter();
        var viewContext = new ViewContext(
            controllerContext,
            new FakeView(),
            viewData,
            new TempDataDictionary(controllerContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        if (helper is IViewContextAware contextAware)
            contextAware.Contextualize(viewContext);

        IHtmlContent result = await helper.InvokeAsync(descriptor.TypeInfo.AsType(), model);
        result.WriteTo(sw, HtmlEncoder.Default);
        return sw.ToString();
    }

    private static string ToPascalCase(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class FakeView : IView
    {
        public string Path => string.Empty;
        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }
}
