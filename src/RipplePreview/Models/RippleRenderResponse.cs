namespace RipplePreview.Models;

/// <summary>
/// Response payload for a rendered block preview.
/// </summary>
public class RippleRenderResponse
{
    /// <summary>The complete HTML document to load into the preview iframe (srcdoc).</summary>
    public string Html { get; set; } = string.Empty;

    /// <summary>
    /// The block's width as a fraction of the full design-width viewport (0..1),
    /// computed from the layout tree (column spans through all nesting levels).
    /// </summary>
    public double WidthFraction { get; set; } = 1d;
}
