using DotLiquid;
using DotLiquid.Exceptions;
using DotLiquid.FileSystems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TabTabGo.Templating.Liquid
{
    public class EnhancedLocalFileSystem : IFileSystem
    {
        public string Root { get; set; }

        public EnhancedLocalFileSystem(string root)
        {
            Root = root;
        }

        public string ReadTemplateFile(Context context, string templateName)
        {
            // The name may be a literal ('part') or a variable; a variable that is not a string must not be coerced (S-3).
            if (!(context[templateName] is string templatePath))
                throw new FileSystemException("Illegal template name", templateName);

            string fullPath = FullPath(templatePath);
            if (!File.Exists(fullPath))
                throw new FileSystemException("Template not found", templatePath);

            try
            {
                var text = GuardedRead.ReadText(fullPath);
                Internal.TemplateNesting.Check(text, RenderScope.CurrentOptions.MaxNestingDepth); // a partial is parsed by DotLiquid on the caller's stack: refuse it before that when it nests too deeply (S-13)
                return text;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // a dangling link or an unreadable file: the system message carries the absolute path, ours does not
                throw new FileSystemException("Template not found", templatePath);
            }
        }

        public string FullPath(string templatePath)
        {
            if (templatePath == null || !Regex.IsMatch(templatePath, @"^[.\/]*[a-zA-Z0-9_\/]+\z") || Path.IsPathRooted(templatePath))
                throw new FileSystemException("Illegal template name", templatePath);

            string fullPath = templatePath.Contains("/")
                ? Path.Combine(Path.Combine(Root ?? string.Empty, Path.GetDirectoryName(templatePath)), string.Format("_{0}.liquid", Path.GetFileName(templatePath)))
                : Path.Combine(Root ?? string.Empty, string.Format("_{0}.liquid", templatePath));

            // Compare normalised paths with a trailing separator on the root: a plain prefix test accepts "/rootEvil" for
            // "/root", and an unescaped root is a regular expression (S-3).
            // an empty root (a template given as a bare file name) means the current directory
            var normalizedRoot = Path.GetFullPath(string.IsNullOrEmpty(Root) ? "." : Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(fullPath);
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!normalizedPath.StartsWith(normalizedRoot, comparison))
                throw new FileSystemException("Illegal template path", templatePath); // the name, never the absolute path

            return fullPath;
        }
    }
}
