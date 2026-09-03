using Foreman.DataCaching;
using Foreman.DataCaching.Loading;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ForemanTest {
    //Phase 7 deferred-minors sweep: ProcessAvailableStatuses indexed store.Assemblers["rocket-silo"]
    //directly (DataCachePostLoadProcessor.cs:226-227), the same KeyNotFoundException shape as
    //MainWindow.OpenSettingsAsync's own rocket-silo bug, but hit earlier - at data-load time, not
    //settings-open time - since a bare DataCache already carries a non-null RocketAssembler (the
    //synthetic pseudo-assembler DataCacheBootstrap.GenerateForemanHelperObjects always creates) before any
    //preset with an actual "rocket-silo" entity ever loads. DataCacheImportedPresetTests' own comment
    //("DataCachePostLoadProcessor's full consistency checks (rocket-silo lookups etc.) pass without a
    //hand-rolled preset needing to satisfy them") documents this as a known, worked-around landmine.
    [TestClass]
    public class DataCachePostLoadProcessorRocketSiloTests : ForemanTestBase {
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        [TestMethod]
        public void ProcessAvailableStatuses_NoRocketSiloAssembler_DoesNotThrow() {
            var cache = new DataCache(filterRecipes: false); //filterRecipes:true's barrel/crating pass hits an unrelated pre-existing "barrel" indexer gap on a bare (preset-less) store - out of this test's scope
            DataCacheStore store = Store(cache);
            Assert.IsNotNull(store.RocketAssembler); //bootstrap always creates this regardless of preset content
            Assert.IsFalse(store.Assemblers.ContainsKey("rocket-silo")); //no preset has been loaded yet

            new DataCachePostLoadProcessor(cache, store).ProcessAvailableStatuses();

            Assert.IsFalse(store.RocketAssembler!.Enabled);
            Assert.IsFalse(store.RocketAssembler!.Available);
        }
    }
}
