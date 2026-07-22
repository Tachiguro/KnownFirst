using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests
{
    [TestClass]
    public class AndroidIconConfigurationTests
    {
        private readonly XDocument _csproj;
        private readonly string _projectDir;

        public AndroidIconConfigurationTests()
        {
            var testAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            _projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testAssemblyPath), "../../../../"));
            var csprojPath = Path.Combine(_projectDir, "KnownFirst.csproj");
            _csproj = XDocument.Load(csprojPath);
        }

        [TestMethod]
        public void Project_ShouldNotUseDefaultMauiIcon()
        {
            var hasDefaultIcon = _csproj.Descendants("MauiIcon")
                .Any(e => (string)e.Attribute("Include") == @"Resources\AppIcon\appicon.svg" ||
                          (string)e.Attribute("Include") == @"Resources\AppIcon\appiconfg.svg" ||
                          (string)e.Attribute("Include") == @"KnownFirst_Picture.png" ||
                          ((string)e.Attribute("Include")).Contains("dotnet_bot"));
            Assert.IsFalse(hasDefaultIcon, "Default MAUI icons or invalid names are still referenced.");
        }

        [TestMethod]
        public void Project_ShouldUseAppIcon()
        {
            var icon = _csproj.Descendants("MauiIcon")
                .FirstOrDefault();
            
            Assert.IsNotNull(icon, "MauiIcon element is missing.");
            Assert.AreEqual(@"Resources\AppIcon\appicon.png", (string)icon.Attribute("Include"));
        }

        [TestMethod]
        public void ApplicationIds_AreCorrectForConfigurations()
        {
            var defaultId = _csproj.Descendants("ApplicationId").First(e => e.Ancestors("PropertyGroup").FirstOrDefault()?.Attribute("Condition") == null).Value;
            Assert.AreEqual("com.tachiguro.knownfirst", defaultId);

            var debugId = _csproj.Descendants("ApplicationId")
                .FirstOrDefault(e => e.Ancestors("PropertyGroup").FirstOrDefault()?.Attribute("Condition")?.Value.Contains("Debug") == true)?.Value;
            Assert.AreEqual("com.tachiguro.knownfirst.debug", debugId);

            var diaId = _csproj.Descendants("ApplicationId")
                .FirstOrDefault(e => e.Ancestors("PropertyGroup").FirstOrDefault()?.Attribute("Condition")?.Value.Contains("BetaDiagnostic") == true)?.Value;
            Assert.AreEqual("com.tachiguro.knownfirst.diagnostic", diaId);
        }
    }
}
