using System;

using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class CachingUnitTests
    {
        [Fact]
        public void RenderTemplateTwice()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory+"/../../../json/simple.json");
            var templatePath = AppContext.BaseDirectory+ "/../../../liquids/simple.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var isCached = dotLiquidEngine.IsCached(templatePath);
            Assert.True(isCached);

            var renderedData2 = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal(renderedData2, renderedData);
        }
    }
}
