using System;
using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    
    public class JsonInputUnitTests
    {
        [Fact]
        public void HashSimpleJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/simple.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/simple.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("hasString:a string,hasBool:true", renderedData);
        }

        [Fact]
        public void HashArrayJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/array.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/array.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("item1:item1,value1:value1,item2:item2,value2:value2", renderedData);
        }

        [Fact]
        public void HashNestedJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/nested.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/nested.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("hasString:a string,hasBool:true,IsNested:true,Name:test", renderedData);
        }

        [Fact]
        public void HashNestedWithArrayJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/nestedWithArray.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/nestedWithArray.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("hasString:a string,hasBool:true,IsNested:true,Name:test,Item1:item1,Value1:value1,Item2:item2,Value2:value2", renderedData);
        }

        [Fact]
        public void HashNestedArraysJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/nestedArrays.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/nestedArrays.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("item1:item1,value1:value1,values(nestedValue1,nestedValue2),item2:item2,value2:value2", renderedData);
        }

        [Fact]
        public void HashStringArrayJson()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/stringArray.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/stringArray.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });

            Assert.Equal("item1,item2,item3,item4", renderedData);
        }
    }
}
