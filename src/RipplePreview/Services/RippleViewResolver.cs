using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Options;
using RipplePreview.Configuration;

namespace RipplePreview.Services;

/// <summary>
/// Resolves the partial view for a block alias using configured view location conventions,
/// caching resolved paths. Falls back to standard MVC view discovery.
/// </summary>
public interface IRippleViewResolver
{
    ViewEngineResult? ResolveBlockView(string contentAlias, RippleEditorKind kind, ControllerContext controllerContext);
    ViewEngineResult? ResolveViewByPath(string viewPath);
    void ClearCache();
}

public class RippleViewResolver : IRippleViewResolver
{
    private const string NotFoundSentinel = "\0";
    private static readonly ConcurrentDictionary<string, string> PathCache = new();

    private readonly IRazorViewEngine _razorViewEngine;
    private readonly IOptionsMonitor<RipplePreviewOptions> _optionsMonitor;

    public RippleViewResolver(
        IRazorViewEngine razorViewEngine,
        IOptionsMonitor<RipplePreviewOptions> optionsMonitor)
    {
        _razorViewEngine = razorViewEngine;
        _optionsMonitor = optionsMonitor;
        _optionsMonitor.OnChange(_ => ClearCache());
    }

    public ViewEngineResult? ResolveBlockView(string contentAlias, RippleEditorKind kind, ControllerContext controllerContext)
    {
        if (string.IsNullOrEmpty(contentAlias))
            return null;

        var cacheKey = $"{kind}:{contentAlias}";
        var viewPath = PathCache.GetOrAdd(cacheKey, _ => FindViewPath(contentAlias, kind));

        if (viewPath != NotFoundSentinel)
        {
            // A fresh ViewEngineResult per call: RazorView/IRazorPage hold mutable per-render state.
            ViewEngineResult result = _razorViewEngine.GetView("", viewPath, false);
            if (result.Success)
                return result;
        }

        // Fall back to normal MVC view discovery (supports precompiled views and RCL view providers).
        ViewEngineResult fallback = _razorViewEngine.FindView(controllerContext, contentAlias, false);
        if (!fallback.Success)
            fallback = _razorViewEngine.FindView(controllerContext, ToPascalCase(contentAlias), false);

        return fallback;
    }

    public ViewEngineResult? ResolveViewByPath(string viewPath)
    {
        if (string.IsNullOrWhiteSpace(viewPath))
            return null;

        ViewEngineResult result = _razorViewEngine.GetView("", viewPath, false);
        return result.Success ? result : null;
    }

    public void ClearCache() => PathCache.Clear();

    private string FindViewPath(string contentAlias, RippleEditorKind kind)
    {
        RipplePreviewOptions options = _optionsMonitor.CurrentValue;
        RippleEditorOptions editorOptions = options.ForKind(kind);

        var locations = new List<string>(editorOptions.ViewLocations)
        {
            kind switch
            {
                RippleEditorKind.BlockGrid => RippleConstants.ViewLocations.BlockGrid,
                RippleEditorKind.RichText => RippleConstants.ViewLocations.RichText,
                RippleEditorKind.SingleBlock => RippleConstants.ViewLocations.SingleBlock,
                _ => RippleConstants.ViewLocations.BlockList,
            }
        };

        foreach (string location in locations)
        {
            string template = location.TrimStart('~');

            foreach (string alias in new[] { contentAlias, ToPascalCase(contentAlias) })
            {
                string candidate = string.Format(template, alias);

                // GetView is the source of truth: it resolves physical, precompiled and
                // RCL-provided views alike.
                if (_razorViewEngine.GetView("", candidate, false).Success)
                    return candidate;
            }
        }

        return NotFoundSentinel;
    }

    private static string ToPascalCase(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
