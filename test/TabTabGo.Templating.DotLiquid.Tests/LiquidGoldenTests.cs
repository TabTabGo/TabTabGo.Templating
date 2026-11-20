using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>One golden case per filter shape (en and ar where culture matters); see <see cref="LiquidGolden"/>.</summary>
public class LiquidGoldenTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Golden", "Liquid");

    public static IEnumerable<object[]> Cases() => LiquidGolden.Cases(Root).Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_RendersTheGoldenOutput(string name) => LiquidGolden.Assert(Path.Combine(Root, name));
}
