
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
    
    public class ValueFilterUnitTests
    {
        [Fact]
        public void LocalizedDateFormatFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory+"/../../../json/dateLocale.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/dateLocale.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var dateString = DateTime.Now.ToString("dd MMMM yyyy", new CultureInfo("ar-AE")) + "," + DateTime.Now.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture);
            Assert.Equal(dateString, renderedData);
        }

        [Fact]
        public void ToTimeZoneFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/dates.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/timezone.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var arabianTimeZone = TimeZoneInfo.GetSystemTimeZones().Where(a => a.StandardName == "Arabian Standard Time").FirstOrDefault();
            var dateString = TimeZoneInfo.ConvertTime(DateTime.Parse((string)data.dateValue2).ToUniversalTime(), arabianTimeZone).ToString("yyyy-MM-dd HH:mm:ss");
            Assert.Equal(dateString, renderedData);
        }

        
        [Fact]
        public void OffsetDateFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/dates.json");
            var templatePath = "./liquids/offsetDate.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            // get AUS timezone
            var timeZones= TimeZoneInfo.GetSystemTimeZones();
            var australianTimezone = timeZones.FirstOrDefault(a => a.StandardName == "Australian Eastern Standard Time");
            var dateString = TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse((string)data.dateValue3).ToUniversalTime(), australianTimezone).ToString("yyyy-MM-dd HH:mm:ss");
            Assert.Equal(dateString.Substring(0,8), renderedData.ToString().Substring(0,8));
        }


        [Fact]
        public void ParseDateWithoutFormatFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/dates.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/parseDates.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var dateString = "01 November 2013,2013-11-01";
            Assert.Equal(dateString, renderedData);
        }

        [Fact]
        public void FullPathFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/fullPath.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/fullPath.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var fullPath = Path.GetFullPath(AppContext.BaseDirectory + "/../../../liquids/someFile.png") 
                + "," + Path.GetFullPath(AppContext.BaseDirectory + "/../../../liquids/content/someFile.png");
            Assert.Equal(fullPath, renderedData);
        }

        [Fact]
        public void NestedFullPathFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/fullPath.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/nestedFullPath.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var fullPath = Path.GetFullPath(AppContext.BaseDirectory + "/../../../liquids/content/");
            Assert.Equal(fullPath, renderedData);
        }

        [Fact]
        public void ReverseSliceSubstringTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/simple.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/reverseSlice.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var output = "sliced:ring";
            Assert.Equal(output, renderedData);
        }

        [Fact]
        public void SimpleMarkNumbersTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/biDi.json");
            var templatePath = "./liquids/biDi.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "هاتف &lrm;+971 &lrm;4 &lrm;434 &lrm;0300";
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void InBetweenTextMarkNumbersTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/biDi-in-between.json");
            var templatePath = "./liquids/biDi.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "ص.ب: &lrm;33269، دبي، إ.ع.م.";
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void FormatCurrencyDefaultCulture()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/number.json");
            var templatePath = "./liquids/number.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "number:$12,345.00";
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void FormatCurrencyAeCulture()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/number.json");
            var templatePath = "./liquids/number-ae.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "number:12,345.00 AED";
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void ParseNumbers()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/numbers.json");
            var templatePath = "./liquids/numbers.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "number1:123, number2:0.1, number3:-0.12, number4:11.11";
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void DivideNumbers()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/division.json");
            var templatePath = "./liquids/division.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "result:"+100.00/10.5;
            Assert.Equal(expectedResult, renderedData);
        }

        [Fact]
        public void GetMatch()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/match.json");
            var templatePath = "./liquids/match.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "result:123456";
            Assert.Equal(expectedResult, renderedData);
        }
    }
}
