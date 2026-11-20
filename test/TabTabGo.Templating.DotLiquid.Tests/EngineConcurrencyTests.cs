using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>B-3: one engine instance is safe as a singleton and under parallel renders.</summary>
public sealed class EngineConcurrencyTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Equal by key only, like many entity types: the old hash cache treated two such objects as one.</summary>
    private sealed class Keyed
    {
        public string Key { get; set; } = "";

        public string Value { get; set; } = "";

        public override bool Equals(object? obj) => obj is Keyed other && other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }

    [Fact]
    public void Hash_KeyEqualObjects_RenderOwnValues()
    {
        var path = _dir.Write("t.liquid", "{{ a.value }}-{{ b.value }}");
        var data = new { a = new Keyed { Key = "k", Value = "one" }, b = new Keyed { Key = "k", Value = "two" } };

        var result = new Liquid.TemplatingEngine().Render(path, data);

        Assert.Equal("one-two", result);
    }

    [Fact]
    public async Task Parallel_100Renders_Isolated()
    {
        var path = _dir.Write("t.liquid", "{% for i in items %}{{ id }}:{{ i.name }};{% endfor %}");
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();

        var tasks = Enumerable.Range(0, 100).Select(id => Task.Run(() =>
        {
            var data = new { id, items = Enumerable.Range(0, 20).Select(n => new { name = "n" + n }).ToList() };
            return (id, text: (string)engine.Render(path, data));
        })).ToList();

        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        foreach (var (id, text) in results)
        {
            var expected = string.Concat(Enumerable.Range(0, 20).Select(n => $"{id}:n{n};"));
            Assert.Equal(expected, text);
        }
    }

    [Fact]
    public async Task Parallel_SharedEngine_NoCorruption()
    {
        var path = _dir.Write("t.liquid", "{{ model.a.b }}");
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();

        var failures = 0;
        var tasks = Enumerable.Range(0, 400).Select(n => Task.Run(() =>
        {
            try
            {
                var shared = new { b = n };
                var text = (string)engine.Render(path, new { model = new { a = shared } });
                if (text != n.ToString())
                {
                    Interlocked.Increment(ref failures);
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref failures);
            }
        })).ToList();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(0, failures);
    }
}
