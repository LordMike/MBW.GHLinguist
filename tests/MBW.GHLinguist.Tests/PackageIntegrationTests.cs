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
    public void BuildTransitiveTargetCopiesTheCompleteNativeClosureWithRelativeLayout()
    {
        XDocument target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist.targets"));
        XElement outputCopy = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "CopyMBWGHLinguistNativeClosureToOutput");
        XElement publishCopy = target.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "CopyMBWGHLinguistNativeClosureToPublish");

        Assert.Equal("CopyFilesToOutputDirectory", (string?)outputCopy.Attribute("AfterTargets"));
        Assert.Equal("CopyFilesToPublishDirectory", (string?)publishCopy.Attribute("AfterTargets"));
        Assert.Contains("nativeassets", target.ToString(), StringComparison.Ordinal);
        Assert.Contains("NETCoreSdkRuntimeIdentifier", target.ToString(), StringComparison.Ordinal);
        Assert.Contains("/**/*", outputCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("$(OutDir)%(RecursiveDir)%(Filename)%(Extension)", outputCopy.ToString(), StringComparison.Ordinal);
        Assert.Contains("$(PublishDir)%(RecursiveDir)%(Filename)%(Extension)", publishCopy.ToString(), StringComparison.Ordinal);
    }
}
