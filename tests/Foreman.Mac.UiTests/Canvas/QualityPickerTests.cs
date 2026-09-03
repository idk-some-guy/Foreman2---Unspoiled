using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas.Panels;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Rider 8 (final fix wave): SelectedQuality indexed straight into `qualities[selector.SelectedIndex]`
    //with no bounds check, throwing whenever the combo's SelectedIndex reverts to -1 (e.g. a transient
    //reset while ItemsSource is being reassigned) even though `qualities` itself is already populated.
    public class QualityPickerTests {
        [AvaloniaFact]
        public void SelectedQuality_SelectedIndexResetToNegativeOne_FallsBackToFirstQualityInsteadOfThrowing() {
            var picker = new QualityPicker();
            var normal = new QualityPrototype(new Foreman.DataCaching.DataCache(filterRecipes: true), "normal", "Normal", "a");
            var epic = new QualityPrototype(new Foreman.DataCaching.DataCache(filterRecipes: true), "epic", "Epic", "b");
            picker.SetQualities([normal, epic]);

            picker.Selector.SelectedIndex = -1;

            Assert.Equal(normal, picker.SelectedQuality);
        }
    }
}
