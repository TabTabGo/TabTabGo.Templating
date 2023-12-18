using System;
using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class LayoutUnitTests
    {
        [Fact]
        public void Layout_Use_Extends()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();

            var templatePath = AppContext.BaseDirectory + "/../../../Layout/child.liquid";
            dynamic data = new { Child = "Test" };

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = @"<!DOCTYPE html>
<html>
<head>
    
</head>
<body>
<table>
    <tr><td><h1>Layout</h1></td></tr>
    <tr>
        <td colspan=""2"" style=""padding-bottom:10px;"">
            
   <p>Test</p>
   <img src=""https://en.m.wikipedia.org/wiki/File:Flat_tick_icon.svg"" />

        </td>
    </tr>
  
</table>
</body>
</html>
";
            Assert.Equal(expectedResult, renderedData);
        }


    }
}