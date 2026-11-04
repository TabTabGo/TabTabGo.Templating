using DotLiquid;
using TabTabGo.Templating.Liquid;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

public class HashEqualityComparerTests
{
    private readonly HashEqualityComparer _comparer = new();

    [Fact]
    public void Equals_SameKeysAndSameValues_True()
    {
        var shared = "v";
        Assert.True(_comparer.Equals(new Hash { ["a"] = shared }, new Hash { ["a"] = shared }));
    }

    [Fact]
    public void Equals_DifferentValue_False()
    {
        Assert.False(_comparer.Equals(new Hash { ["a"] = "1" }, new Hash { ["a"] = "2" }));
    }

    [Fact]
    public void Equals_KeyMissingOnEitherSide_False()
    {
        Assert.False(_comparer.Equals(new Hash { ["a"] = "1" }, new Hash { ["b"] = "1" }));
        Assert.False(_comparer.Equals(new Hash { ["a"] = "1" }, new Hash { ["a"] = "1", ["b"] = "2" }));
    }

    [Fact]
    public void GetHashCode_DelegatesToObject()
    {
        var hash = new Hash();
        Assert.Equal(hash.GetHashCode(), _comparer.GetHashCode(hash));
    }
}
