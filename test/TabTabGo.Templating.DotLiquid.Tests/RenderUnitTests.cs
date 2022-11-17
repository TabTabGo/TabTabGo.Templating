using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    //TODO: Refactor to xUnit
    //[TestClass]
    //public class RenderUnitTests
    //{
    //    [TestMethod]
    //    public void RenderBaseLiquid()
    //    {
    //        var dotLiquidEngine = new Liquid.TemplatingEngine();
    //        var jsonStr = File.ReadAllText("./json/simple.json");
    //        var templatePath = "./liquids/base.liquid";
    //        dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

    //        var renderedData = (string)dotLiquidEngine.Render(templatePath, new { data = data });
            
    //        Assert.IsTrue(!string.IsNullOrEmpty(renderedData));
    //    }

    //    [TestMethod]
    //    public void RenderRelativeBaseLiquid()
    //    {
    //        var dotLiquidEngine = new Liquid.TemplatingEngine();
    //        var jsonStr = File.ReadAllText("./json/simple.json");
    //        var templatePath = "./liquids/somefolder/relativeBase.liquid";
    //        dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

    //        var renderedData = (string)dotLiquidEngine.Render(templatePath, new { data = data });

    //        Assert.IsTrue(!string.IsNullOrEmpty(renderedData));
    //    }
    //}
}
