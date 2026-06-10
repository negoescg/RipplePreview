using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RipplePreview.Configuration;
using RipplePreview.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using static Umbraco.Cms.Core.Constants;

namespace RipplePreview.Services;

/// <summary>
/// Orchestrates rendering a single block (grid or list) into frontend markup.
/// </summary>
public interface IRipplePreviewService
{
    Task<RippleRenderResult> RenderGridBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext);
    Task<RippleRenderResult> RenderListBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext);
    Task<RippleRenderResult> RenderRichTextBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext);
    Task<RippleRenderResult> RenderSingleBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext);
}

/// <summary>A child block rendered inside a parent preview, in document order.</summary>
public record RippleChildRef(Guid Key, string Alias);

/// <summary>Result of rendering a block: inner markup plus the computed width fraction.</summary>
public record RippleRenderResult(string Markup, double WidthFraction, IReadOnlyList<RippleChildRef>? ChildRefs = null);

public class RipplePreviewService : IRipplePreviewService
{
    private readonly IRippleBlockDataConverter _dataConverter;
    private readonly IRippleBlockModelFactory _modelFactory;
    private readonly IRippleViewResolver _viewResolver;
    private readonly IRippleViewRenderer _viewRenderer;
    private readonly IRippleViewComponentRenderer _viewComponentRenderer;
    private readonly IContentTypeService _contentTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly IOptionsMonitor<RipplePreviewOptions> _options;
    private readonly ILogger<RipplePreviewService> _logger;

    public RipplePreviewService(
        IRippleBlockDataConverter dataConverter,
        IRippleBlockModelFactory modelFactory,
        IRippleViewResolver viewResolver,
        IRippleViewRenderer viewRenderer,
        IRippleViewComponentRenderer viewComponentRenderer,
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IOptionsMonitor<RipplePreviewOptions> options,
        ILogger<RipplePreviewService> logger)
    {
        _dataConverter = dataConverter;
        _modelFactory = modelFactory;
        _viewResolver = viewResolver;
        _viewRenderer = viewRenderer;
        _viewComponentRenderer = viewComponentRenderer;
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _options = options;
        _logger = logger;
    }

