using Xunit;

namespace Foreman.Mac.UiTests.support {
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ScaleTestGroup {
        internal const string Name = "WindowScaler tests";
    }
}
