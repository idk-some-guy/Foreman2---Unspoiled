using Xunit;

namespace Foreman.Mac.Kit.Tests.support {
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class KitScaleGroup {
        internal const string Name = "Kit scale tests";
    }
}
