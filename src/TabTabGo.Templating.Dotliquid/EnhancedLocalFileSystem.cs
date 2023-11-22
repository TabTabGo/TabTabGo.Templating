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
            string templatePath = (string)context[templateName];
            string fullPath = FullPath(templatePath);
            if (!File.Exists(fullPath))
                throw new FileSystemException("Template not found", templatePath);

            return File.ReadAllText(fullPath);
        }

        public string FullPath(string templatePath)
        {
            if (templatePath == null || !Regex.IsMatch(templatePath, @"^[.\/]*[a-zA-Z0-9_\/]+$"))
                throw new FileSystemException("Illegal template name", templatePath);

            string fullPath = templatePath.Contains("/")
                ? Path.Combine(Path.Combine(Root, Path.GetDirectoryName(templatePath)), string.Format("_{0}.liquid", Path.GetFileName(templatePath)))
                : Path.Combine(Root, string.Format("_{0}.liquid", templatePath));

            string escapedPath = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                escapedPath = Root.Replace(@"\", @"\\").Replace("(", @"\(").Replace(")", @"\)");
            else
                escapedPath = Root.Replace(@"\", @"/").Replace("(", @"\(").Replace(")", @"\)");

            var escapedPathRegex = string.Format("^{0}", escapedPath);
            if (!Regex.IsMatch(fullPath, escapedPathRegex))
                throw new FileSystemException("Illegal template path", Path.GetFullPath(fullPath));

            return fullPath;
        }
    }
}
