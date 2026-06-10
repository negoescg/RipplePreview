using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RipplePreview.Configuration;
using RipplePreview.Models;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace RipplePreview.Controllers;

/// <summary>
/// Serves Ripple Preview settings to the backoffice client so it can decide which
/// block editor custom views to register.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "RipplePreview")]
public class RippleSettingsController : RipplePreviewControllerBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private readonly IOptionsMonitor<RipplePreviewOptions> _options;
    private readonly IContentTypeService _contentTypeService;
    private readonly IAppPolicyCache _runtimeCache;

    public RippleSettingsController(
        IOptionsMonitor<RipplePreviewOptions> options,
        IContentTypeService contentTypeService,
        AppCaches appCaches)
    {
        _options = options;
        _contentTypeService = contentTypeService;
        _runtimeCache = appCaches.RuntimeCache;
    }

    [HttpGet("settings")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(RippleSettingsResponse), StatusCodes.Status200OK)]
    public IActionResult GetSettings()
    {
        RipplePreviewOptions options = _options.CurrentValue;

        return Ok(new RippleSettingsResponse
        {
            DesignWidth = options.DesignWidth,
            BlockGrid = MapEditor(options.BlockGrid),
            BlockList = MapEditor(options.BlockList),
            RichText = MapEditor(options.RichText),
            SingleBlock = MapEditor(options.SingleBlock),
        });
    }

    private RippleEditorSettingsResponse MapEditor(RippleEditorOptions editor)
    {
        List<string> contentTypes = editor.ContentTypes.Count > 0
            ? editor.ContentTypes
            : editor.IgnoredContentTypes.Count > 0
                ? GetAllElementAliases().Except(editor.IgnoredContentTypes, StringComparer.OrdinalIgnoreCase).ToList()
                : [];

        return new RippleEditorSettingsResponse
        {
            Enabled = editor.Enabled,
            ContentTypes = contentTypes,
            FullAreaPreviewContentTypes = editor.FullAreaPreviewContentTypes,
            StackedAreaPreviewContentTypes = editor.StackedAreaPreviewContentTypes,
        };
    }

    private List<string> GetAllElementAliases()
        => _runtimeCache.GetCacheItem("RipplePreview.ElementAliases",
               () => _contentTypeService.GetAll().Where(ct => ct.IsElement).Select(ct => ct.Alias).ToList(),
               CacheDuration) ?? [];
}
