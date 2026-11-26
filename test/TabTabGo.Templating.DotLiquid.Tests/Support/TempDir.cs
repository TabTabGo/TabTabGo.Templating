using System;
using System.IO;
using System.Linq;

namespace TabTabGo.Templating.TestKit;

/// <summary>A unique directory under the system temp folder, deleted on dispose. Tests never write anywhere else.</summary>
public sealed class TempDir : IDisposable
{
    /// <summary>Creates the directory.</summary>
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ttg-tpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Full path of the directory.</summary>
    public string Path { get; }

    /// <summary>Combines <paramref name="parts"/> under the directory.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    /// <summary>Writes a UTF-8 text file (creating folders) and returns its full path.</summary>
    public string Write(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leaked temp folder must not fail a test
        }
    }
}
