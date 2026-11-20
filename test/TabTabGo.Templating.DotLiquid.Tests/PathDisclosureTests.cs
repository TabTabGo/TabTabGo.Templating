using DotLiquid.Exceptions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>S-6 and S-12 (no server paths in output or messages) and S-11 (symlinks cannot leave the include root).</summary>
public sealed class PathDisclosureTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void FullPath_Default_Relative()
    {
        var path = _dir.Write("t.liquid", "{{ 'images/logo.png' | FullPath }}|{{ 'logo.png' | FullPath }}");

        var result = (string)new Liquid.TemplatingEngine().Render(path, new { });

        Assert.Equal($"images{Path.DirectorySeparatorChar}logo.png|logo.png", result);
        Assert.DoesNotContain(_dir.Path, result);
    }

    [Fact]
    public void FullPath_Default_TraversalStaysRelative_NeverRevealsTheRoot()
    {
        var path = _dir.Write("t.liquid", "{{ '../outside.png' | FullPath }}");

        var result = (string)new Liquid.TemplatingEngine().Render(path, new { });

        Assert.False(Path.IsPathRooted(result));
        Assert.DoesNotContain(_dir.Path, result);
    }

    [Fact]
    public void FullPath_EmptyInput_RelativeRoot()
    {
        var path = _dir.Write("t.liquid", "[{{ '' | FullPath }}]");

        var result = (string)new Liquid.TemplatingEngine().Render(path, new { });

        Assert.DoesNotContain(_dir.Path, result);
    }

    [Fact]
    public void FullPath_AllowAbsolute_Absolute()
    {
        var path = _dir.Write("t.liquid", "{{ 'images/logo.png' | FullPath }}");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { AllowAbsolutePaths = true });

        var result = (string)engine.Render(path, new { });

        Assert.Equal(Path.GetFullPath(Path.Combine(_dir.Path, "images", "logo.png")), result);
    }

    [Fact]
    public void Include_Error_DoesNotContainAbsolutePath()
    {
        var path = _dir.Write("t.liquid", "{% include 'missing' %}");

        var result = (string)new Liquid.TemplatingEngine().Render(path, new { });

        Assert.Contains("Template not found", result);
        Assert.DoesNotContain(_dir.Path, result);
    }

    [Fact]
    public void Include_TraversalError_DoesNotContainAbsolutePath()
    {
        var fileSystem = new Liquid.EnhancedLocalFileSystem(_dir.Path);

        var error = Assert.Throws<FileSystemException>(() => fileSystem.FullPath("../../etc/hosts"));

        Assert.DoesNotContain(_dir.Path, error.Message);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(_dir.Path, "..")), error.Message);
    }

    [Fact]
    public void Include_SymlinkToOutsideRoot_Throws()
    {
        using var outside = new TempDir();
        outside.Write("_hosts.liquid", "SECRET");
        var root = Path.Combine(_dir.Path, "root");
        Directory.CreateDirectory(root);
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "_hosts.liquid"), Path.Combine(outside.Path, "_hosts.liquid"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // symlinks are not available (Windows without developer mode): nothing to check here
        }

        var template = Path.Combine(root, "main.liquid");
        File.WriteAllText(template, "{% include 'hosts' %}");

        var result = (string)new Liquid.TemplatingEngine().Render(template, new { });

        Assert.DoesNotContain("SECRET", result);
        Assert.Contains("Illegal template path", result);
    }

    [Fact]
    public void Include_SymlinkInsideRoot_Works()
    {
        var root = Path.Combine(_dir.Path, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "_real.liquid"), "REAL");
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "_alias.liquid"), Path.Combine(root, "_real.liquid"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var template = Path.Combine(root, "main.liquid");
        File.WriteAllText(template, "{% include 'alias' %}");

        Assert.Equal("REAL", new Liquid.TemplatingEngine().Render(template, new { }));
    }
}
