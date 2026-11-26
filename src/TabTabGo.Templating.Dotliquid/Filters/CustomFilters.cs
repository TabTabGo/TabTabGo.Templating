using System.Globalization;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>Filters that do not fit another group.</summary>
public class CustomFilters
{
    /// <summary>Returns the English name of a country from its ISO code; a code that is not valid is returned unchanged (F-13).</summary>
    /// <param name="countryCode">The ISO 3166 code, for example <c>AE</c>.</param>
    public static string? CountryName(string? countryCode) => CountryName(countryCode, "en");

    /// <summary>
    /// Returns the name of a country from its ISO code. For an Arabic culture the region's native name is returned; otherwise the English
    /// name. A code that is not valid is returned unchanged.
    /// </summary>
    /// <param name="countryCode">The ISO 3166 code, for example <c>AE</c>.</param>
    /// <param name="culture">Culture name; only the language matters.</param>
    public static string? CountryName(string? countryCode, string? culture)
    {
        if (string.IsNullOrEmpty(countryCode))
        {
            return countryCode;
        }

        try
        {
            var region = new RegionInfo(countryCode);
            var arabic = !string.IsNullOrEmpty(culture) && new CultureInfo(culture).TwoLetterISOLanguageName == "ar";
            return arabic ? region.NativeName : region.EnglishName;
        }
        catch (ArgumentException)
        {
            return countryCode;
        }
    }
}