    public async Task<RippleRenderResult> RenderGridBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext)
    {
        string blockJson = request.BlockValue?.ToJsonString()
            ?? throw new InvalidOperationException("No block value supplied.");

        BlockEditorData<BlockGridValue, BlockGridLayoutItem>? editorData = _dataConverter.DeserializeBlockGrid(blockJson)
            ?? throw new InvalidOperationException("The block value could not be deserialized.");

        BlockGridValue value = editorData.BlockValue;

        BlockItemData contentData = value.ContentData.FirstOrDefault(x => x.Key == request.ContentKey)
            ?? throw new InvalidOperationException($"No content data found for block '{request.ContentKey}'.");

        BlockItemData? settingsData = request.SettingsKey is { } sk && sk != Guid.Empty
            ? value.SettingsData.FirstOrDefault(x => x.Key == sk)
            : null;

        IPublishedElement contentElement = _dataConverter.ConvertToElement(contentData, owner);
        IPublishedElement? settingsElement = settingsData != null ? _dataConverter.ConvertToElement(settingsData, owner) : null;

        BlockGridItem blockItem = _modelFactory.CreateGridItem(contentElement, settingsElement, contentData.Key, settingsData?.Key);

        BlockGridConfiguration? config = await ResolveGridConfigurationAsync(request.DocumentTypeKey, request.PropertyAlias, contentData.ContentTypeKey);
        int rootGridColumns = config?.GridColumns ?? 12;

        // Locate the layout entry for this block anywhere in the layout tree and compute its
        // cumulative width fraction (its true share of the full-page viewport width).
        IEnumerable<BlockGridLayoutItem> rootLayouts = value.GetLayouts() ?? [];
        LayoutMatch? match = FindLayout(rootLayouts, request.ContentKey, 1d, rootGridColumns, config, value, []);

        BlockGridLayoutItem? layout = match?.Layout;
        double widthFraction = match?.WidthFraction ?? 1d;

        blockItem.RowSpan = layout?.RowSpan ?? 1;
        blockItem.ColumnSpan = layout?.ColumnSpan ?? rootGridColumns;
        blockItem.GridColumns = rootGridColumns;

        BlockGridConfiguration.BlockGridBlockConfiguration? blockConfig =
            config?.Blocks.FirstOrDefault(b => b.ContentElementTypeKey == contentData.ContentTypeKey);
        blockItem.AreaGridColumns = blockConfig?.AreaGridColumns ?? rootGridColumns;

        List<RippleChildRef> childRefs = [];
        if (request.IncludeAreas && layout != null && config != null)
        {
            PopulateAreas(blockItem, layout, config, value, owner);
            CollectChildRefs(layout, value, childRefs);
        }
        else
        {
            blockItem.Areas = [];
        }

        widthFraction = Math.Clamp(widthFraction, 0.01, 1d);

        ViewDataDictionary viewData = CreateViewData(blockItem, owner);
        viewData["blockGridPreview"] = true;
        viewData["rippleWidthFraction"] = widthFraction;
        viewData["rippleAncestors"] = BuildAncestorElements(match?.AncestorChain, value, owner);

        string markup = await RenderBlockAsync(blockItem, contentElement.ContentType.Alias, RippleEditorKind.BlockGrid, controllerContext, viewData, skipWrapper: request.Backdrop);
        return new RippleRenderResult(markup, widthFraction, childRefs);
    }

    public async Task<RippleRenderResult> RenderListBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext)
    {
        string blockJson = request.BlockValue?.ToJsonString()
            ?? throw new InvalidOperationException("No block value supplied.");

        BlockEditorData<BlockListValue, BlockListLayoutItem>? editorData = _dataConverter.DeserializeBlockList(blockJson)
            ?? throw new InvalidOperationException("The block value could not be deserialized.");

        BlockListValue value = editorData.BlockValue;

        BlockItemData contentData = value.ContentData.FirstOrDefault(x => x.Key == request.ContentKey)
            ?? throw new InvalidOperationException($"No content data found for block '{request.ContentKey}'.");

        BlockItemData? settingsData = request.SettingsKey is { } sk && sk != Guid.Empty
            ? value.SettingsData.FirstOrDefault(x => x.Key == sk)
            : null;

        IPublishedElement contentElement = _dataConverter.ConvertToElement(contentData, owner);
        IPublishedElement? settingsElement = settingsData != null ? _dataConverter.ConvertToElement(settingsData, owner) : null;

        BlockListItem blockItem = _modelFactory.CreateListItem(contentElement, settingsElement, contentData.Key, settingsData?.Key);

        ViewDataDictionary viewData = CreateViewData(blockItem, owner);
        viewData["blockListPreview"] = true;
        viewData["rippleWidthFraction"] = 1d;

        string markup = await RenderBlockAsync(blockItem, contentElement.ContentType.Alias, RippleEditorKind.BlockList, controllerContext, viewData);
        return new RippleRenderResult(markup, 1d);
    }

    public async Task<RippleRenderResult> RenderRichTextBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext)
    {
        string blockJson = request.BlockValue?.ToJsonString()
            ?? throw new InvalidOperationException("No block value supplied.");

        BlockEditorData<RichTextBlockValue, RichTextBlockLayoutItem>? editorData = _dataConverter.DeserializeRichText(blockJson)
            ?? throw new InvalidOperationException("The block value could not be deserialized.");

        RichTextBlockValue value = editorData.BlockValue;

        BlockItemData contentData = (request.ContentKey != Guid.Empty
                ? value.ContentData.FirstOrDefault(x => x.Key == request.ContentKey)
                : value.ContentData.FirstOrDefault())
            ?? throw new InvalidOperationException($"No content data found for block '{request.ContentKey}'.");

        BlockItemData? settingsData = request.SettingsKey is { } sk && sk != Guid.Empty
            ? value.SettingsData.FirstOrDefault(x => x.Key == sk)
            : null;

        IPublishedElement contentElement = _dataConverter.ConvertToElement(contentData, owner);
        IPublishedElement? settingsElement = settingsData != null ? _dataConverter.ConvertToElement(settingsData, owner) : null;

        RichTextBlockItem blockItem = _modelFactory.CreateRichTextItem(contentElement, settingsElement, contentData.Key, settingsData?.Key);

        ViewDataDictionary viewData = CreateViewData(blockItem, owner);
        viewData["richTextPreview"] = true;
        viewData["rippleWidthFraction"] = 1d;

        string markup = await RenderBlockAsync(blockItem, contentElement.ContentType.Alias, RippleEditorKind.RichText, controllerContext, viewData);
        return new RippleRenderResult(markup, 1d);
    }

    public async Task<RippleRenderResult> RenderSingleBlockAsync(RippleRenderRequest request, IPublishedContent owner, ControllerContext controllerContext)
    {
        string blockJson = request.BlockValue?.ToJsonString()
            ?? throw new InvalidOperationException("No block value supplied.");

        BlockEditorData<SingleBlockValue, SingleBlockLayoutItem>? editorData = _dataConverter.DeserializeSingleBlock(blockJson)
            ?? throw new InvalidOperationException("The block value could not be deserialized.");

        SingleBlockValue value = editorData.BlockValue;

        BlockItemData contentData = (request.ContentKey != Guid.Empty
                ? value.ContentData.FirstOrDefault(x => x.Key == request.ContentKey)
                : value.ContentData.FirstOrDefault())
            ?? throw new InvalidOperationException($"No content data found for block '{request.ContentKey}'.");

        BlockItemData? settingsData = request.SettingsKey is { } sk && sk != Guid.Empty
            ? value.SettingsData.FirstOrDefault(x => x.Key == sk)
            : null;

        IPublishedElement contentElement = _dataConverter.ConvertToElement(contentData, owner);
        IPublishedElement? settingsElement = settingsData != null ? _dataConverter.ConvertToElement(settingsData, owner) : null;

        BlockListItem blockItem = _modelFactory.CreateListItem(contentElement, settingsElement, contentData.Key, settingsData?.Key);

        ViewDataDictionary viewData = CreateViewData(blockItem, owner);
        viewData["singleBlockPreview"] = true;
        viewData["rippleWidthFraction"] = 1d;

        string markup = await RenderBlockAsync(blockItem, contentElement.ContentType.Alias, RippleEditorKind.SingleBlock, controllerContext, viewData);
        return new RippleRenderResult(markup, 1d);
    }

    private static ViewDataDictionary CreateViewData(object model, IPublishedContent owner)
    {
        return new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
            ["ripplePreview"] = true,
            ["blockPreview"] = true, // compatibility with views written for Umbraco.Community.BlockPreview
            ["rippleOwner"] = owner, // the document providing rendering context (page background etc.)
        };
    }

    /// <summary>
    /// Converts the ancestor layout chain (outermost first) into published elements so wrapper
    /// views can replicate ancestor chrome (e.g. a colored background area) around nested blocks.
    /// </summary>
    private List<IPublishedElement> BuildAncestorElements(IReadOnlyList<BlockGridLayoutItem>? ancestors, BlockGridValue value, IPublishedContent owner)
    {
        List<IPublishedElement> elements = [];
        if (ancestors == null)
            return elements;

        foreach (BlockGridLayoutItem ancestor in ancestors)
        {
            BlockItemData? data = value.ContentData.FirstOrDefault(x => x.Key == ancestor.ContentKey);
            if (data == null)
                continue;

            try
            {
                elements.Add(_dataConverter.ConvertToElement(data, owner));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RipplePreview: could not convert ancestor block '{Key}'.", ancestor.ContentKey);
            }
        }

        return elements;
    }

    private async Task<string> RenderBlockAsync(object blockItem, string contentAlias, RippleEditorKind kind, ControllerContext controllerContext, ViewDataDictionary viewData, bool skipWrapper = false)
    {
        // ViewComponents named after the alias win over partial views (parity with frontend conventions).
        string? markup = await _viewComponentRenderer.TryRenderAsync(contentAlias, blockItem, controllerContext, viewData);

        if (string.IsNullOrEmpty(markup))
        {
            ViewEngineResult? viewResult = _viewResolver.ResolveBlockView(contentAlias, kind, controllerContext);
            if (viewResult == null || !viewResult.Success || viewResult.View == null)
            {
                string searched = string.Join(", ", viewResult?.SearchedLocations ?? []);
                throw new InvalidOperationException($"No view or ViewComponent found for block '{contentAlias}'. Searched: {searched}");
            }

            markup = await _viewRenderer.RenderViewAsync(controllerContext, viewResult, viewData);
        }

        if (_options.CurrentValue.Diagnostics)
        {
            string snippet = System.Text.RegularExpressions.Regex.Replace(markup, @"\s+", " ").Trim();
            _logger.LogInformation("RipplePreview diag markup '{Alias}': {Snippet}",
                contentAlias, snippet.Length > 500 ? snippet[..500] + "…" : snippet);
        }

        RippleEditorOptions editorOptions = _options.CurrentValue.ForKind(kind);
        if (!skipWrapper && !string.IsNullOrWhiteSpace(editorOptions.WrapperView))
        {
            ViewEngineResult? wrapperResult = _viewResolver.ResolveViewByPath(editorOptions.WrapperView!);
            if (wrapperResult?.View != null)
            {
                var wrapperViewData = new ViewDataDictionary(viewData)
                {
                    ["rippleInnerHtml"] = new HtmlString(markup),
                };
                markup = await _viewRenderer.RenderViewAsync(controllerContext, wrapperResult, wrapperViewData);
            }
            else
            {
                _logger.LogWarning("RipplePreview: configured wrapper view '{WrapperView}' was not found; rendering without it.", editorOptions.WrapperView);
            }
        }

        return markup;
    }

    /// <summary>
    /// Resolves the BlockGridConfiguration for the property. If the property cannot be found on the
    /// supplied content type (e.g. the editor sits on an element type inside a nested workspace),
    /// falls back to scanning all Block Grid data types for one containing the block's element type.
    /// </summary>
    private async Task<BlockGridConfiguration?> ResolveGridConfigurationAsync(Guid documentTypeKey, string propertyAlias, Guid blockContentTypeKey)
    {
        IContentType? contentType = documentTypeKey != Guid.Empty ? _contentTypeService.Get(documentTypeKey) : null;

        IPropertyType? property = contentType?.PropertyTypes.FirstOrDefault(x => x.Alias == propertyAlias)
            ?? contentType?.CompositionPropertyTypes.FirstOrDefault(x => x.Alias == propertyAlias);

        if (property != null)
        {
            IDataType? dataType = await _dataTypeService.GetAsync(property.DataTypeKey);
            BlockGridConfiguration? configuration = dataType?.ConfigurationAs<BlockGridConfiguration>();
            if (configuration != null)
                return configuration;
        }

        // Fallback: find a Block Grid data type whose block collection contains this element type.
        try
        {
            IEnumerable<IDataType> gridDataTypes = await _dataTypeService.GetByEditorAliasAsync(PropertyEditors.Aliases.BlockGrid);
            foreach (IDataType dataType in gridDataTypes)
            {
                BlockGridConfiguration? configuration = dataType.ConfigurationAs<BlockGridConfiguration>();
                if (configuration?.Blocks.Any(b => b.ContentElementTypeKey == blockContentTypeKey) == true)
                {
                    _logger.LogDebug("RipplePreview: resolved grid configuration for block type {BlockTypeKey} via data type scan ('{DataType}').",
                        blockContentTypeKey, dataType.Name);
                    return configuration;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RipplePreview: data type scan for grid configuration failed.");
        }

        return null;
    }

    private sealed record LayoutMatch(BlockGridLayoutItem Layout, double WidthFraction, IReadOnlyList<BlockGridLayoutItem> AncestorChain);

    /// <summary>
    /// Depth-first search for the layout entry of <paramref name="targetKey"/>, accumulating the
    /// width fraction through column spans, area spans and area grid columns at each level, and
    /// tracking the ancestor chain (outermost first).
    /// </summary>
    private LayoutMatch? FindLayout(
        IEnumerable<BlockGridLayoutItem> items,
        Guid targetKey,
        double parentFraction,
        int containerColumns,
        BlockGridConfiguration? config,
        BlockGridValue value,
        List<BlockGridLayoutItem> ancestors)
    {
        foreach (BlockGridLayoutItem item in items)
        {
            int span = Math.Clamp(item.ColumnSpan ?? containerColumns, 1, containerColumns);
            double fraction = parentFraction * span / containerColumns;

            if (item.ContentKey == targetKey)
                return new LayoutMatch(item, fraction, ancestors.ToList());

            if (item.Areas.Length == 0)
                continue;

            Guid? itemTypeKey = value.ContentData.FirstOrDefault(x => x.Key == item.ContentKey)?.ContentTypeKey;
            BlockGridConfiguration.BlockGridBlockConfiguration? blockConfig = itemTypeKey.HasValue
                ? config?.Blocks.FirstOrDefault(b => b.ContentElementTypeKey == itemTypeKey.Value)
                : null;

            int areaGridColumns = blockConfig?.AreaGridColumns ?? config?.GridColumns ?? 12;

            ancestors.Add(item);
            foreach (BlockGridLayoutAreaItem area in item.Areas)
            {
                BlockGridConfiguration.BlockGridAreaConfiguration? areaConfig =
                    blockConfig?.Areas.FirstOrDefault(a => a.Key == area.Key);

                int areaSpan = Math.Clamp(areaConfig?.ColumnSpan ?? areaGridColumns, 1, areaGridColumns);
                double areaFraction = fraction * areaSpan / areaGridColumns;

                // Items within an area lay out on a grid of the area's column span.
                LayoutMatch? matched = FindLayout(area.Items, targetKey, areaFraction, areaSpan, config, value, ancestors);
                if (matched != null)
                    return matched;
            }
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        return null;
    }

    /// <summary>
    /// Collects the keys of all child blocks in depth-first (document) order, so the client
    /// can map click positions inside the rendered preview back to editable blocks.
    /// </summary>
    private static void CollectChildRefs(BlockGridLayoutItem layout, BlockGridValue value, List<RippleChildRef> refs)
    {
        foreach (BlockGridLayoutAreaItem area in layout.Areas)
        {
            foreach (BlockGridLayoutItem item in area.Items)
            {
                string alias = value.ContentData.FirstOrDefault(x => x.Key == item.ContentKey)?.ContentTypeAlias ?? string.Empty;
                refs.Add(new RippleChildRef(item.ContentKey, alias));
                CollectChildRefs(item, value, refs);
            }
        }
    }

    /// <summary>
    /// Recursively builds the Areas tree of a grid block so the preview can render its children
    /// exactly like the frontend does.
    /// </summary>
    private void PopulateAreas(
        BlockGridItem item,
        BlockGridLayoutItem layout,
        BlockGridConfiguration config,
        BlockGridValue value,
        IPublishedContent owner)
    {
        BlockGridConfiguration.BlockGridBlockConfiguration? blockConfig =
            config.Blocks.FirstOrDefault(b => b.ContentElementTypeKey == item.Content.ContentType.Key);

        item.GridColumns = config.GridColumns ?? 12;
        item.AreaGridColumns = blockConfig?.AreaGridColumns ?? config.GridColumns ?? 12;

        if (blockConfig == null || layout.Areas.Length == 0)
        {
            item.Areas = [];
            return;
        }

        var areaConfigMap = blockConfig.Areas.ToDictionary(a => a.Key);

        item.Areas = layout.Areas
            .Select(area =>
            {
                if (!areaConfigMap.TryGetValue(area.Key, out BlockGridConfiguration.BlockGridAreaConfiguration? areaConfig))
                    return null;

                List<BlockGridItem> children = area.Items
                    .Select(childLayout =>
                    {
                        BlockItemData? childContentData = value.ContentData.FirstOrDefault(x => x.Key == childLayout.ContentKey);
                        if (childContentData == null)
                            return null;

                        BlockItemData? childSettingsData = childLayout.SettingsKey is { } childSettingsKey
                            ? value.SettingsData.FirstOrDefault(x => x.Key == childSettingsKey)
                            : null;

                        try
                        {
                            IPublishedElement childContent = _dataConverter.ConvertToElement(childContentData, owner);
                            IPublishedElement? childSettings = childSettingsData != null ? _dataConverter.ConvertToElement(childSettingsData, owner) : null;

                            BlockGridItem child = _modelFactory.CreateGridItem(childContent, childSettings, childLayout.ContentKey, childLayout.SettingsKey);
                            child.RowSpan = childLayout.RowSpan ?? 1;
                            child.ColumnSpan = childLayout.ColumnSpan ?? areaConfig.ColumnSpan ?? 12;

                            PopulateAreas(child, childLayout, config, value, owner);
                            return child;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "RipplePreview: failed to build child block '{ChildKey}' in area '{AreaAlias}'.",
                                childLayout.ContentKey, areaConfig.Alias);
                            return null;
                        }
                    })
                    .WhereNotNull()
                    .ToList();

                return new BlockGridArea(children, areaConfig.Alias!, areaConfig.RowSpan ?? 1, areaConfig.ColumnSpan ?? 12);
            })
            .WhereNotNull()
            .ToArray();
    }
}
