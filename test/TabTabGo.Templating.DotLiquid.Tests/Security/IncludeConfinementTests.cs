using System;
using System.IO;
using DotLiquid;
using DotLiquid.Exceptions;
using TabTabGo.Templating.Liquid;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>S-3: include/extends are confined to the template root (8.0.4).</summary>
[Collection("liquid-static")]
public sealed class IncludeConfinementTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-inc-" + Guid.NewGuid().ToString("N"));

    public IncludeConfinementTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string MakeRoot(string name = "root")
    {
        var root = Path.Combine(_base, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "_inside.liquid"), "INSIDE");
        File.WriteAllText(Path.Combine(_base, "_secret.liquid"), "TOPSECRET");
        return root;
    }

    [Fact]
    public void Include_ParentTraversal_Throws()
    {
        var fs = new EnhancedLocalFileSystem(MakeRoot());
        Assert.Throws<FileSystemException>(() => fs.FullPath("../secret"));
        Assert.Throws<FileSystemException>(() => fs.FullPath("../../secret"));
    }

    [Fact]
    public void Include_ParentTraversalThroughEngine_DoesNotLeakFile()
    {
        var root = MakeRoot();
        var templatePath = Path.Combine(root, "main.liquid");
        File.WriteAllText(templatePath, "{% include '../secret' %}");

        var result = (string)new Liquid.TemplatingEngine().Render(templatePath, new { });

        Assert.DoesNotContain("TOPSECRET", result);
        Assert.Contains("Illegal template path", result);
    }

    [Fact]
    public void Include_RootedName_Throws()
    {
        var fs = new EnhancedLocalFileSystem(MakeRoot());
        Assert.Throws<FileSystemException>(() => fs.FullPath("/etc/passwd"));
    }

    [Fact]
    public void Include_RootWithRegexChars_Works()
    {
        var root = MakeRoot("r+o[o]t.(1)");
        var fs = new EnhancedLocalFileSystem(root);

        Assert.Equal(Path.Combine(root, "_inside.liquid"), fs.FullPath("inside"));
        Assert.Equal("INSIDE", fs.ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'inside'"));
    }

    [Fact]
    public void Include_SiblingDirectorySharingRootPrefix_Throws()
    {
        var root = MakeRoot();
        var sibling = root + "evil"; // hyphen-free, so the name pattern accepts it and only the root check can reject it
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "_loot.liquid"), "LOOT");
        var fs = new EnhancedLocalFileSystem(root);

        Assert.Throws<FileSystemException>(() => fs.FullPath("../rootevil/loot"));
    }

    [Fact]
    public void Include_InsideRoot_Works()
    {
        var root = MakeRoot();
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "_deep.liquid"), "DEEP");
        var fs = new EnhancedLocalFileSystem(root);

        Assert.Equal("DEEP", fs.ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'sub/deep'"));
    }

    [Fact]
    public void Include_NameResolvedFromNonStringVariable_Throws()
    {
        var fs = new EnhancedLocalFileSystem(MakeRoot());
        var context = new Context(System.Globalization.CultureInfo.InvariantCulture);
        context["n"] = 5;

        Assert.Throws<FileSystemException>(() => fs.ReadTemplateFile(context, "n"));
    }
}
