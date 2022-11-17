using System;
using System.Collections.Generic;
#if NET452
using System.Configuration;
#else
using Microsoft.Extensions.Configuration;
#endif

using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Templating
{
    public class Configuration
    {
        public static TemplatingConfigurationSection GetTemplatingConfiguration()
        {

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettigs.json")
                .Build();
            var templatingConfig = new TemplatingConfigurationSection();
            configBuilder.GetSection(TemplatingConfigurationSection.SectionName).Bind(templatingConfig);
            return templatingConfig;
        }
    }
}
