using Newtonsoft.Json.Linq;
using System;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet
{
    public class ParseExcelTemplateTest
    {
        [Fact]
        public void ParseExcel()
        {
            var jobject = JObject.Parse(File.ReadAllText("SpreadSheet/json/test01.json"));
            var templatePath = "SpreadSheet/Template/test01.xlsx";
            var engine = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, jobject) as MemoryStream;
            Assert.NotNull(content);
            content.WriteTo(new FileStream("./SpreadSheet/Output/test01.xlsx", FileMode.CreateNew | FileMode.Truncate));
        }

        [Fact]
        public void ParseExcelReport()
        {
            var jobject = JObject.Parse(File.ReadAllText("SpreadSheet/json/report.json"));
            var templatePath = "SpreadSheet/Template/detailed-report.xlsx";
            var engine = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, new { reportResult = jobject }) as MemoryStream;
            Assert.NotNull(content);
            content.WriteTo(new FileStream("SpreadSheet/Output/report_output.xlsx", FileMode.CreateNew | FileMode.Truncate));
        }

        [Theory]
        [InlineData("SpreadSheet/json/report.json", "SpreadSheet/Template/detailed-report.xlsx", "SpreadSheet/Output/report_output.xlsx")]
        [InlineData("SpreadSheet/json/report.illegal.json", "SpreadSheet/Template/detailed-report.xlsx", "SpreadSheet/Output/report_illegal_output.xlsx")]
        public void GenerateExcelReport(string inputFile, string templatePath, string expectedOutputPath)
        {
            var jobject = JObject.Parse(File.ReadAllText(inputFile));
            
            var engine = new OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, new { reportResult = jobject }) as MemoryStream;

            Assert.NotNull(content);

            content.WriteTo(new FileStream(expectedOutputPath, FileMode.CreateNew | FileMode.Truncate));
        }
    }
}
