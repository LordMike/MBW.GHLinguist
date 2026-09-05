using MBW.GHLinguist;

namespace MBW.GHLinguist.PackageTopology.Library;

public static class LinguistConsumer
{
    public static LinguistRuntime CreateRuntime() => LinguistRuntime.Create();
}
