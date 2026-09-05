using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace MBW.GHLinguist.Tests;

public sealed class PackageIntegrationTests
{
    [Fact]
    public void NativeAssetRootIsInTheManagedAssemblyDirectory()
    {
        string assemblyLocation = typeof(LinguistRuntime).Assembly.Location;

        Assert.Equal(Path.Combine(Path.GetDirectoryName(assemblyLocation)!, "MBW.GHLinguist"), NativeLinguistRuntimeBackend.GetNativeAssetRoot());
    }

    [Fact]
    public void NativeAssetRootRejectsSingleFileAssemblyLocations()
    {
        PlatformNotSupportedException exception = Assert.Throws<PlatformNotSupportedException>(() => NativeLinguistRuntimeBackend.GetNativeAssetRoot(string.Empty));

        Assert.Contains("Single-file deployment is not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetIntegrityAcceptsACompleteMatchingClosure()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("win-x64");

        NativeAssetIntegrity.Validate(closure.Root, "win-x64");
    }

    [Fact]
    public void NativeAssetIntegrityRejectsMissingProvenance()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("win-x64");
        File.Delete(Path.Combine(closure.Root, "provenance.json"));

        LinguistException exception = Assert.Throws<LinguistException>(() => NativeAssetIntegrity.Validate(closure.Root, "win-x64"));

        Assert.Contains("provenance.json is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetIntegrityRejectsTheWrongRuntimeIdentifier()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("linux-x64");

        LinguistException exception = Assert.Throws<LinguistException>(() => NativeAssetIntegrity.Validate(closure.Root, "win-x64"));

        Assert.Contains("current runtime identifier 'win-x64'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetIntegrityRejectsMissingAssets()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("win-x64");
        File.Delete(closure.AssetPath);

        LinguistException exception = Assert.Throws<LinguistException>(() => NativeAssetIntegrity.Validate(closure.Root, "win-x64"));

        Assert.Contains("is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetIntegrityRejectsHashMismatches()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("win-x64");
        File.AppendAllText(closure.AssetPath, "corrupt");

        LinguistException exception = Assert.Throws<LinguistException>(() => NativeAssetIntegrity.Validate(closure.Root, "win-x64"));

        Assert.Contains("does not match its recorded SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetIntegrityRejectsTraversalPaths()
    {
        using TestNativeClosure closure = TestNativeClosure.Create("win-x64", "../outside.dll");

        LinguistException exception = Assert.Throws<LinguistException>(() => NativeAssetIntegrity.Validate(closure.Root, "win-x64"));

        Assert.Contains("invalid asset path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MBW.GHLinguist.Runtime.win-x64.targets", "win-x64")]
    [InlineData("MBW.GHLinguist.Runtime.linux-x64.targets", "linux-x64")]
    public void RuntimePackageBuildTransitiveTargetPublishesTheNativeClosureAsContent(
        string targetFile,
        string runtimeIdentifier)
    {
        XDocument target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, targetFile));
        XElement itemGroup = target.Descendants("ItemGroup").Single();
        XElement content = itemGroup.Element("Content")!;
        XElement assetRoot = target.Descendants("PropertyGroup").Single().Elements().Single(element => element.Name.LocalName.EndsWith("AssetRoot", StringComparison.Ordinal));

        Assert.Contains(runtimeIdentifier, (string?)itemGroup.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("nativeassets", assetRoot.Value, StringComparison.Ordinal);
        Assert.Contains("/**/*", (string?)content.Attribute("Include"), StringComparison.Ordinal);
        Assert.Null(content.Attribute("Exclude"));
        Assert.Equal("MBW.GHLinguist/%(RecursiveDir)%(Filename)%(Extension)", (string?)content.Attribute("Link"));
        Assert.Equal("PreserveNewest", (string?)content.Attribute("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)content.Attribute("CopyToPublishDirectory"));
        Assert.DoesNotContain(target.Descendants("Copy"), _ => true);
    }

    [Fact]
    public void ManagedPackageBuildTransitiveTargetRequiresASupportedRuntimeIdentifierForExecutables()
    {
        XDocument target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist.targets"));
        XElement validation = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "ValidateMBWGHLinguistRuntimeIdentifier");
        XElement[] errors = validation.Elements("Error").ToArray();

        Assert.Equal("PrepareForBuild", (string?)validation.Attribute("BeforeTargets"));
        Assert.Equal("'$(OutputType)' == 'Exe' or '$(OutputType)' == 'WinExe'", (string?)validation.Attribute("Condition"));
        Assert.Equal(2, errors.Length);
        Assert.Contains("RuntimeIdentifier)' == ''", (string?)errors[0].Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("win-x64", validation.ToString(), StringComparison.Ordinal);
        Assert.Contains("linux-x64", validation.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Library", null, true, null)]
    [InlineData("Exe", null, false, "requires RuntimeIdentifier win-x64 or linux-x64")]
    [InlineData("Exe", "osx-x64", false, "supports only RuntimeIdentifier win-x64 or linux-x64; found osx-x64")]
    [InlineData("Exe", "win-x64", true, null)]
    [InlineData("WinExe", "win-x64", true, null)]
    public void ManagedPackageBuildTransitiveTargetValidatesOnlyExecutableProjects(
        string outputType,
        string? runtimeIdentifier,
        bool succeeds,
        string? expectedOutput)
    {
        using TemporaryProject project = TemporaryProject.Create(outputType, runtimeIdentifier);

        (int exitCode, string output) = project.Build();

        Assert.True(succeeds == (exitCode == 0), output);
        if (expectedOutput is not null)
        {
            Assert.Contains(expectedOutput, output, StringComparison.Ordinal);
        }
    }

    private sealed class TestNativeClosure : IDisposable
    {
        private TestNativeClosure(string root, string assetPath)
        {
            Root = root;
            AssetPath = assetPath;
        }

        internal string Root { get; }

        internal string AssetPath { get; }

        internal static TestNativeClosure Create(string runtimeIdentifier, string provenancePath = "lib/runtime.bin")
        {
            string root = Path.Combine(Path.GetTempPath(), $"MBW.GHLinguist.Tests-{Guid.NewGuid():N}");
            string assetPath = Path.Combine(root, "lib", "runtime.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllText(assetPath, "native asset");
            string sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath))).ToLowerInvariant();
            string provenance = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                platform = runtimeIdentifier,
                files = new[] { new { path = provenancePath, sha256 } },
            });
            File.WriteAllText(Path.Combine(root, "provenance.json"), provenance);
            return new TestNativeClosure(root, assetPath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string directory, string projectPath)
        {
            Directory = directory;
            ProjectPath = projectPath;
        }

        private string Directory { get; }

        private string ProjectPath { get; }

        internal static TemporaryProject Create(string outputType, string? runtimeIdentifier)
        {
            string directory = Path.Combine(Path.GetTempPath(), $"MBW.GHLinguist.TargetTests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string targetPath = Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist.targets").Replace("&", "&amp;", StringComparison.Ordinal);
            string runtimeIdentifierElement = runtimeIdentifier is null ? string.Empty : $"<RuntimeIdentifier>{runtimeIdentifier}</RuntimeIdentifier>";
            string projectPath = Path.Combine(directory, "Consumer.csproj");
            if (outputType is "Exe" or "WinExe")
            {
                File.WriteAllText(Path.Combine(directory, "Program.cs"), "System.Console.WriteLine(\"target test\");");
            }
            File.WriteAllText(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>{{outputType}}</OutputType>
                    {{runtimeIdentifierElement}}
                  </PropertyGroup>
                  <Import Project="{{targetPath}}" />
                </Project>
                """);
            return new TemporaryProject(directory, projectPath);
        }

        internal (int ExitCode, string Output) Build()
        {
            ProcessStartInfo startInfo = new("dotnet")
            {
                WorkingDirectory = Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(ProjectPath);
            startInfo.ArgumentList.Add("--nologo");
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
