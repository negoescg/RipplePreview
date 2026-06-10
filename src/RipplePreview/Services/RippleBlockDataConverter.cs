using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RipplePreview.Configuration;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Cache.PropertyEditors;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.Editors;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Infrastructure.Serialization;
using static Umbraco.Cms.Core.Constants;

namespace RipplePreview.Services;

/// <summary>
/// Converts raw backoffice block editor values into published elements.
/// Property values arrive in editor format and are normalized through each
/// property editor's FromEditor before running the published value converters.
/// Adapted from Umbraco.Community.BlockPreview (MIT, © Rick Butterfield and contributors).
/// </summary>
public interface IRippleBlockDataConverter
{
    BlockEditorData<BlockGridValue, BlockGridLayoutItem>? DeserializeBlockGrid(string? blockData);
    BlockEditorData<BlockListValue, BlockListLayoutItem>? DeserializeBlockList(string? blockData);
    BlockEditorData<RichTextBlockValue, RichTextBlockLayoutItem>? DeserializeRichText(string? blockData);
    BlockEditorData<SingleBlockValue, SingleBlockLayoutItem>? DeserializeSingleBlock(string? blockData);
    IPublishedElement ConvertToElement(BlockItemData data, IPublishedElement owner);
}

public class RippleBlockDataConverter : IRippleBlockDataConverter
{
    private readonly BlockEditorConverter _blockEditorConverter;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger<RippleBlockDataConverter> _logger;
    private readonly PropertyEditorCollection _propertyEditors;
    private readonly IDataTypeConfigurationCache _dataTypeConfigurationCache;
    private readonly BlockEditorValues<BlockGridValue, BlockGridLayoutItem> _blockGridEditorValues;
    private readonly BlockEditorValues<BlockListValue, BlockListLayoutItem> _blockListEditorValues;
    private readonly BlockEditorValues<RichTextBlockValue, RichTextBlockLayoutItem> _richTextBlockEditorValues;
    private readonly BlockEditorValues<SingleBlockValue, SingleBlockLayoutItem> _singleBlockEditorValues;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly IOptionsMonitor<RipplePreviewOptions> _options;

