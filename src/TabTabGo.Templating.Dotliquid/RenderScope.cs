using System;
using System.Threading;
using DotLiquid.FileSystems;

namespace TabTabGo.Templating.Liquid
{
    /// <summary>
    /// Per-render ambient state (options and the template file system). DotLiquid filters are static methods that cannot
    /// receive the engine instance, so the engine opens a scope around each render and the filters read
    /// <see cref="Current"/>. The scope flows with the logical call context, so concurrent renders do not see each other's scope.
    /// </summary>
    internal sealed class RenderScope : IDisposable
    {
        private static readonly AsyncLocal<RenderScope?> CurrentScope = new AsyncLocal<RenderScope?>();
        private static readonly TemplatingOptions DefaultOptions = new TemplatingOptions();

        private readonly RenderScope? _previous;

        private RenderScope(TemplatingOptions options, IFileSystem? fileSystem, RenderScope? previous)
        {
            Options = options;
            FileSystem = fileSystem;
            _previous = previous;
        }

        /// <summary>The scope of the render running on the current logical call context, or null outside a render.</summary>
        public static RenderScope? Current => CurrentScope.Value;

        /// <summary>The options of the running render, or defaults outside a render.</summary>
        public static TemplatingOptions CurrentOptions => CurrentScope.Value?.Options ?? DefaultOptions;

        /// <summary>The options of this render.</summary>
        public TemplatingOptions Options { get; }

        /// <summary>The file system of the template being rendered (its includes resolve against it).</summary>
        public IFileSystem? FileSystem { get; }

        /// <summary>Opens a scope; dispose it when the render ends.</summary>
        public static RenderScope Begin(TemplatingOptions options, IFileSystem? fileSystem)
        {
            var scope = new RenderScope(options, fileSystem, CurrentScope.Value);
            CurrentScope.Value = scope;
            return scope;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            CurrentScope.Value = _previous;
        }
    }
}
