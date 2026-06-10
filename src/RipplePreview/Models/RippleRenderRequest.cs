using System.Text.Json.Nodes;

namespace RipplePreview.Models;

/// <summary>
/// Request payload for rendering a single block preview.
/// </summary>
public class RippleRenderRequest
{
    /// <summary>
    /// The raw block editor value: { layout, contentData, settingsData, expose }.
    /// The layout should contain the full layout tree of the property so nested
    /// blocks can be located and their true width computed.
    /// </summary>
    public JsonNode? BlockValue { get; set; }

    /// <summary>The document (content) key the property belongs to, when known/saved.</summary>
    public Guid? DocumentKey { get; set; }

    /// <summary>The content type key (document type or element type) declaring the block editor property.</summary>
    public Guid DocumentTypeKey { get; set; }

    /// <summary>The alias of the block editor property.</summary>
    public string PropertyAlias { get; set; } = string.Empty;

    /// <summary>The culture to render in (null/empty = resolve automatically).</summary>
    public string? Culture { get; set; }

    /// <summary>The key of the block's content element.</summary>
    public Guid ContentKey { get; set; }

    /// <summary>The key of the block's settings element, if any.</summary>
    public Guid? SettingsKey { get; set; }

    /// <summary>
    /// Block Grid only: when true the block's areas (child blocks) are rendered inside the preview.
    /// When false, areas are left empty so the client can render them as native editable areas.
    /// </summary>
    public bool IncludeAreas { get; set; }

    /// <summary>
    /// Block Grid only: render the block's own chrome as a stretched backdrop document — the
    /// client lays it behind the natively editable areas so children visually sit inside the
    /// parent's background exactly like the frontend. Skips the wrapper view.
    /// </summary>
    public bool Backdrop { get; set; }
}
