using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests
{
    public class CollectionFilterUnitTests
    {
        [Fact]
        public void GroupByTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/arrayToGroup.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/groupBy.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var dateString = "\r\nGroup: A Item: item1Group: B Item: item2Group: B Item: item3";
            Assert.Equal(dateString, renderedData);
        }
    }
}
