using Newtonsoft.Json.Linq;
using System;
using System.IO;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet
{
    public class ParseExcelTemplateTest : IDisposable
    {
        private readonly string _outputDirectory =
            Path.Combine(Path.GetTempPath(), "ttg-templating-tests", Guid.NewGuid().ToString("N"));

        public ParseExcelTemplateTest()
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, recursive: true);
            }
        }

        [Fact]
        public void ParseExcel()
        {
            var jobject = JObject.Parse(File.ReadAllText("SpreadSheet/json/test01.json"));
            var templatePath = "SpreadSheet/Template/test01.xlsx";
            var engine = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, jobject) as MemoryStream;
            Assert.NotNull(content);
            AssertWritesNonEmptyFile(content, "test01.xlsx");
        }

        [Fact]
        public void ParseExcelReport()
        {
            var jobject = JObject.Parse(File.ReadAllText("SpreadSheet/json/report.json"));
            var templatePath = "SpreadSheet/Template/detailed-report.xlsx";
            var engine = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, new { reportResult = jobject }) as MemoryStream;
            Assert.NotNull(content);
            AssertWritesNonEmptyFile(content, "report_output.xlsx");
        }

        [Theory]
        [InlineData("SpreadSheet/json/report.json", "SpreadSheet/Template/detailed-report.xlsx", "report_output.xlsx")]
        [InlineData("SpreadSheet/json/report.illegal.json", "SpreadSheet/Template/detailed-report.xlsx", "report_illegal_output.xlsx")]
        public void GenerateExcelReport(string inputFile, string templatePath, string outputFileName)
        {
            var jobject = JObject.Parse(File.ReadAllText(inputFile));

            var engine = new OpenXml.SpreadSheet.TemplatingEngine();
            var content = engine.Render(templatePath, new { reportResult = jobject }) as MemoryStream;

            Assert.NotNull(content);

            AssertWritesNonEmptyFile(content, outputFileName);
        }

        private void AssertWritesNonEmptyFile(MemoryStream content, string fileName)
        {
            var path = Path.Combine(_outputDirectory, fileName);
            using (var file = new FileStream(path, FileMode.Create))
            {
                content.WriteTo(file);
            }

            Assert.True(new FileInfo(path).Length > 0);
        }
    }
}
