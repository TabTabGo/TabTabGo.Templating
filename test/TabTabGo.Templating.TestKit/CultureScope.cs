using System.Globalization;

namespace TabTabGo.Templating.TestKit;

/// <summary>Sets the thread culture for a test and restores the previous one on dispose, so no test inherits the machine culture.</summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    /// <summary>Switches the current thread to <paramref name="name"/> (default <c>en-US</c>).</summary>
    public CultureScope(string name = "en-US")
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
