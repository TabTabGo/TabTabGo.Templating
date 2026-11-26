using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.Liquid;

/// <summary>Compares hashes by content: the same keys with equal values (numbers by value, nested hashes and lists by content).</summary>
public class HashEqualityComparer : IEqualityComparer<Hash>
{
    /// <inheritdoc />
    public bool Equals(Hash? x, Hash? y) => ValueSemantics.Equals(x, y);

    /// <inheritdoc />
    public int GetHashCode(Hash obj) => ValueSemantics.GetHashCode(obj);
}
