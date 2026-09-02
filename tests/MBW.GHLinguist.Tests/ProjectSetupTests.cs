using System.Reflection;

namespace MBW.GHLinguist.Tests;

public sealed class ProjectSetupTests
{
    [Fact]
    public void LibraryAssemblyCanBeLoaded()
    {
        Assembly assembly = Assembly.Load("MBW.GHLinguist");

        Assert.Equal("MBW.GHLinguist", assembly.GetName().Name);
    }
}
