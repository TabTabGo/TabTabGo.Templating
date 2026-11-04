using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>
/// Serialises every Liquid test: DotLiquid keeps process-wide statics (<c>Template.FileSystem</c>, filters,
/// naming convention) that parallel test classes would otherwise race on (bug B-1, fixed in 8.0.4).
/// </summary>
[CollectionDefinition("liquid-static", DisableParallelization = true)]
public class LiquidStaticCollection;
