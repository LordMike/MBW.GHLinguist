using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace MBW.GHLinguist.Tests;

public sealed class PackageIntegrationTests
{
    [Fact]
    public void NativeAssetRootIsTheManagedAssemblyDirectory()
    {
        string assemblyLocation = typeof(LinguistRuntime).Assembly.Location;

        Assert.Equal(Path.GetDirectoryName(assemblyLocation), NativeLinguistRuntimeBackend.GetNativeAssetRoot());
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
    [InlineData("MBW.GHLinguist.Runtime.win-x64.targets", "win-x64", "CopyMBWGHLinguistWinX64NativeClosure")]
    [InlineData("MBW.GHLinguist.Runtime.linux-x64.targets", "linux-x64", "CopyMBWGHLinguistLinuxX64NativeClosure")]
    public void RuntimePackageBuildTransitiveTargetCopiesTheCompleteNativeClosureWithRelativeLayout(
        string targetFile,
        string runtimeIdentifier,
        string targetNamePrefix)
    {
        XDocument target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, targetFile));
        XElement outputCopy = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == targetNamePrefix + "ToOutput");
        XElement publishCopy = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == targetNamePrefix + "ToPublish");

        Assert.Equal("CopyFilesToOutputDirectory", (string?)outputCopy.Attribute("AfterTargets"));
        Assert.Equal("CopyFilesToPublishDirectory", (string?)publishCopy.Attribute("AfterTargets"));
        Assert.Contains("nativeassets", target.ToString(), StringComparison.Ordinal);
        Assert.Contains("NETCoreSdkRuntimeIdentifier", target.ToString(), StringComparison.Ordinal);
        Assert.Contains(runtimeIdentifier, target.ToString(), StringComparison.Ordinal);
        Assert.Contains("/**/*", outputCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("[System.IO.Directory]::Exists", outputCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("[System.IO.Directory]::Exists", publishCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("$(OutDir)%(RecursiveDir)%(Filename)%(Extension)", outputCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("$(PublishDir)%(RecursiveDir)%(Filename)%(Extension)", publishCopy.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedPackageBuildTransitiveTargetRequiresOneRuntimePackage()
    {
        XDocument target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist.targets"));
        XElement validation = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "RequireMBWGHLinguistRuntimePackage");

        Assert.Equal("CopyFilesToOutputDirectory;CopyFilesToPublishDirectory", (string?)validation.Attribute("BeforeTargets"));
        Assert.Contains("MBWGHLinguistRuntimePackageInstalled", validation.ToString(), StringComparison.Ordinal);
        Assert.Contains("Runtime.win-x64", validation.ToString(), StringComparison.Ordinal);
        Assert.Contains("Runtime.linux-x64", validation.ToString(), StringComparison.Ordinal);
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
}
