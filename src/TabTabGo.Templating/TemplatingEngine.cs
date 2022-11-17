using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Templating
{
    public abstract class TemplatingEngine
    {
        public abstract string EngineName { get; }
        public abstract bool IsCached(string templatePath);
        public abstract void ParseTemplate(string templatePath);
        public abstract object Render(string templatePath, object sourceData, string culture = "en-Us");
        public abstract void RegisterFilter(Type type);
    }

}
