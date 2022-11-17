using System;
using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class InvoiceUnitTests
    {
        [Fact]
        public void HashInvoiceJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../invoice/json/invoice.json");
            var templatePath = AppContext.BaseDirectory + "/../../../invoice/liquids/invoice.liquid";
            dynamic data = JObject.Parse(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

           // Assert.Equal("hasString:a string,hasBool:true", renderedData);
        }

        
    }
}