    public RippleBlockDataConverter(
        BlockEditorConverter blockEditorConverter,
        IJsonSerializer jsonSerializer,
        IBlockEditorElementTypeCache elementTypeCache,
        ILogger<RippleBlockDataConverter> logger,
        PropertyEditorCollection propertyEditors,
        IDataTypeConfigurationCache dataTypeConfigurationCache,
        IOptionsMonitor<RipplePreviewOptions> options)
    {
        _options = options;
        _blockEditorConverter = blockEditorConverter;
        _jsonSerializer = jsonSerializer;
        _logger = logger;
        _propertyEditors = propertyEditors;
        _dataTypeConfigurationCache = dataTypeConfigurationCache;

        _blockGridEditorValues = new BlockEditorValues<BlockGridValue, BlockGridLayoutItem>(
            new BlockGridEditorDataConverter(jsonSerializer), elementTypeCache, logger);
        _blockListEditorValues = new BlockEditorValues<BlockListValue, BlockListLayoutItem>(
            new BlockListEditorDataConverter(jsonSerializer), elementTypeCache, logger);
        _richTextBlockEditorValues = new BlockEditorValues<RichTextBlockValue, RichTextBlockLayoutItem>(
            new RichTextEditorBlockDataConverter(jsonSerializer), elementTypeCache, logger);
        _singleBlockEditorValues = new BlockEditorValues<SingleBlockValue, SingleBlockLayoutItem>(
            new SingleBlockEditorDataConverter(jsonSerializer), elementTypeCache, logger);

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(),
                new JsonUdiConverter(),
                new JsonUdiRangeConverter(),
                new JsonObjectConverter(),
                new JsonBlockValueConverter()
            }
        };
    }

    public BlockEditorData<BlockGridValue, BlockGridLayoutItem>? DeserializeBlockGrid(string? blockData)
        => _blockGridEditorValues.DeserializeAndClean(blockData);

    public BlockEditorData<BlockListValue, BlockListLayoutItem>? DeserializeBlockList(string? blockData)
        => _blockListEditorValues.DeserializeAndClean(blockData);

    public BlockEditorData<RichTextBlockValue, RichTextBlockLayoutItem>? DeserializeRichText(string? blockData)
        => _richTextBlockEditorValues.DeserializeAndClean(blockData);

    public BlockEditorData<SingleBlockValue, SingleBlockLayoutItem>? DeserializeSingleBlock(string? blockData)
        => _singleBlockEditorValues.DeserializeAndClean(blockData);

    public IPublishedElement ConvertToElement(BlockItemData data, IPublishedElement owner)
    {
        FormatBlockItem(data);

        var element = _blockEditorConverter.ConvertToElement(owner, data, PropertyCacheLevel.None, preview: true);
        return element ?? throw new InvalidOperationException($"Unable to convert element of type '{data.ContentTypeAlias}'.");
    }

    private void FormatBlockItem(BlockItemData data)
    {
        foreach (BlockPropertyValue property in data.Values)
        {
            string? propertyAsString = property.Value?.ToString();

            if (property.EditorAlias == PropertyEditors.Aliases.RichText)
            {
                if (RichTextPropertyEditorHelper.TryParseRichTextEditorValue(property.Value, _jsonSerializer, _logger, out RichTextEditorValue? richTextEditorValue)
                    && richTextEditorValue != null)
                {
                    var blockValue = _richTextBlockEditorValues.DeserializeAndClean(_jsonSerializer.Serialize(richTextEditorValue.Blocks));
                    if (blockValue != null)
                    {
                        FormatBlockData(blockValue.BlockValue.ContentData);
                        FormatBlockData(blockValue.BlockValue.SettingsData);

                        richTextEditorValue.Blocks = blockValue.BlockValue;
                        property.Value = JsonSerializer.Serialize(richTextEditorValue, _jsonSerializerOptions);
                    }
                }
            }
            else if (property.EditorAlias == PropertyEditors.Aliases.BlockGrid)
            {
                var blockValue = _blockGridEditorValues.DeserializeAndClean(propertyAsString);
                if (blockValue != null)
                {
                    FormatBlockData(blockValue.BlockValue.ContentData);
                    FormatBlockData(blockValue.BlockValue.SettingsData);
                    property.Value = JsonSerializer.Serialize(blockValue.BlockValue, _jsonSerializerOptions);
                }
            }
            else if (property.EditorAlias == PropertyEditors.Aliases.BlockList)
            {
                var blockValue = _blockListEditorValues.DeserializeAndClean(propertyAsString);
                if (blockValue != null)
                {
                    FormatBlockData(blockValue.BlockValue.ContentData);
                    FormatBlockData(blockValue.BlockValue.SettingsData);
                    property.Value = JsonSerializer.Serialize(blockValue.BlockValue, _jsonSerializerOptions);
                }
            }
            else
            {
                ConvertPropertyValue(property);
            }
        }
    }

    private void FormatBlockData(List<BlockItemData>? blockData)
    {
        if (blockData == null || blockData.Count == 0)
            return;

        foreach (BlockItemData itemData in blockData)
            FormatBlockItem(itemData);
    }

    private void ConvertPropertyValue(BlockPropertyValue propertyData)
    {
        if (propertyData.PropertyType is null || propertyData.EditorAlias is null)
        {
            if (_options.CurrentValue.Diagnostics)
            {
                _logger.LogInformation(
                    "RipplePreview diag: skipped '{Alias}' (editor '{EditorAlias}') — no resolved property type.",
                    propertyData.Alias, propertyData.EditorAlias ?? "null");
            }
            return;
        }

        if (!_propertyEditors.TryGet(propertyData.EditorAlias, out IDataEditor? editor))
        {
            if (_options.CurrentValue.Diagnostics)
            {
                _logger.LogInformation(
                    "RipplePreview diag: no property editor found for '{Alias}' (editor '{EditorAlias}').",
                    propertyData.Alias, propertyData.EditorAlias);
            }
            return;
        }

        var originalValue = propertyData.Value;

        try
        {
            var config = _dataTypeConfigurationCache.GetConfiguration(propertyData.PropertyType.DataTypeKey);
            var editorValue = new ContentPropertyData(propertyData.Value, config);
            propertyData.Value = editor.GetValueEditor().FromEditor(editorValue, null);

            if (_options.CurrentValue.Diagnostics)
            {
                _logger.LogInformation(
                    "RipplePreview diag: '{Alias}' [{EditorAlias}] {BeforeType}('{Before}') -> {AfterType}('{After}')",
                    propertyData.Alias,
                    propertyData.EditorAlias,
                    originalValue?.GetType().Name ?? "null",
                    Truncate(originalValue),
                    propertyData.Value?.GetType().Name ?? "null",
                    Truncate(propertyData.Value));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RipplePreview: FromEditor threw for property editor '{EditorAlias}'. Keeping the original value.",
                propertyData.EditorAlias);
            propertyData.Value = originalValue;
        }
    }

    private static string Truncate(object? value)
    {
        var text = value?.ToString() ?? "null";
        return text.Length > 120 ? text[..120] + "…" : text;
    }
}
