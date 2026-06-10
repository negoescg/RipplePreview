namespace RipplePreview;

/// <summary>
/// Constants used throughout Ripple Preview.
/// </summary>
public static class RippleConstants
{
    public const string PackageName = "RipplePreview";

    public static class Configuration
    {
        /// <summary>The appsettings section name.</summary>
        public const string AppSettingsRoot = "RipplePreview";

        /// <summary>The OpenAPI document / MapToApi name.</summary>
        public const string ApiName = "ripple-preview";

        /// <summary>Route prefix under /umbraco.</summary>
        public const string ApiPath = "ripple-preview/api";
    }

    public static class ViewLocations
    {
        public const string BlockGrid = "/Views/Partials/blockgrid/Components/{0}.cshtml";
        public const string BlockList = "/Views/Partials/blocklist/Components/{0}.cshtml";
        public const string RichText = "/Views/Partials/richtext/Components/{0}.cshtml";
        public const string SingleBlock = "/Views/Partials/blocklist/Components/{0}.cshtml";
    }

    public static class HttpContextItems
    {
        /// <summary>Set on HttpContext.Items while a Ripple Preview render request is executing.</summary>
        public const string IsRipplePreview = "RipplePreview.IsPreview";
    }
}

/// <summary>
/// The block editor kinds Ripple Preview can render.
/// </summary>
public enum RippleEditorKind
{
    BlockGrid,
    BlockList,
    RichText,
    SingleBlock,
}
