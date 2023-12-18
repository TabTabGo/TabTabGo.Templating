
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class CustomFilterUnitTests
    {
        [Fact]
        public void LocalizedDateFormatFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var json = File.ReadAllText(AppContext.BaseDirectory+ "/../../../json/countryName.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/countryName.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var output = "United Arab Emirates";
            Assert.Equal(output, renderedData);
        }

      
    }
}
