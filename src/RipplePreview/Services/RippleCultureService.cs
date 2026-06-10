using System.Globalization;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace RipplePreview.Services;

/// <summary>
/// Applies the requested culture to both Umbraco's variation context (variant property values)
/// and the thread culture (dictionary items, date/number formatting in views).
/// </summary>
public class RippleCultureService
{
    private readonly IVariationContextAccessor _variationContextAccessor;

    public RippleCultureService(IVariationContextAccessor variationContextAccessor)
    {
        _variationContextAccessor = variationContextAccessor;
    }

    public void SetCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return;

        _variationContextAccessor.VariationContext = new VariationContext(culture);

        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
        }
        catch (CultureNotFoundException)
        {
            // Variation context is still applied; thread culture stays as-is.
        }
    }
}
