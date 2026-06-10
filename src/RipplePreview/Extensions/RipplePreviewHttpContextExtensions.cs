using Microsoft.AspNetCore.Http;

namespace RipplePreview.Extensions;

/// <summary>
/// Helpers for views/services that need to know whether they are rendering inside a Ripple Preview.
/// </summary>
public static class RipplePreviewHttpContextExtensions
{
    /// <summary>
    /// Returns true while the current request is rendering a Ripple Preview block.
    /// Usable from views via Context: <c>@if (Context.IsRipplePreview()) { ... }</c>.
    /// </summary>
    public static bool IsRipplePreview(this HttpContext? httpContext)
        => httpContext?.Items.ContainsKey(RippleConstants.HttpContextItems.IsRipplePreview) == true;
}
