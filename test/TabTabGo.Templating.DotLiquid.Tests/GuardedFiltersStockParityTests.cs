using System.Collections;
using System.Globalization;
using DotLiquid;
using TabTabGo.Templating.Liquid;
using Guarded = TabTabGo.Templating.Liquid.Filters.GuardedStringFilters;
using Stock = DotLiquid.StandardFilters;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>
/// M-1 (architect round 5): inside the limits the guarded filters return exactly what the stock DotLiquid 2.3.260 filters return, at every
/// syntax level and culture, including the failures. Only the size, item and time-out checks may differ.
/// </summary>
public sealed class GuardedFiltersStockParityTests
{
    private static readonly string?[] Texts =
    {
        null, "", "a", "a.b.a", "aXbXa", "ab", "a­b", "Straße", "straSSe", "ae æ", "$1", "x*y", "I i İ ı", "((", "[a-", "a|b", " ", "  ", "\t", "\u00A0", " a ", "a  b",
    };

    private static readonly string?[] Finds = { null, "", " ", "a", ".", "a.", "X", "ab", "æ", "ae", "SS", "ß", "(a)", "a|b", "(", "[a-", "\\." };

    private static readonly string?[] Replacements =
    {
        null, "", "-", "$1", "$&", "${n}", "$$", "$_", "$`", "$'", "$0", "$9", "${", "[$&]", "æ",
    };

    private static readonly string[] Cultures = { "", "en-US", "tr-TR", "de-DE", "ar-SA" };

    private static readonly SyntaxCompatibility[] Levels =
    {
        SyntaxCompatibility.DotLiquid20, SyntaxCompatibility.DotLiquid21, SyntaxCompatibility.DotLiquid22, SyntaxCompatibility.DotLiquid22a,
    };

    private static readonly object?[][] Lists =
    {
        Array.Empty<object?>(),
        new object?[] { "a" },
        new object?[] { "a", "b", "c" },
        new object?[] { 1, 2.5m, null, "x" },
        new object?[] { new[] { "a", "b" }, "c" },
        new object?[] { new object?[] { new[] { 1, 2 }, 3 }, new List<string> { "d" } },
    };

    private static Context NewContext(SyntaxCompatibility level, string culture) =>
        new(CultureInfo.GetCultureInfo(culture)) { SyntaxCompatibilityLevel = level };

    private static object? Run(Func<object?> action)
    {
        try
        {
            var value = action();
            return value is IEnumerable and not string ? string.Join("|", ((IEnumerable)value).Cast<object?>().Select(Describe)) : value;
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            return "THROWS " + ex.GetType().Name;
        }
    }

    private static string Describe(object? item) => item is not string && item is IEnumerable e ? "[" + string.Join(",", e.Cast<object?>().Select(Describe)) + "]" : item?.ToString() ?? "<nil>";

    private static void WithCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            using var scope = RenderScope.Begin(new TemplatingOptions(), null);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Append_Prepend_MatchStock()
    {
        foreach (var input in Texts)
        {
            foreach (var operand in Replacements)
            {
                Assert.Equal(Run(() => Stock.Append(input!, operand!)), Run(() => Guarded.Append(input, operand)));
                Assert.Equal(Run(() => Stock.Prepend(input!, operand!)), Run(() => Guarded.Prepend(input, operand)));
            }
        }
    }

    [Fact]
    public void Join_MatchesStock()
    {
        foreach (var culture in Cultures)
        {
            WithCulture(culture, () =>
            {
                foreach (var list in Lists)
                {
                    foreach (var glue in new string?[] { null, "", " ", ", ", "$&" })
                    {
                        Assert.Equal(Run(() => Stock.Join(list, glue!)), Run(() => Guarded.Join(list, glue)));
                    }
                }

                Assert.Equal(Run(() => Stock.Join(null!, " ")), Run(() => Guarded.Join(null, " ")));
            });
        }
    }

