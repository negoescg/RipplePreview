namespace RipplePreview.Models;

/// <summary>
/// Settings served to the backoffice client, used to decide which custom views to register.
/// </summary>
public class RippleSettingsResponse
{
    public int DesignWidth { get; set; }
    public RippleEditorSettingsResponse BlockGrid { get; set; } = new();
    public RippleEditorSettingsResponse BlockList { get; set; } = new();
    public RippleEditorSettingsResponse RichText { get; set; } = new();
    public RippleEditorSettingsResponse SingleBlock { get; set; } = new();
}

/// <summary>
/// Per-editor settings served to the backoffice client.
/// </summary>
public class RippleEditorSettingsResponse
{
    public bool Enabled { get; set; }

    /// <summary>Resolved element type aliases to register the custom view for. Empty = all.</summary>
    public List<string> ContentTypes { get; set; } = [];

    /// <summary>Element types whose areas render inside the preview (Block Grid only).</summary>
    public List<string> FullAreaPreviewContentTypes { get; set; } = [];

    /// <summary>Element types whose chrome stacks above their areas instead of backdropping them (Block Grid only).</summary>
    public List<string> StackedAreaPreviewContentTypes { get; set; } = [];
}
