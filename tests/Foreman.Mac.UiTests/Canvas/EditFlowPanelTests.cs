using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaVisual = Avalonia.Visual;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises Task 4's real EditFlowPanel (docs/panels-reference.md §4/§9 step 4): the non-recipe node
    //editor - rate value (SelectedRateUnit-aware, already handled by Foreman.Core's ActualRate/DesiredRate),
    //fixed/auto toggle, simple-passthrough checkbox, key-node controls, and the exact live-update map
    //(reference §4, mirroring EditRecipePanel's transcribed map in §3): fixed-rate edits and the fixed/auto
    //toggle re-solve; the passthrough checkbox and key-node fields only redraw.
    public class EditFlowPanelTests {
        private const int Half = 200;

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static (DataCache Cache, ItemPrototype Item, QualityPrototype Quality) NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "logistics-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var item = new ItemPrototype(cache, "iron-plate", "Iron Plate", subgroup, "a") { Available = true };
            store.Items[item.Name] = item;

            return (cache, item, quality);
        }

        private static GraphCanvasControl NewControl(DataCache cache) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();
            return control;
        }

        private static INodeViewModel Supplier(GraphCanvasControl control, ItemQualityPair item, Point location) {
            NodeId id = control.Viewer.Session.Editor.CreateSupplierNode(item, location);
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            return node!;
        }

        private static INodeViewModel Consumer(GraphCanvasControl control, ItemQualityPair item, Point location) {
            NodeId id = control.Viewer.Session.Editor.CreateConsumerNode(item, location);
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            return node!;
        }

        private static INodeViewModel Passthrough(GraphCanvasControl control, ItemQualityPair item, Point location) {
            NodeId id = control.Viewer.Session.Editor.CreatePassthroughNode(item, location);
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            return node!;
        }

        [AvaloniaFact]
        public void FixedRateEdit_UpdatesNodeAndResolvesDownstream() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);

            INodeViewModel supplier = Supplier(control, pair, new Point(-100, 0));
            INodeViewModel consumer = Consumer(control, pair, new Point(100, 0));
            control.Viewer.Session.Editor.CreateLink(supplier.Id, consumer.Id, pair);

            if (control.Viewer.Session.Editor.RequestNodeController(supplier.Id) is not BaseNodeController supplierController)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            supplierController.SetRateType(RateType.Manual);
            supplierController.SetDesiredSetValue(5);
            control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(5, consumer.ActualSetValue, 3);

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(-100, 0));

            panel.FixedFlowInput.Value = 40m;

            Assert.Equal(40, supplier.DesiredSetValue, 3);
            Assert.Equal(40, consumer.ActualSetValue, 3);
        }

        [AvaloniaFact]
        public void FixedOptionChecked_SwitchesNodeToManualRateType() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));
            Assert.Equal(RateType.Auto, supplier.RateType);

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));

            Assert.False(panel.FixedFlowInput.IsEnabled);
            panel.FixedOption.IsChecked = true;

            Assert.Equal(RateType.Manual, supplier.RateType);
            Assert.True(panel.FixedFlowInput.IsEnabled);
        }

        [AvaloniaFact]
        public void AutoOptionChecked_SwitchesNodeBackToAutoRateType() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            if (control.Viewer.Session.Editor.RequestNodeController(supplier.Id) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            controller.SetRateType(RateType.Manual);

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Assert.True(panel.FixedOption.IsChecked);

            panel.AutoOption.IsChecked = true;

            Assert.Equal(RateType.Auto, supplier.RateType);
            Assert.False(panel.FixedFlowInput.IsEnabled);
        }

        [AvaloniaFact]
        public void RateDisplay_ConvertsPerSelectedRateUnit_PerSecondThenPerMinute() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            control.Viewer.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Sec;
            if (control.Viewer.Session.Editor.RequestNodeController(supplier.Id) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(2); //2 items/sec while SelectedRateUnit is Per1Sec

            var perSecondPanel = new EditFlowPanel(supplier, control.Viewer);
            Assert.Equal(2m, perSecondPanel.FixedFlowInput.Value);
            Assert.Contains("1 sec", perSecondPanel.RateLabel.Text);

            control.Viewer.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Min; //same underlying per-sec rate, new display unit
            var perMinutePanel = new EditFlowPanel(supplier, control.Viewer);
            Assert.Equal(120m, perMinutePanel.FixedFlowInput.Value); //2/sec * 60
            Assert.Contains("1 min", perMinutePanel.RateLabel.Text);
        }

        [AvaloniaFact]
        public void PassthroughCheckbox_TogglesNodeSimpleDrawFlag() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel passthrough = Passthrough(control, pair, new Point(0, 0));
            var passthroughNode = (IPassthroughNodeViewModel)passthrough;
            Assert.False(passthroughNode.SimpleDraw);

            var panel = new EditFlowPanel(passthrough, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Assert.True(panel.SimplePassthroughNodesCheckBox.IsVisible);
            Assert.False(panel.SimplePassthroughNodesCheckBox.IsChecked);

            panel.SimplePassthroughNodesCheckBox.IsChecked = true;

            Assert.True(passthroughNode.SimpleDraw);
        }

        //Review finding 1: upstream declares RateLabel.Font at 10pt bold (Designer.cs), which is a point size
        //- Avalonia's FontSize is device-independent px at 96 DPI, so it needs the 96/72 conversion rather
        //than being left at the panel's ambient size the way it used to be.
        [AvaloniaFact]
        public void RateLabel_UsesUpstreamsBoldTenPointSize_ConvertedToAvaloniaPixels() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            var panel = new EditFlowPanel(supplier, control.Viewer);

            Assert.Equal(Avalonia.Media.FontWeight.Bold, panel.RateLabel.FontWeight);
            Assert.Equal(10.0 * 96.0 / 72.0, panel.RateLabel.FontSize, 3);
        }

        [AvaloniaFact]
        public void PassthroughCheckbox_HiddenForNonPassthroughNodes() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            var panel = new EditFlowPanel(supplier, control.Viewer);

            Assert.False(panel.SimplePassthroughNodesCheckBox.IsVisible);
        }

        [AvaloniaFact]
        public void KeyNodeAndTitleEdits_DoNotTriggerAResolve() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);

            INodeViewModel supplier = Supplier(control, pair, new Point(-100, 0));
            INodeViewModel consumer = Consumer(control, pair, new Point(100, 0));
            control.Viewer.Session.Editor.CreateLink(supplier.Id, consumer.Id, pair);

            if (control.Viewer.Session.Editor.RequestNodeController(supplier.Id) is not BaseNodeController supplierController)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            supplierController.SetRateType(RateType.Manual);
            supplierController.SetDesiredSetValue(5);
            control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(5, consumer.ActualSetValue, 3);

            //A stale mutation only a real resolve would clear - if KeyNode/title edits below silently
            //resolve, this stale value updates and the test's own staleness check goes false-negative.
            supplierController.SetDesiredSetValue(20);
            Assert.Equal(5, consumer.ActualSetValue, 3); //still stale, proves the setter alone doesn't resolve

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(-100, 0));

            panel.KeyNodeCheckBox.IsChecked = true;
            panel.KeyNodeTitleInput.Text = "Hello";

            Assert.Equal(5, consumer.ActualSetValue, 3); //still stale: neither edit re-solved
            Assert.True(supplier.KeyNode);
            Assert.Equal("Hello", supplier.KeyNodeTitle);

            control.Viewer.Graph.UpdateNodeValues(); //sanity: staleness check itself is meaningful
            Assert.Equal(20, consumer.ActualSetValue, 3);
        }

        //Review finding 1: upstream wires FixedOption.CheckedChanged/FixedFlowInput.ValueChanged at
        //InitializeComponent time, before InitializeRates() runs (EditFlowPanel.cs:36-40) - so setting
        //FixedFlowInput.Value to the node's live ActualSetValue during InitializeRates already syncs a stale
        //DesiredSetValue up to what's on screen, since SetRateType alone never touches DesiredSetValue
        //(BaseNode.cs:155,157). A supplier feeding a linked consumer, left on Auto with a DesiredSetValue
        //that has drifted from what the solver actually produced, isolates that pre-seed: toggling Fixed
        //afterward must apply the value the panel displayed, not the stale one still sitting on the node.
        [AvaloniaFact]
        public void FixedToggle_OnAutoNodeWithDivergedDesiredValue_AppliesDisplayedActualValueNotStaleDesired() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);

            INodeViewModel supplier = Supplier(control, pair, new Point(-100, 0));
            INodeViewModel consumer = Consumer(control, pair, new Point(100, 0));
            control.Viewer.Session.Editor.CreateLink(supplier.Id, consumer.Id, pair);

            if (control.Viewer.Session.Editor.RequestNodeController(supplier.Id) is not BaseNodeController supplierController)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            if (control.Viewer.Session.Editor.RequestNodeController(consumer.Id) is not BaseNodeController consumerController)
                throw new Xunit.Sdk.XunitException("Consumer node has no controller.");
            consumerController.SetRateType(RateType.Manual);
            consumerController.SetDesiredSetValue(7);
            supplierController.SetDesiredSetValue(999); //stale: supplier stays Auto, so solve never reads this
            control.Viewer.Graph.UpdateNodeValues();

            Assert.Equal(RateType.Auto, supplier.RateType);
            Assert.Equal(7, supplier.ActualSetValue, 3); //solved to satisfy the consumer's demand
            Assert.Equal(999, supplier.DesiredSetValue, 3); //still stale, untouched by an Auto-node solve

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(-100, 0));
            Assert.Equal(7m, panel.FixedFlowInput.Value); //displayed value is what the solver actually produced

            panel.FixedOption.IsChecked = true; //toggle only - never touch FixedFlowInput directly

            Assert.Equal(RateType.Manual, supplier.RateType);
            Assert.Equal(7, supplier.DesiredSetValue, 3); //applies the displayed value, not the stale 999
        }

        //Review finding 2: upstream's KeyNodeTitleInput carries MaxLength=200 (Designer.cs:157).
        [AvaloniaFact]
        public void KeyNodeTitleInput_TypedTextBeyond200Chars_IsTruncatedToMaxLength() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.KeyNodeCheckBox.IsChecked = true;
            panel.KeyNodeTitleInput.Text = "";
            panel.KeyNodeTitleInput.Focus();

            var window = (AvaloniaWindow)TopLevel.GetTopLevel(control)!;
            window.KeyTextInput(new string('a', 250));

            Assert.Equal(200, panel.KeyNodeTitleInput.Text?.Length);
            Assert.Equal(200, supplier.KeyNodeTitle.Length);
        }

        //---- task deliverable: offscreen render of the real EditFlowPanel bound to a real node ----

        [AvaloniaFact]
        public void Render_EditFlowPanel_BoundToRealNode_ProducesNonEmptyPngInSddWorkspace() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel passthrough = Passthrough(control, pair, new Point(0, 0));

            if (control.Viewer.Session.Editor.RequestNodeController(passthrough.Id) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Passthrough node has no controller.");
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(12.5);
            controller.SetKeyNode(true);

            var panel = new EditFlowPanel(passthrough, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));

            const int width = 320;
            const int height = 200;
            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);
            string outPath = Path.Combine(sddDir, "task-4-flow-panel-render.png");

            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            panel.RenderOffscreen(surface.Canvas, width, height);
            using SKImage renderedImage = surface.Snapshot();
            using SKData data = renderedImage.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        //---- Human nit: FixedFlowInput hits the same Finding-B4 spinner squish as FixedAssemblerInput -
        //120px minus the spinner buttons' ~68px leaves room for only about four digits. Flow values run to
        //six digits just as often as assembler counts, so it needs the same widening ----

        [AvaloniaFact]
        public void FixedFlowInput_WideEnoughForSixDigits() {
            (DataCache cache, ItemPrototype item, QualityPrototype quality) = NewFixture();
            GraphCanvasControl control = NewControl(cache);
            var pair = new ItemQualityPair(item, quality);
            INodeViewModel supplier = Supplier(control, pair, new Point(0, 0));

            var panel = new EditFlowPanel(supplier, control.Viewer);
            control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            TextBox valueBox = TemplateChild<TextBox>(panel.FixedFlowInput, "PART_TextBox");
            double neededWidth = SixDigitTextWidth(valueBox);
            Assert.True(valueBox.Bounds.Width >= neededWidth,
                $"expected the fixed-flow value box to fit six digits ({neededWidth}px), got width {valueBox.Bounds.Width}");
        }

        //Measures "999999" against the value box's own font/padding rather than hardcoding a pixel count,
        //so the assertion tracks whatever font Avalonia's Fluent theme actually renders with.
        private static double SixDigitTextWidth(TextBox valueBox) {
            var typeface = new Avalonia.Media.Typeface(valueBox.FontFamily, valueBox.FontStyle, valueBox.FontWeight);
            var sixDigits = new Avalonia.Media.FormattedText("999999", System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight, typeface, valueBox.FontSize, null);
            return sixDigits.Width + valueBox.Padding.Left + valueBox.Padding.Right;
        }

        private static T TemplateChild<T>(AvaloniaVisual root, string partName) where T : AvaloniaVisual {
            return root.GetVisualDescendants()
                .OfType<T>()
                .First(v => (v as Avalonia.StyledElement)?.Name == partName);
        }
    }
}
