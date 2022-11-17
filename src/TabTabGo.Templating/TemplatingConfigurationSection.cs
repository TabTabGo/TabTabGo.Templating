using System;
using System.Collections.Generic;
#if NET452
using System.Configuration;
#endif
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Templating
{

    public class TemplatingConfigurationSection
    {
        public static string SectionName = "TabTabGo.Templating";
        public string TemplatesPath { get; set; }

    }
}
