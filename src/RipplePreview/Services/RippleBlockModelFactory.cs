using System.Collections.Concurrent;
using System.Reflection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace RipplePreview.Services;

/// <summary>
/// Creates typed BlockGridItem / BlockListItem instances for rendering. When no ModelsBuilder
/// model exists for an element type, falls back to untyped IPublishedElement block items so
/// previews still work without generated models.
/// </summary>
public interface IRippleBlockModelFactory
{
    BlockGridItem CreateGridItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey);
    BlockListItem CreateListItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey);
    RichTextBlockItem CreateRichTextItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey);
}

public class RippleBlockModelFactory : IRippleBlockModelFactory
{
    private readonly IPublishedValueFallback _publishedValueFallback;
    private readonly BlockEditorConverter _blockEditorConverter;

    private static readonly ConcurrentDictionary<Type, ConstructorInfo> ModelConstructorCache = new();
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> BlockItemConstructorCache = new();

    public RippleBlockModelFactory(IPublishedValueFallback publishedValueFallback, BlockEditorConverter blockEditorConverter)
    {
        _publishedValueFallback = publishedValueFallback;
        _blockEditorConverter = blockEditorConverter;
    }

    public BlockGridItem CreateGridItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey)
        => (BlockGridItem)CreateItem(typeof(BlockGridItem<>), typeof(BlockGridItem<,>), contentElement, settingsElement, contentKey, settingsKey);

    public BlockListItem CreateListItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey)
        => (BlockListItem)CreateItem(typeof(BlockListItem<>), typeof(BlockListItem<,>), contentElement, settingsElement, contentKey, settingsKey);

    public RichTextBlockItem CreateRichTextItem(IPublishedElement contentElement, IPublishedElement? settingsElement, Guid contentKey, Guid? settingsKey)
        => (RichTextBlockItem)CreateItem(typeof(RichTextBlockItem<>), typeof(RichTextBlockItem<,>), contentElement, settingsElement, contentKey, settingsKey);

    private object CreateItem(
        Type openContentOnlyType,
        Type openContentSettingsType,
        IPublishedElement contentElement,
        IPublishedElement? settingsElement,
        Guid contentKey,
        Guid? settingsKey)
    {
        Type contentModelType = ResolveModelType(contentElement);
        object contentInstance = CreateModel(contentModelType, contentElement);

        Type? settingsModelType = settingsElement != null ? ResolveModelType(settingsElement) : null;
        object? settingsInstance = settingsModelType != null && settingsElement != null
            ? CreateModel(settingsModelType, settingsElement)
            : null;

        Type itemType = settingsModelType != null
            ? openContentSettingsType.MakeGenericType(contentModelType, settingsModelType)
            : openContentOnlyType.MakeGenericType(contentModelType);

        ConstructorInfo ctor = BlockItemConstructorCache.GetOrAdd(itemType, static type =>
            type.GetConstructors().FirstOrDefault(c =>
                c.GetParameters().Length == 4 && c.GetParameters()[0].ParameterType == typeof(Udi))
            ?? throw new InvalidOperationException($"Type {type.Name} does not have the expected (Udi, content, Udi?, settings) constructor."));

        Udi contentUdi = Udi.Create(Umbraco.Cms.Core.Constants.UdiEntityType.Element, contentKey);
        Udi? settingsUdi = settingsKey.HasValue && settingsKey.Value != Guid.Empty
            ? Udi.Create(Umbraco.Cms.Core.Constants.UdiEntityType.Element, settingsKey.Value)
            : null;

        return ctor.Invoke([contentUdi, contentInstance, settingsUdi, settingsInstance]);
    }

    private Type ResolveModelType(IPublishedElement element)
    {
        Type modelType = _blockEditorConverter.GetModelType(element.ContentType.Key);
        // GetModelType returns IPublishedElement when no generated model exists — that's our untyped fallback.
        return modelType;
    }

    private object CreateModel(Type modelType, IPublishedElement element)
    {
        if (modelType == typeof(IPublishedElement) || modelType.IsInstanceOfType(element) && modelType.IsInterface)
            return element;

        ConstructorInfo ctor = ModelConstructorCache.GetOrAdd(modelType, static type =>
            type.GetConstructor([typeof(IPublishedElement), typeof(IPublishedValueFallback)])
            ?? throw new InvalidOperationException($"Type {type.Name} does not have the expected (IPublishedElement, IPublishedValueFallback) constructor."));

        return ctor.Invoke([element, _publishedValueFallback]);
    }
}
