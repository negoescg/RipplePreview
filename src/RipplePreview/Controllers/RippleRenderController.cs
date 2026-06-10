using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RipplePreview.Configuration;
using RipplePreview.Models;
using RipplePreview.Services;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.HybridCache;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace RipplePreview.Controllers;

/// <summary>
/// Renders single-block previews as complete HTML documents for the preview iframe.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "RipplePreview")]
public class RippleRenderController : RipplePreviewControllerBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private readonly IRipplePreviewService _previewService;
    private readonly IRippleDocumentBuilder _documentBuilder;
    private readonly RippleCultureService _cultureService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IPublishedRouter _publishedRouter;
    private readonly ILanguageService _languageService;
    private readonly IDocumentCacheService _documentCacheService;
    private readonly IPublishedContentTypeCache _contentTypeCache;
    private readonly IDocumentNavigationQueryService _documentNavigationQueryService;
    private readonly IScopeProvider _scopeProvider;
    private readonly IAppPolicyCache _runtimeCache;
    private readonly IOptionsMonitor<RipplePreviewOptions> _options;
    private readonly ILogger<RippleRenderController> _logger;

    public RippleRenderController(
        IRipplePreviewService previewService,
        IRippleDocumentBuilder documentBuilder,
        RippleCultureService cultureService,
        IUmbracoContextAccessor umbracoContextAccessor,
        IPublishedRouter publishedRouter,
        ILanguageService languageService,
        IDocumentCacheService documentCacheService,
        IPublishedContentTypeCache contentTypeCache,
        IDocumentNavigationQueryService documentNavigationQueryService,
        IScopeProvider scopeProvider,
        AppCaches appCaches,
        IOptionsMonitor<RipplePreviewOptions> options,
        ILogger<RippleRenderController> logger)
    {
        _previewService = previewService;
        _documentBuilder = documentBuilder;
        _cultureService = cultureService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _publishedRouter = publishedRouter;
        _languageService = languageService;
        _documentCacheService = documentCacheService;
        _contentTypeCache = contentTypeCache;
        _documentNavigationQueryService = documentNavigationQueryService;
        _scopeProvider = scopeProvider;
        _runtimeCache = appCaches.RuntimeCache;
        _options = options;
        _logger = logger;
    }

    [HttpPost("render/grid")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(RippleRenderResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> RenderGrid([FromBody] RippleRenderRequest request)
        => RenderAsync(request, RippleEditorKind.BlockGrid);

    [HttpPost("render/list")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(RippleRenderResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> RenderList([FromBody] RippleRenderRequest request)
        => RenderAsync(request, RippleEditorKind.BlockList);

    [HttpPost("render/rte")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(RippleRenderResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> RenderRichText([FromBody] RippleRenderRequest request)
        => RenderAsync(request, RippleEditorKind.RichText);

    [HttpPost("render/single")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(RippleRenderResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> RenderSingle([FromBody] RippleRenderRequest request)
        => RenderAsync(request, RippleEditorKind.SingleBlock);

    private async Task<IActionResult> RenderAsync(RippleRenderRequest request, RippleEditorKind kind)
    {
        RippleEditorOptions editorOptions = _options.CurrentValue.ForKind(kind);

        try
        {
            HttpContext.Items[RippleConstants.HttpContextItems.IsRipplePreview] = true;

            IPublishedContent? owner = GetOwnerContent(request.DocumentKey, request.DocumentTypeKey);
            if (owner == null)
            {
                return Ok(new RippleRenderResponse
                {
                    Html = BuildMessageDocument("No published content is available yet to provide rendering context. Save or publish a page first."),
                    WidthFraction = 1d,
                });
            }

            string? culture = await ResolveCultureAsync(request.Culture, owner);
            _cultureService.SetCulture(culture);

            await SetupPublishedRequestAsync(owner);

            RippleRenderResult result = kind switch
            {
                RippleEditorKind.BlockGrid => await _previewService.RenderGridBlockAsync(request, owner, ControllerContext),
                RippleEditorKind.RichText => await _previewService.RenderRichTextBlockAsync(request, owner, ControllerContext),
                RippleEditorKind.SingleBlock => await _previewService.RenderSingleBlockAsync(request, owner, ControllerContext),
                _ => await _previewService.RenderListBlockAsync(request, owner, ControllerContext),
            };

            string document = _documentBuilder.BuildDocument(result.Markup, result.WidthFraction, editorOptions, culture, request.Backdrop, result.ChildRefs);

            return Ok(new RippleRenderResponse { Html = document, WidthFraction = result.WidthFraction });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RipplePreview: failed to render {Editor} block {ContentKey}.", kind, request.ContentKey);
            return Ok(new RippleRenderResponse
            {
                Html = BuildMessageDocument(ex.Message),
                WidthFraction = 1d,
            });
        }
    }

    private string BuildMessageDocument(string message)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(message);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0}" +
               ".ripple-msg{font:13px/1.5 'Segoe UI',sans-serif;color:#515054;background:#fff3d1;border:1px solid #f0c659;border-radius:4px;padding:10px 14px;margin:6px;}" +
               "</style></head><body><div class=\"ripple-msg\">" + encoded + "</div>" +
               "<script>parent.postMessage({source:'ripple-preview',type:'size',height:document.body.scrollHeight+12},'*');</script>" +
               "</body></html>";
    }

    private async Task<string?> ResolveCultureAsync(string? requestedCulture, IPublishedContent? content)
    {
        string? culture = string.IsNullOrWhiteSpace(requestedCulture) || requestedCulture == "undefined"
            ? null
            : requestedCulture;

        culture ??= content?.GetCultureFromDomains();

        if (string.IsNullOrEmpty(culture))
        {
            var languages = (await _languageService.GetAllAsync()).ToList();
            culture = languages.Count == 1
                ? languages[0].IsoCode
                : await _languageService.GetDefaultIsoCodeAsync();
        }

        return culture;
    }

    private async Task SetupPublishedRequestAsync(IPublishedContent? content)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? context))
            return;

        var requestUrl = new Uri(Request.GetDisplayUrl());
        IPublishedRequestBuilder requestBuilder = await _publishedRouter.CreateRequestAsync(requestUrl);

        if (content != null)
            requestBuilder.SetPublishedContent(content);

        context.PublishedRequest = requestBuilder.Build();
    }

    /// <summary>
    /// Resolves the content used as rendering context: the actual document (draft-aware) when it
    /// exists, otherwise any published document of the same type, otherwise any root content.
    /// </summary>
    private IPublishedContent? GetOwnerContent(Guid? documentKey, Guid documentTypeKey)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? context))
            return null;

        if (documentKey.HasValue && documentKey.Value != Guid.Empty)
        {
            IPublishedContent? content = context.Content?.GetById(preview: true, documentKey.Value);
            if (content != null)
                return content;
        }

        if (documentTypeKey != Guid.Empty)
        {
            try
            {
                IPublishedContentType? publishedContentType = _contentTypeCache.Get(PublishedItemType.Content, documentTypeKey);
                if (publishedContentType != null)
                {
                    var cacheKey = $"RipplePreview.FirstOfType.{documentTypeKey}";
                    using IScope scope = _scopeProvider.CreateScope();
                    IPublishedContent? cacheItem = _runtimeCache.GetCacheItem(cacheKey,
                        () => _documentCacheService.GetByContentType(publishedContentType).FirstOrDefault(),
                        CacheDuration);
                    scope.Complete();

                    if (cacheItem != null)
                        return cacheItem;
                }
            }
            catch (Exception ex)
            {
                // documentTypeKey may be an element type (nested block workspaces) — fall through.
                _logger.LogDebug(ex, "RipplePreview: content type {Key} is not a document type; using root fallback.", documentTypeKey);
            }
        }

        if (_documentNavigationQueryService.TryGetRootKeys(out IEnumerable<Guid> rootKeys))
        {
            foreach (Guid rootKey in rootKeys)
            {
                IPublishedContent? root = context.Content?.GetById(preview: true, rootKey);
                if (root != null)
                    return root;
            }
        }

        return null;
    }
}
