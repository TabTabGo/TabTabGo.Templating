using System.Collections;
using System.Reflection;
using DotLiquid;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Converts the data handed to a render into DotLiquid variables (<see cref="Hash"/> and lists). One instance serves one call: the
/// visited map is private to it, so renders on a shared engine never see each other's objects (B-3). Objects are matched by
/// reference, never by <c>Equals</c>, and conversion stops <c>maxDepth</c> levels below the root (ADR 0003 section 8).
/// </summary>
internal sealed class ObjectHasher
{
    private readonly Dictionary<object, object> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly int _maxDepth;

    public ObjectHasher(int maxDepth)
    {
        _maxDepth = maxDepth;
    }

    /// <summary>
    /// Builds the variables of a render. An object (POCO, <see cref="JObject"/>) exposes its members directly; null gives no
    /// variables; any other root (a list, an array, a scalar) is exposed as <c>model</c> (B-5).
    /// </summary>
    public Hash HashRoot(object? root)
    {
        switch (root)
        {
            case null:
                return new Hash();
            case Hash hash:
                return hash;
            case JObject jObject:
                return HashJObject(jObject, 0) ?? new Hash();
            case JToken token:
                return Wrap(HashJToken(token, 0));
        }

        var type = root.GetType();
        if (root is string || IsEnumerableType(type) || type.IsPrimitive || type.IsEnum || root is decimal or DateTime or DateTimeOffset or Guid or TimeSpan)
        {
            return Wrap(HashValue(root, null, null, 0));
        }

        return (Hash)HashValue(root, null, null, 0, isRoot: true)!;
    }

    private static Hash Wrap(object? model) => new() { ["model"] = model };

    private object? HashValue(object? obj, Hash? parentHash, string? propertyName, int depth, bool isRoot = false)
    {
        if (obj == null)
        {
            return null;
        }

        if (depth > _maxDepth)
        {
            return null;
        }

        // an object-typed member holding a string, number, date ... is a value, not a list of characters or a property bag
        if (!isRoot && obj is not JToken && IsHashable(obj.GetType()))
        {
            return obj;
        }

        if (_visited.TryGetValue(obj, out var seen))
        {
            return seen;
        }

        if (obj is JObject jObject)
        {
            AddJsonSibling(parentHash, propertyName, obj);
            return HashJObject(jObject, depth);
        }

        if (obj is JArray jArray)
        {
            AddJsonSibling(parentHash, propertyName, obj);
            return HashJArray(jArray, depth);
        }

        if (obj is JToken token)
        {
            return HashJToken(token, depth);
        }

        if (IsEnumerableType(obj.GetType()))
        {
            var collection = new List<object?>();
            _visited[obj] = collection;
            foreach (var item in (IEnumerable)obj)
            {
                collection.Add(HashValue(item, null, null, depth + 1));
            }

            return collection;
        }

        var result = new Hash();
        _visited[obj] = result;

        foreach (var property in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // indexers and write-only members have no value to read; methods are never invoked (ADR 0003 section 8)
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(obj, null);
            var memberName = Template.NamingConvention.GetMemberName(property.Name);
            result[memberName] = IsHashable(property.PropertyType) ? value : HashValue(value, result, memberName, depth + 1);
        }

        return result;
    }

    private object? HashJToken(JToken token, int depth) => token switch
    {
        JObject o => HashJObject(o, depth),
        JArray a => HashJArray(a, depth),
        JValue v => v.Value,
        _ => token.ToString(),
    };

    private Hash? HashJObject(JObject obj, int depth)
    {
        if (depth > _maxDepth)
        {
            return null;
        }

        if (_visited.TryGetValue(obj, out var seen))
        {
            return (Hash)seen;
        }

        var result = new Hash();
        _visited[obj] = result;

        foreach (var property in obj.Properties())
        {
            result[property.Name] = property.Value == null ? null : HashJToken(property.Value, depth + 1);
        }

        return result;
    }

    private ICollection<object?>? HashJArray(JArray obj, int depth)
    {
        if (depth > _maxDepth)
        {
            return null;
        }

        if (_visited.TryGetValue(obj, out var seen))
        {
            return (ICollection<object?>)seen;
        }

        var collection = new List<object?>();
        _visited[obj] = collection;

        foreach (var item in obj)
        {
            collection.Add(HashJToken(item, depth + 1));
        }

        return collection;
    }

    private static void AddJsonSibling(Hash? parentHash, string? propertyName, object token)
    {
        if (!string.IsNullOrEmpty(propertyName) && parentHash != null)
        {
            parentHash[propertyName + "Json"] = token;
        }
    }

    private static bool IsHashable(Type type)
    {
        if (type == typeof(object))
        {
            return false;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return IsHashable(Nullable.GetUnderlyingType(type)!);
        }

        return type.IsPrimitive
            || type == typeof(string)
            || !type.GetProperties().Any()
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(JToken)
            || type == typeof(JObject)
            || type == typeof(decimal);
    }

    private static bool IsEnumerableType(Type type) => type.GetInterface("IEnumerable", true) != null;
}
