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

            return File.ReadAllText(fullPath);
        }

        public string FullPath(string templatePath)
        {
            if (templatePath == null || !Regex.IsMatch(templatePath, @"^[.\/]*[a-zA-Z0-9_\/]+$") || Path.IsPathRooted(templatePath))
                throw new FileSystemException("Illegal template name", templatePath);

            string fullPath = templatePath.Contains("/")
                ? Path.Combine(Path.Combine(Root, Path.GetDirectoryName(templatePath)), string.Format("_{0}.liquid", Path.GetFileName(templatePath)))
                : Path.Combine(Root, string.Format("_{0}.liquid", templatePath));

            // Compare normalised paths with a trailing separator on the root: a plain prefix test accepts "/rootEvil" for
            // "/root", and an unescaped root is a regular expression (S-3).
            var normalizedRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(fullPath);
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!normalizedPath.StartsWith(normalizedRoot, comparison))
                throw new FileSystemException("Illegal template path", normalizedPath);

            return fullPath;
        }
    }
}
