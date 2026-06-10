namespace RipplePreview.Configuration;

/// <summary>
/// Options for Ripple Preview, bound from the "RipplePreview" appsettings section.
/// </summary>
public class RipplePreviewOptions
{
    /// <summary>
    /// The viewport width (px) previews are rendered at inside the preview iframe.
    /// Viewport-relative units (vw/vh) and media queries resolve against this width,
    /// then the preview is scaled down to fit the block entry. Default: 1440.
    /// </summary>
    public int DesignWidth { get; set; } = 1440;

    /// <summary>
    /// Logs every property value conversion (editor format to intermediate format) at
    /// Information level. Useful when a property renders differently in previews than on
    /// the frontend. Default: false.
    /// </summary>
    public bool Diagnostics { get; set; }

    /// <summary>Block Grid editor preview settings.</summary>
    public RippleEditorOptions BlockGrid { get; set; } = new();

    /// <summary>Block List editor preview settings.</summary>
    public RippleEditorOptions BlockList { get; set; } = new();

    /// <summary>Rich Text Editor block preview settings.</summary>
    public RippleEditorOptions RichText { get; set; } = new();

    /// <summary>Single Block editor preview settings (Umbraco 17.3+).</summary>
    public RippleEditorOptions SingleBlock { get; set; } = new();

    /// <summary>Gets the editor options for the given kind.</summary>
    public RippleEditorOptions ForKind(RippleEditorKind kind) => kind switch
    {
        RippleEditorKind.BlockGrid => BlockGrid,
        RippleEditorKind.BlockList => BlockList,
        RippleEditorKind.RichText => RichText,
        RippleEditorKind.SingleBlock => SingleBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// Per-editor (Block Grid / Block List) preview settings.
/// </summary>
public class RippleEditorOptions
{
    /// <summary>Whether previews are enabled for this editor type. Default: false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Allowlist of element type aliases to render previews for. Empty = all element types.
    /// </summary>
    public List<string> ContentTypes { get; set; } = [];

    /// <summary>
    /// Element type aliases to exclude. Only applied when <see cref="ContentTypes"/> is empty.
    /// </summary>
    public List<string> IgnoredContentTypes { get; set; } = [];

    /// <summary>
    /// Additional view locations searched before the defaults. Use {0} as the alias placeholder,
    /// e.g. "/Views/Partials/myBlocks/{0}.cshtml".
    /// </summary>
    public List<string> ViewLocations { get; set; } = [];

    /// <summary>Stylesheet paths injected into every preview document, e.g. "/css/site.css".</summary>
    public List<string> Stylesheets { get; set; } = [];

    /// <summary>Script paths appended to every preview document, e.g. "/js/site.js".</summary>
    public List<string> Scripts { get; set; } = [];

    /// <summary>
    /// Optional path to a wrapper partial view rendered around the block markup, letting sites
    /// replicate their grid chrome (containers, wrapper classes). The wrapper receives the block
    /// item as its model and the inner markup via ViewData["rippleInnerHtml"].
    /// </summary>
    public string? WrapperView { get; set; }

    /// <summary>
    /// Block Grid only: element type aliases whose areas (child blocks) are rendered inside the
    /// preview iframe rather than as natively editable backoffice areas. Full visual fidelity,
    /// but children can then only be edited through their own workspace.
    /// </summary>
    public List<string> FullAreaPreviewContentTypes { get; set; } = [];

    /// <summary>
    /// Block Grid only: element type aliases whose own chrome renders as a normal block above
    /// their natively editable areas, instead of as a stretched backdrop behind them. Use for
    /// parents with flow content (headings, intros) above their areas. Blocks with areas not
    /// listed here or in <see cref="FullAreaPreviewContentTypes"/> default to backdrop mode.
    /// </summary>
    public List<string> StackedAreaPreviewContentTypes { get; set; } = [];
}
