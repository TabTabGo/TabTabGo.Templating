
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
    
    [Collection("liquid-static")]
    public class ValueFilterUnitTests
    {
        [Fact]
        public void LocalizedDateFormatFilterTest()
        {
            // "Now" renders in UTC (decision F-10), so the clock is pinned to an instant whose UTC date differs from a UTC+4 local date, and the expectation is the UTC date
            var instant = new DateTimeOffset(2026, 11, 21, 22, 30, 0, TimeSpan.Zero);
            var previous = Liquid.Filters.ValueFilters.Clock;
            Liquid.Filters.ValueFilters.Clock = new FixedClock(instant);
            try
            {
                var dotLiquidEngine = new Liquid.TemplatingEngine();
                var jsonStr = File.ReadAllText(AppContext.BaseDirectory+"/../../../json/dateLocale.json");
                var templatePath = AppContext.BaseDirectory + "/../../../liquids/dateLocale.liquid";
                dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

                var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
                var dateString = instant.UtcDateTime.ToString("dd MMMM yyyy", new CultureInfo("ar-AE")) + "," + instant.UtcDateTime.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture);
                Assert.Equal(dateString, renderedData);
            }
            finally
            {
                Liquid.Filters.ValueFilters.Clock = previous;
            }
        }

        private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => instant;
        }

        [Fact]
        public void ToTimeZoneFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/dates.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/timezone.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            // F-9: the zone is found by id (the Windows id "Arabian Standard Time" is UTC+4); a parsed date without a zone is UTC (F-10)
            var arabianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time");
            var dateString = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(DateTime.Parse((string)data.dateValue2), DateTimeKind.Utc), arabianTimeZone).ToString("yyyy-MM-dd HH:mm:ss");
            Assert.Equal(dateString, renderedData);
        }

        
        [Fact]
        public void OffsetDateFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/dates.json");
            var templatePath = "./liquids/offsetDate.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            // get AUS timezone
            var timeZones= TimeZoneInfo.GetSystemTimeZones();
            var australianTimezone = timeZones.FirstOrDefault(a => a.StandardName == "Australian Eastern Standard Time");
            var dateString = TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse((string)data.dateValue3).ToUniversalTime(), australianTimezone!).ToString("yyyy-MM-dd HH:mm:ss");
            Assert.Equal(dateString.Substring(0,8), renderedData.ToString()!.Substring(0,8));
        }


        [Fact]
        public void ParseDateWithoutFormatFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/dates.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/parseDates.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            // the template prepends "content" to the file name with no separator (Prepend semantics), see spec 11.4
            // S-6: relative to the template root unless AllowAbsolutePaths is set
            Assert.Equal("someFile.png,contentsomeFile.png", renderedData);
        }

        [Fact]
        public void NestedFullPathFilterTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/fullPath.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/nestedFullPath.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            Assert.Equal("content" + Path.DirectorySeparatorChar, renderedData);
        }

        [Fact]
        public void ReverseSliceSubstringTest()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText(AppContext.BaseDirectory + "/../../../json/simple.json");
            var templatePath = AppContext.BaseDirectory + "/../../../liquids/reverseSlice.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            // The engine formats with the thread culture today (bug B-6, fixed on v10), so pin it for the test.
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            try
            {
                var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
                var expectedResult = "number:$12,345.00";
                Assert.Equal(expectedResult, renderedData);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void FormatCurrencyAeCulture()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/number.json");
            var templatePath = "./liquids/number-ae.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

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
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "result:123456";
            Assert.Equal(expectedResult, renderedData);
        }
        [Fact]
        public void NumberToWords()
        {
            var dotLiquidEngine = new Liquid.TemplatingEngine();
            var jsonStr = File.ReadAllText("./json/numberToWords.json");
            var templatePath = "./liquids/numberToWords.liquid";
            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr)!;

            var renderedData = dotLiquidEngine.Render(templatePath, new { data = data });
            var expectedResult = "number:One Hundred and Twenty-Three points Forty-Five";
            Assert.Equal(expectedResult, renderedData);
        }
    }
}
