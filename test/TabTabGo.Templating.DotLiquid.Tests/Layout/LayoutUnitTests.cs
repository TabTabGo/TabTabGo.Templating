using System;
using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class LayoutUnitTests
    {
        [Fact]
        public void HashJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
           
            var templatePath = AppContext.BaseDirectory + "/../../../Layout/child.liquid";
            dynamic data = new { Child = "Test"};

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

           // Assert.Equal("hasString:a string,hasBool:true", renderedData);
        }

        
    }
}
