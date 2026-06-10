using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using RipplePreview.Configuration;
using RipplePreview.Services;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace RipplePreview;

/// <summary>
/// Registers Ripple Preview services, options and the OpenAPI document.
/// </summary>
public class RipplePreviewComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<RipplePreviewOptions>()
            .BindConfiguration(RippleConstants.Configuration.AppSettingsRoot)
            .ValidateDataAnnotations();

        builder.Services.AddScoped<IRippleBlockDataConverter, RippleBlockDataConverter>();
        builder.Services.AddScoped<IRippleBlockModelFactory, RippleBlockModelFactory>();
        builder.Services.AddSingleton<IRippleViewResolver, RippleViewResolver>();
        builder.Services.AddScoped<IRippleViewRenderer, RippleViewRenderer>();
        builder.Services.AddScoped<IRippleViewComponentRenderer, RippleViewComponentRenderer>();
        builder.Services.AddScoped<IRipplePreviewService, RipplePreviewService>();
        builder.Services.AddSingleton<IRippleDocumentBuilder, RippleDocumentBuilder>();
        builder.Services.AddScoped<RippleCultureService>();

        builder.Services.ConfigureOptions<RippleSwaggerGenOptions>();
        builder.Services.ConfigureOptions<RippleViewEngineOptionsSetup>();
    }
}

/// <summary>
/// Registers a dedicated OpenAPI document for the Ripple Preview API.
/// </summary>
public class RippleSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc(
            RippleConstants.Configuration.ApiName,
            new OpenApiInfo { Title = "Ripple Preview Api", Version = "1.0" });
    }
}

/// <summary>
/// Adds the block component view locations to MVC view discovery so the FindView
/// fallback (and precompiled/RCL views) can resolve block partials by alias.
/// </summary>
public class RippleViewEngineOptionsSetup : IConfigureOptions<RazorViewEngineOptions>
{
    private readonly IOptions<RipplePreviewOptions> _options;

    public RippleViewEngineOptionsSetup(IOptions<RipplePreviewOptions> options)
    {
        _options = options;
    }

    public void Configure(RazorViewEngineOptions options)
    {
        options.ViewLocationExpanders.Add(new RippleViewLocationExpander(_options.Value));
    }

    private sealed class RippleViewLocationExpander : IViewLocationExpander
    {
        private readonly string[] _locations;

        public RippleViewLocationExpander(RipplePreviewOptions options)
        {
            _locations =
            [
                .. options.BlockGrid.ViewLocations,
                .. options.BlockList.ViewLocations,
                .. options.RichText.ViewLocations,
                .. options.SingleBlock.ViewLocations,
                RippleConstants.ViewLocations.BlockGrid,
                RippleConstants.ViewLocations.BlockList,
                RippleConstants.ViewLocations.RichText,
            ];
        }

        public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
            => viewLocations.Concat(_locations);

        public void PopulateValues(ViewLocationExpanderContext context)
        {
        }
    }
}
