using System.Globalization;
using DotLiquid;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid;
using TabTabGo.Templating.Liquid.Extensions;
using TabTabGo.Templating.Liquid.Filters;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>F-1 to F-15: filter and helper defects of the 8.x line, each pinned by a test that failed first.</summary>
public sealed class FilterFixesTests
{
    private static string Render(string template, object? data = null, string culture = "en-US")
    {
        using var scope = new CultureScope("en-US");
        return new Liquid.TemplatingEngine().RenderString(template, data ?? new { }, culture);
    }

    private static Hash H(params (string Key, object? Value)[] pairs)
    {
        var hash = new Hash();
        foreach (var (key, value) in pairs)
        {
            hash[key] = value;
        }

        return hash;
    }

    // ---- F-1 MarkNumbers

    [Fact]
    public void MarkNumbers_LettersBeforeDigits_NotMarked()
    {
        Assert.Equal("ABC&lrm;123", ValueFilters.MarkNumbers("ABC123"));
        Assert.Equal("a:b&lrm;9", ValueFilters.MarkNumbers("a:b9"));
    }

    [Theory]
    [InlineData("AED 1,234.50", "AED &lrm;1,234.50")]
    [InlineData("$12", "&lrm;$12")]
    [InlineData("total (12)", "total &lrm;(12)")]
    [InlineData("+971 4 434 0300", "&lrm;+971 &lrm;4 &lrm;434 &lrm;0300")]
    [InlineData("no digits", "no digits")]
    public void MarkNumbers_Currency_Marked(string input, string expected) => Assert.Equal(expected, ValueFilters.MarkNumbers(input));

    // ---- F-2 ToWords

    [Fact]
    public void ToWords_1_999_RoundsToTwo()
    {
        Assert.Equal("Two", 1.999m.ToWords("en"));
        Assert.Equal("One points Fifty", 1.5m.ToWords("en"));
    }

    [Fact]
    public void ToWords_EnUs_Capitalised()
    {
        Assert.Equal("Twenty-One", 21m.ToWords("en-US"));
        Assert.Equal("Twenty-One", 21m.ToWords("en"));
    }