    [Fact]
    public void Concat_MatchesStock()
    {
        var inputs = new IEnumerable?[] { null, Lists[0], Lists[2], Lists[4] };
        foreach (var left in inputs)
        {
            foreach (var right in inputs)
            {
                Assert.Equal(Run(() => Stock.Concat(left!, right!)), Run(() => Guarded.Concat(left, right)));
            }
        }
    }

    [Fact]
    public void Replace_And_ReplaceFirst_MatchStock_AtEverySyntaxLevelAndCulture()
    {
        foreach (var culture in Cultures)
        {
            WithCulture(culture, () =>
            {
                foreach (var level in Levels)
                {
                    var context = NewContext(level, culture);
                    foreach (var input in Texts)
                    {
                        foreach (var find in Finds)
                        {
                            foreach (var replacement in Replacements.Where(r => r != null)) // a nil replacement is the one accepted divergence, below
                            {
                                var where = $"level={level} culture={culture} input={input} find={find} replacement={replacement}";
                                Assert.True(Equals(Run(() => Stock.Replace(context, input!, find!, replacement!)), Run(() => Guarded.Replace(context, input, find, replacement))),
                                    "Replace " + where);
                                Assert.True(Equals(Run(() => Stock.ReplaceFirst(context, input!, find!, replacement!)), Run(() => Guarded.ReplaceFirst(context, input, find, replacement))),
                                    "ReplaceFirst " + where);
                                Assert.True(Equals(Run(() => Stock.RemoveFirst(context, input!, find!)), Run(() => Guarded.RemoveFirst(context, input, find))),
                                    "RemoveFirst " + where);
                            }
                        }
                    }
                }
            });
        }
    }

    [Fact]
    public void Replace_NilReplacement_IsTheEmptyString_TheOneAcceptedDivergence()
    {
        // stock throws ArgumentNullException (a "Liquid error" text) in regex mode; here the match is removed, as in literal mode
        using var scope = RenderScope.Begin(new TemplatingOptions(), null);
        Assert.Equal("b", Guarded.Replace(NewContext(SyntaxCompatibility.DotLiquid20, "en-US"), "ab", "a", null));
        Assert.Equal("b", Guarded.ReplaceFirst(NewContext(SyntaxCompatibility.DotLiquid22, "en-US"), "ab", "a", null));
    }

    [Fact]
    public void Plus_And_Times_MatchStock()
    {
        var context = NewContext(SyntaxCompatibility.DotLiquid20, "en-US");
        object?[] values = { null, "", "ab", 3, 2.5m, "3", 1L, 0, -2, 1.5d };
        WithCulture("en-US", () =>
        {
            foreach (var left in values)
            {
                foreach (var right in values)
                {
                    Assert.Equal(Run(() => Stock.Plus(context, left!, right!)), Run(() => Guarded.Plus(context, left, right)));
                    Assert.Equal(Run(() => Stock.Times(context, left!, right!)), Run(() => Guarded.Times(context, left, right)));
                }
            }
        });
    }

    [Fact]
    public void Join_DoesNotRecurse_IntoNestedLists()
    {
        // 100 000 levels of nesting: a recursive flatten would overflow the stack; the stock filter prints each nested list's ToString
        object?[] list = { "a" };
        for (var i = 0; i < 100_000; i++)
        {
            list = new object?[] { list };
        }

        using var scope = RenderScope.Begin(new TemplatingOptions(), null);
        Assert.Equal(Run(() => Stock.Join(list, ",")), Run(() => Guarded.Join(list, ",")));
    }

    [Fact]
    public void Liquid_NilInput_OfAppendAndPrepend_GivesTheOperand()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions());
        Assert.Equal("Dear ", engine.RenderString("{{ title | Prepend: 'Dear ' }}", new { }));
        Assert.Equal("x", engine.RenderString("{{ title | Append: 'x' }}", new { }));
    }
}