    [Fact]
    public void ToWords_Huge_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 1e20m.ToWords("en"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueFilters.ToWords(-1e20m, "en"));
    }

    [Fact]
    public void ToWords_Ar_Golden()
    {
        Assert.Equal("واحد و عشرون", 21m.ToWords("ar"));
        Assert.Equal("مئة", 100m.ToWords("ar-AE"));
    }

    [Fact]
    public void Capitalize_EmptyWords_AreSkipped()
    {
        Assert.Equal("One Two", HumanizerExtensions.Capitalize("one  two"));
        Assert.Equal(string.Empty, HumanizerExtensions.Capitalize(""));
    }

    // ---- F-3 Union

    [Fact]
    public void Union_EmptySecond_ReturnsFirst()
    {
        Assert.Equal("1,2,3,", Render("{% assign u = a | Union: none %}{% for x in u %}{{ x }},{% endfor %}", new { a = new[] { 1, 2, 3 }, none = new int[0] }));
        var deduplicated = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Union(new object[] { 1, 1, 2 }, Array.Empty<object>()));
        Assert.Equal(new object[] { 1, 2 }, deduplicated);
        Assert.Equal(new object[] { 1 }, Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Union(new object[] { 1 }, null!)));
    }

    // ---- F-4 Distinct / HashEqualityComparer

    [Fact]
    public void Distinct_EqualHashes_One()
    {
        var list = new object[] { H(("k", 1)), H(("k", 1)), H(("k", 2)) };

        var result = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Distinct(list)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Distinct_Ints_Distinct()
    {
        var result = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Distinct(new object[] { 1, 1, 2 })).ToList();

        Assert.Equal(new object[] { 1, 2 }, result);
    }

    [Fact]
    public void Distinct_Strings_KeepOrder_AndNullArray_Null()
    {
        Assert.Equal(new object[] { "b", "a" }, Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Distinct(new object[] { "b", "a", "b" })).ToList());
        Assert.Null(CollectionFilters.Distinct(null!));
    }

    [Fact]
    public void HashEqualityComparer_EqualHashes_SameHashCode()
    {
        var comparer = new HashEqualityComparer();
        var a = H(("k", 1), ("s", "x"));
        var b = H(("s", "x"), ("k", 1));

        Assert.True(comparer.Equals(a, b));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.NotEqual(comparer.GetHashCode(a), comparer.GetHashCode(H(("k", 2), ("s", "x"))));
    }

    [Fact]
    public void HashEqualityComparer_NestedValues_CompareByContent()
    {
        var comparer = new HashEqualityComparer();

        Assert.True(comparer.Equals(H(("n", H(("a", 1)))), H(("n", H(("a", 1))))));
        Assert.True(comparer.Equals(H(("l", new List<object> { 1, 2 })), H(("l", new List<object> { 1, 2 }))));
        Assert.False(comparer.Equals(H(("l", new List<object> { 1, 2 })), H(("l", new List<object> { 1, 3 }))));
        Assert.True(comparer.Equals(H(("n", null)), H(("n", null))));
        Assert.False(comparer.Equals(null!, H()));
    }

    // ---- F-5 OrderBy

    [Fact]
    public void OrderBy_ValueTypes()
    {
        Assert.Equal(new object[] { 1, 2, 3 }, CollectionFilters.OrderBy(new List<int> { 3, 1, 2 }, "").Cast<object>().ToArray());
    }

    [Fact]
    public void OrderBy_MixedNumeric()
    {
        var items = new List<object> { H(("n", 2L)), H(("n", 1.5)), H(("n", 3)) };

        var result = CollectionFilters.OrderBy(items, "n").Cast<Hash>().Select(h => Convert.ToDouble(h["n"], CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(new[] { 1.5, 2, 3 }, result);
    }

    [Fact]
    public void OrderBy_Null_Empty()
    {
        Assert.Empty(CollectionFilters.OrderBy(null!, "n").Cast<object>());
    }

    [Fact]
    public void OrderBy_HashesWithoutProperty_KeepOrder_AndIsStable()
    {
        var first = H(("g", 1), ("id", "a"));
        var second = H(("g", 1), ("id", "b"));
        var third = H(("g", 0), ("id", "c"));

        Assert.Equal(new[] { first, second }, CollectionFilters.OrderBy(new object[] { first, second }, "").Cast<Hash>().ToArray());
        Assert.Equal(new[] { third, first, second }, CollectionFilters.OrderBy(new object[] { first, second, third }, "g").Cast<Hash>().ToArray());
    }

    // ---- F-6 GroupBy

    [Fact]
    public void GroupBy_NullKey_EmptyGroup()
    {
        var result = Render("{% assign g = items | GroupBy: 'k' %}{% for x in g %}[{{ x.Key }}]{% endfor %}", new { items = new object[] { new { k = (string?)null }, new { k = "a" } } });

        Assert.Equal("[][a]", result);
    }

    // ---- F-7 SelectMany / Flatten

    [Fact]
    public void SelectMany_NonHashItems_AreSkipped_NullArray_Null()
    {
        var result = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.SelectMany(new object?[] { H(("a", 1)), "text", null }, "a"));

        Assert.Equal(new object[] { 1 }, result);
        Assert.Null(CollectionFilters.SelectMany(null!, "a"));
    }

    [Fact]
    public void Flatten_NullNestedList_IsSkipped()
    {
        var orders = new object[] { H(("lines", null)), H(("lines", new List<object> { 1, 2 })) };

        var result = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Flatten(orders, "lines"));

        Assert.Equal(new object[] { 1, 2 }, result);
    }

    [Fact]
    public void Flatten_StringNestedValue_IsAScalar_NotCharacters()
    {
        var result = Assert.IsAssignableFrom<IEnumerable<object>>(CollectionFilters.Flatten(new object[] { H(("name", "abc")) }, "name"));

        Assert.Equal(new object[] { "abc" }, result);
    }

    // ---- F-8 JSelectTokenValue

    [Fact]
    public void JSelectTokenValue_Scalar_Value()
    {
        var json = JObject.Parse("""{"s":"text","n":5,"b":true,"o":{"x":1}}""");

        Assert.Equal("text", JObjectFilters.JSelectTokenValue(json, "$.s"));
        Assert.Equal(5L, JObjectFilters.JSelectTokenValue(json, "$.n"));
        Assert.Equal(true, JObjectFilters.JSelectTokenValue(json, "$.b"));
    }

    [Fact]
    public void JSelectTokenValue_Object_Token_AndMissing_Null()
    {
        var json = JObject.Parse("""{"o":{"x":1}}""");

        Assert.Null(JObjectFilters.JSelectTokenValue(json, "$.missing"));
        Assert.NotNull(JObjectFilters.JSelectTokenValue(json, "$.o"));
    }

    // ---- F-9 ToTimeZone

    private static readonly DateTime Winter = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Summer = new(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void ToTimeZone_IanaId()
    {
        var result = Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Winter, "Asia/Dubai"));

        Assert.Equal(TimeSpan.FromHours(4), result.Offset);
        Assert.Equal(14, result.Hour);
    }

    [Fact]
    public void ToTimeZone_WindowsId()
    {
        var result = Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Winter, "Arabian Standard Time"));

        Assert.Equal(TimeSpan.FromHours(4), result.Offset);
    }

    [Fact]
    public void ToTimeZone_Dst()
    {
        Assert.Equal(TimeSpan.Zero, Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Winter, "Europe/London")).Offset);
        Assert.Equal(TimeSpan.FromHours(1), Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Summer, "Europe/London")).Offset);
    }

    [Fact]
    public void ToTimeZone_LegacyStandardName()
    {
        var name = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai").StandardName;

        var result = Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Winter, name));

        Assert.Equal(TimeSpan.FromHours(4), result.Offset);
    }

    [Fact]
    public void ToTimeZone_UnknownZone_ReturnsInput_AndEmptyZoneToo()
    {
        Assert.Equal(Winter, ValueFilters.ToTimeZone(Winter, "Nowhere/Land"));
        Assert.Equal(Winter, ValueFilters.ToTimeZone(Winter, ""));
    }

    // ---- F-10 OffsetDate / FormatDate

    [Fact]
    public void OffsetDate_NonWholeMinuteOffset_RoundsToMinutes()
    {
        var result = ValueFilters.OffsetDate(Winter, 3600 + 29);

        Assert.Equal(TimeSpan.FromMinutes(60), result!.Value.Offset);
    }

    [Fact]
    public void OffsetDate_Today_IsTheUtcDate()
    {
        var result = ValueFilters.OffsetDate("Today", 0)!.Value;

        Assert.Equal(DateTime.UtcNow.Date, result.UtcDateTime.Date);
        Assert.Equal(TimeSpan.Zero, result.TimeOfDay);
    }

    [Fact]
    public void OffsetDate_Now_IsCloseToUtcNow()
    {
        var result = ValueFilters.OffsetDate("now", 0)!.Value;

        Assert.True((DateTimeOffset.UtcNow - result).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void OffsetDate_UnsupportedInput_ThrowsArgumentException_NullIsNull()
    {
        Assert.Throws<ArgumentException>(() => ValueFilters.OffsetDate(5, 0));
        Assert.Throws<ArgumentException>(() => ValueFilters.OffsetDate("not a date", 0));
        Assert.Null(ValueFilters.OffsetDate(null!, 0));
    }

    [Fact]
    public void OffsetDate_UnspecifiedDateTime_TreatedAsUtc()
    {
        var unspecified = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Unspecified);

        var result = ValueFilters.OffsetDate(unspecified, 7200)!.Value;

        Assert.Equal(12, result.Hour);
        Assert.Equal(TimeSpan.FromHours(2), result.Offset);
    }

    [Fact]
    public void FormatDate_Now_IsUtc_AndUnspecifiedIsUtc()
    {
        Assert.Equal("+00:00", ValueFilters.FormatDate("Now", "zzz", "en-US"));
        Assert.Equal("10:30 +00:00", ValueFilters.FormatDate(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Unspecified), "HH:mm zzz", "en-US"));
    }

    // ---- F-12 Increment

    [Fact]
    public void Increment_Missing_StartsAtOne()
    {
        Assert.Equal("1", Render("{% increment counter %}{{ counter }}"));
    }

    [Fact]
    public void Increment_InLoop()
    {
        Assert.Equal("4", Render("{% assign n = 1 %}{% for i in (1..3) %}{% increment n %}{% endfor %}{{ n }}"));
    }

    [Fact]
    public void Increment_FromData_AndInvariantParse()
    {
        using var scope = new CultureScope("ar-SA");

        Assert.Equal("6", new Liquid.TemplatingEngine().RenderString("{% increment n %}{{ n }}", new { n = 5 }));
        Assert.Equal("8", new Liquid.TemplatingEngine().RenderString("{% assign s = '7' %}{% increment s %}{{ s }}", new { }));
    }

    // ---- F-13 CountryName

    [Fact]
    public void CountryName_InvalidCode_ReturnsInput()
    {
        Assert.Equal("XX", CustomFilters.CountryName("XX"));
        Assert.Equal("", CustomFilters.CountryName(""));
        Assert.Equal("United Arab Emirates", CustomFilters.CountryName("AE"));
    }

    [Fact]
    public void CountryName_ArabicCulture_NativeName()
    {
        Assert.Matches("[؀-ۿ]", CustomFilters.CountryName("AE", "ar"));
        Assert.Equal("United Arab Emirates", CustomFilters.CountryName("AE", "en"));
        Assert.Equal("XX", CustomFilters.CountryName("XX", "ar"));
    }

    // ---- F-14 OpenXml filters

    [Fact]
    public void OpenXmlMaxWidth_AcceptsHashAndJObject()
    {
        var hashes = new object[] { H(("name", "abcdef")), H(("name", "ab")) };
        var objects = new object[] { JObject.Parse("""{"name":"abcdef"}"""), JObject.Parse("""{"name":"ab"}""") };

        Assert.Equal("6", OpenXmlFilters.OpenXmlMaxWidth(hashes, "name", "n"));
        Assert.Equal("6", OpenXmlFilters.OpenXmlMaxWidth(objects, "name", "n"));
        Assert.Equal("9", OpenXmlFilters.OpenXmlMaxWidth(hashes, "name", "123456789"));
        Assert.Equal("3", OpenXmlFilters.OpenXmlMaxWidth(null!, "name", "abc"));
    }

    [Fact]
    public void OpenXmlMaxWidth_ThroughTheEngine()
    {
        var result = Render("{{ items | OpenXmlMaxWidth: 'name', 'x' }}", new { items = new[] { new { name = "abcd" } } });

        Assert.Equal("4", result);
    }

    [Theory]
    [InlineData("System.Single", "Number")]
    [InlineData("Single", "Number")]
    [InlineData("Byte", "Number")]
    [InlineData("SByte", "Number")]
    [InlineData("UInt16", "Number")]
    [InlineData("UInt32", "Number")]
    [InlineData("UInt64", "Number")]
    [InlineData("Float", "Number")]
    [InlineData("Boolean", "Boolean")]
    [InlineData("DateOnly", "Date")]
    [InlineData("Guid", "String")]
    public void OpenXmlType_EveryClrNumericName(string clrName, string expected) => Assert.Equal(expected, OpenXmlFilters.OpenXmlType(clrName));

    // ---- F-15 decimals

    [Fact]
    public void FormatNumber_Decimal_Exact()
    {
        var result = Render("{{ n | FormatNumber: 'N2' }}", new { n = 1234567890123456.78m });

        Assert.Equal("1,234,567,890,123,456.78", result);
    }

    // ---- B-9 (found while testing): object-typed members holding strings

    [Fact]
    public void Render_ObjectTypedStringMember_StaysAString()
    {
        var data = new Dictionary<string, object> { ["v"] = "abc", ["n"] = 5 };

        Assert.Equal("ABC", Render("{{ Value | Upcase }}", new { Value = (object)"abc" }));
        Assert.Equal("ABC", Render("{% for p in model %}{% if p.Key == 'v' %}{{ p.Value | Upcase }}{% endif %}{% endfor %}", data));
    }

    [Fact]
    public void Render_ObjectWithAnIndexer_DoesNotThrow()
    {
        Assert.Equal("x", Render("{{ Name }}", new WithIndexer()));
    }

    private sealed class WithIndexer
    {
        public string Name => "x";

        public string this[int index] => "nope";
    }
}
