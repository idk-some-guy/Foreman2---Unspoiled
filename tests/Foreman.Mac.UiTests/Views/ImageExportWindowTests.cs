using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Point = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Views {
    //Covers the PNG export sheet (io-reference.md §3): scale mapping, the live size label, the view-limited and
    //full-graph transforms, transparency, the in-sheet guards, and the in-progress and failed strips.
    //ShapeAnnotationElement stands in for a node: its fixed graph-space rect and settable FillColor make
    //pixel sampling exact, and none of this sheet's maths touches DataCache.
    public class ImageExportWindowTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static GraphViewer NewViewer(double width, double height) =>
            new(new Viewport(width, height), new GridManager());

        private static void AddSolidShape(GraphViewer viewer, Point center, int size, SKColor color) {
            var shape = new ShapeAnnotationElement(center, size, size) { FillColor = color, BorderWidth = 0 };
            viewer.AddAnnotationElement(shape);
        }

        private static string TempPngPath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

        //---- scale/multiplier wiring -------------------------------------------------------------------

        private static readonly string[] ExpectedScaleNames = ["1/20", "1/10", "1/5", "1/2", "1", "2", "3"];

        [AvaloniaFact]
        public void Constructor_PopulatesSevenScaleOptions_DefaultsToOneX() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            Assert.Equal(ExpectedScaleNames, sheet.ScaleSelectionBoxControl.Items);
            Assert.Equal("1", sheet.ScaleSelectionBoxControl.SelectedItem);
        }

        //---- size label math (reference §3 UpdateSizeLabel) --------------------------------------------

        [AvaloniaFact]
        public void ImageSizeLabel_FullGraphAtDefaultScale_MatchesExportBoundsMath() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);

            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            int expectedX = GraphExportBounds.ScaledWidth(bounds, 1f);
            int expectedY = GraphExportBounds.ScaledHeight(bounds, 1f);
            Assert.Equal($"{expectedX} × {expectedY} px", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ScaleChangedToHalf_HalvesReportedDimensions() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);

            sheet.ScaleSelectionBoxControl.SelectedItem = "1/2";

            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            int expectedX = GraphExportBounds.ScaledWidth(bounds, 0.5f);
            int expectedY = GraphExportBounds.ScaledHeight(bounds, 0.5f);
            Assert.Equal($"{expectedX} × {expectedY} px", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ViewLimitChecked_SwitchesToViewportPixelDimensions() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            sheet.ViewLimitCheckBoxControl.IsChecked = true;

            Assert.Equal("400 × 300 px", sheet.ImageSizeLabelControl.Text);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(sheet.ImageSizeLabelControl);
            Assert.Equal(("Image size", "400 × 300 px"), (peer.GetName(), peer.GetProvider<IValueProvider>()!.Value));
        }

        [AvaloniaFact]
        public void ImageSizeLabel_EmptyGraphFullGraphMode_ReadsNothingToExport_InTextTertiary() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            SheetFixture.Present(sheet);

            TextBlock label = sheet.ImageSizeLabelControl;
            Assert.Equal("Nothing to export", label.Text);
            Assert.Equal("Nothing to export", ControlAutomationPeer.CreatePeerForElement(label).GetProvider<IValueProvider>()!.Value);
            Assert.Equal(Brush("Foreman.Color.TextTertiary"), label.Foreground?.ToImmutable());
            Assert.DoesNotContain("—", label.Text);
            Assert.DoesNotContain("(", label.Text);
        }

        private static Avalonia.Media.IImmutableBrush Brush(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return value switch {
                Avalonia.Media.IBrush brush => brush.ToImmutable(),
                Avalonia.Media.Color color => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color),
                _ => throw new InvalidOperationException(key),
            };
        }

        //Invariant digits with no thousands separator, U+00D7 between them, whatever the display culture.
        [AvaloniaFact]
        public void ImageSizeLabel_ReadsPlainDimensions_UnderEveryCulture() {
            foreach (string culture in new[] { "de-DE", "en-US" })
                AssertPlainDimensions(culture);
        }

        private static void AssertPlainDimensions(string culture) {
            System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            try {
                GraphViewer viewer = NewViewer(400, 300);
                viewer.AddAnnotationElement(new ShapeAnnotationElement(new Point(0, 0), 2040, 1260) { BorderWidth = 0 });
                var sheet = new ImageExportSheet(viewer);
                SheetFixture.Present(sheet);

                TextBlock label = sheet.ImageSizeLabelControl;
                Assert.Equal("2140 × 1360 px", label.Text);
                Assert.Equal(Brush("Foreman.Color.TextSecondary"), label.Foreground?.ToImmutable());
                Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, label.TextTrimming);
                Assert.Equal(0, label.MinWidth);
                Assert.Equal("Image size", Avalonia.Automation.AutomationProperties.GetName(label));
            } finally {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        //---- pixel-sampled export: view-limit off vs. on (reference §3 ExportBitmap) --------------------

        //Viewport is 400x300 at the default ViewOffset (0,0)/ViewScale (1) - a shape centered at graph
        //x=2000 sits nowhere near that. Full-graph bounds = the shape's own rect padded by
        //GraphExportBounds.AnnotationOnlyPadding (50px each side, since there are no nodes) = a 140x140
        //rect with the shape's 40x40 fill centered at local (70,70).
        [AvaloniaFact]
        public async Task ExportAsync_NodeOutsideViewport_ViewLimitOff_IsPresentInExportedImage() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(2000, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.SavePath = TempPngPath();

            await sheet.ExportAsync().WaitAsync(Timeout);

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(140, bitmap.Width);
            Assert.Equal(140, bitmap.Height);
            Assert.Equal(SKColors.Red, bitmap.GetPixel(70, 70));
        }

        [AvaloniaFact]
        public async Task ExportAsync_NodeOutsideViewport_ViewLimitOn_IsAbsentFromExportedImage() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(2000, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.ViewLimitCheckBoxControl.IsChecked = true;
            sheet.SavePath = TempPngPath();

            await sheet.ExportAsync().WaitAsync(Timeout);

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(400, bitmap.Width);
            Assert.Equal(300, bitmap.Height);
            Assert.DoesNotContain(SKColors.Red, bitmap.Pixels);
        }

        //---- transparency (reference §3 ExportBitmap's conditional Clear) ------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyChecked_BackgroundPixelIsFullyTransparent() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.TransparencyCheckBoxControl.IsChecked = true;
            sheet.SavePath = TempPngPath();

            await sheet.ExportAsync().WaitAsync(Timeout);

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(0, bitmap.GetPixel(5, 5).Alpha);
        }

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyUnchecked_BackgroundPixelIsOpaque() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.SavePath = TempPngPath();

            await sheet.ExportAsync().WaitAsync(Timeout);

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(255, bitmap.GetPixel(5, 5).Alpha);
        }

        //---- guards: a field problem is the Save to row's error, an operation problem the strip, never an alert ----

        private static FormRow SaveToRow(ImageExportSheet sheet) =>
            sheet.GetVisualDescendants().OfType<FormRow>().Single(r => r.Label == "Save to");

        [AvaloniaFact]
        public async Task ExportAsync_EmptyGraphFullGraphMode_ShowsNothingToExportInTheStripAndWritesNoFile() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            string path = TempPngPath();
            sheet.SavePath = path;

            await sheet.ExportAsync().WaitAsync(Timeout);
            AnimationClock.Settle();

            SheetFixture.AssertNoAlert(host);
            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            Assert.Equal("There’s nothing to export. Add nodes or annotations to the graph first.", strip.Sentence);
            Assert.False(result.IsCompleted);
            Assert.False(File.Exists(path));
            Assert.Null(sheet.LastPngBytes);
        }

        [AvaloniaFact]
        public async Task ExportAsync_FolderDoesNotExist_IsTheSaveToRowError_FocusesBrowse_AndWritesNoFile() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "export.png");
            sheet.SavePath = path;

            await sheet.ExportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("This folder doesn’t exist. Choose another location.", SaveToRow(sheet).Error);
            Assert.False(SaveToRow(sheet).ErrorIsWarning);
            Assert.True(sheet.BrowseButtonControl.IsFocused);
            Assert.False(result.IsCompleted);
            Assert.False(File.Exists(path));
        }

        [AvaloniaFact]
        public async Task ExportAsync_NoPath_AsksWhereToSave_ThenAValidPathClearsTheRowError() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            sheet.WriteImageStub = (_, _) => Task.CompletedTask;

            await sheet.ExportAsync().WaitAsync(Timeout);
            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Choose where to save the image.", SaveToRow(sheet).Error);
            Assert.True(sheet.BrowseButtonControl.IsFocused);

            sheet.SavePath = TempPngPath();
            await sheet.ExportAsync().WaitAsync(Timeout);
            Assert.Null(SaveToRow(sheet).Error);
            Assert.True(await result);
        }

        //---- happy path -----------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_ValidPath_WritesDecodablePngAndClosesTheSheet() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<bool> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            string path = TempPngPath();
            sheet.SavePath = path;

            try {
                await sheet.ExportAsync().WaitAsync(Timeout);

                Assert.True(File.Exists(path));
                using SKBitmap bitmap = SKBitmap.Decode(path);
                Assert.NotNull(bitmap);
                Assert.True(await task);
                AnimationClock.Settle();
                Assert.False(host.IsOpen);
            } finally {
                File.Delete(path);
            }
        }

        //---- Browse fills the textbox but does not export (reference §3 button1_Click) ------------------

        [AvaloniaFact]
        public async Task BrowseAsync_StubReturnsPath_FillsThePathWithoutExporting() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            string path = TempPngPath();
            sheet.SaveFilePathStub = () => Task.FromResult<string?>(path);

            await sheet.BrowseAsync();

            Assert.Equal(path, sheet.SavePath);
            Assert.False(File.Exists(path));
        }

        //---- MainWindow wiring (reference upstream MainForm.cs:508-514) --------------------------------

        [AvaloniaFact]
        public void OpenImageExportAsync_PassesLiveViewerToDialog() {
            var window = new MainWindow();
            window.Show();

            GraphViewer? captured = null;
            window.ImageExportDialogStub = viewer => captured = viewer;

            _ = window.OpenImageExportAsync();

            Assert.Same(window.GraphCanvas.Viewer, captured);
        }

        //---- the kit sheet: named width, window-column rows, trailing button row, failed save ------------

        //Tab starts on Browse…, skips the path label, and walks Scale, the two options and the buttons before wrapping.
        [AvaloniaFact]
        public void ImageExport_Width_IsTheNamedSheetWidth_AndTabWalksTheControls() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (Window window, SheetHost host, _) = SheetFixture.Present(sheet);
            Assert.True(sheet.BrowseButtonControl.IsFocused);
            Button[] buttons = [.. sheet.Buttons!.Items.Cast<Button>()];
            foreach (Control expected in new Control[] { sheet.ScaleSelectionBoxControl, sheet.TransparencyCheckBoxControl, sheet.ViewLimitCheckBoxControl, buttons[0], buttons[1], sheet.BrowseButtonControl }) {
                Avalonia.Input.IInputElement? focused = window.FocusManager!.GetFocusedElement();
                Foreman.Mac.UiTests.support.CanvasKeys.KeyDownOn((Control)focused!, Avalonia.Input.Key.Tab);
                Assert.True(expected.IsKeyboardFocusWithin, $"Tab should reach {expected}, reached {window.FocusManager.GetFocusedElement()}");
            }

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Export an image", SheetFixture.FrameOf(host, sheet).Title);
            FormRow[] rows = [.. sheet.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(["Save to", "Scale", "Options"], rows.Select(r => r.Label));
            Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Window, r.LabelColumn));
            Assert.Equal("Browse…", sheet.BrowseButtonControl.Content);
            Assert.Equal("Transparent background", sheet.TransparencyCheckBoxControl.Content);
            Assert.Equal("Limit to the visible area", sheet.ViewLimitCheckBoxControl.Content);
        }

        //---- Save to row: a front-truncating path label with Browse… trailing at its natural width ----

        private static string LongPathUnderHome() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "Factorio", "Builds", "Megabase", "Iron and copper smelting columns", "west block", "iron-plate-line-final.png");

        //At the default font and a wider Linux-like one (1.12x) the label absorbs the growth and Browse never overlaps it.
        [AvaloniaFact]
        public void SaveTo_LongPath_TruncatesTheLabel_AndBrowseKeepsItsNaturalWidthAtTheTrailingEdge() {
            foreach (double fontFactor in new[] { 1.0, 1.12 })
                AssertSaveToRowFits(fontFactor);
        }

        private static void AssertSaveToRowFits(double fontFactor) {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            foreach (string key in new[] { "Foreman.Type.Body.FontSize", "Foreman.Type.Callout.FontSize" })
                sheet.Resources[key] = SheetFixture.Token(key) * fontFactor;
            string path = LongPathUnderHome();
            Assert.True(path.Length >= 120);
            (_, SheetHost host, _) = SheetFixture.Present(sheet);
            sheet.SavePath = path;
            AnimationClock.Settle();

            FormRow saveTo = SaveToRow(sheet);
            Assert.Empty(saveTo.GetVisualDescendants().OfType<TextBox>());
            Grid rowGrid = saveTo.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Grid");
            double columnRight = rowGrid.TranslatePoint(new Avalonia.Point(rowGrid.Bounds.Width, 0), sheet)!.Value.X;
            Button browse = sheet.BrowseButtonControl;
            TextBlock label = sheet.SavePathLabelControl;
            double browseLeft = browse.TranslatePoint(new Avalonia.Point(0, 0), sheet)!.Value.X;
            double labelRight = label.TranslatePoint(new Avalonia.Point(label.Bounds.Width, 0), sheet)!.Value.X;
            Assert.Equal(browse.DesiredSize.Width - browse.Margin.Left - browse.Margin.Right, browse.Bounds.Width, 0.5);
            Assert.Equal(columnRight, browseLeft + browse.Bounds.Width, 0.5);
            Assert.True(labelRight + 8 <= browseLeft + 0.5, $"label right {labelRight}, browse left {browseLeft}");
            Assert.Equal(22, ((Grid)browse.Parent!).Bounds.Height, 0.5);
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal(Avalonia.Media.TextTrimming.LeadingCharacterEllipsis, label.TextTrimming);
            Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, label.TextWrapping);
            Assert.Equal(0, label.MinWidth);
            Assert.False(label.Focusable);
        }

        [AvaloniaFact]
        public void SaveTo_Label_ReadsNotChosenThenTheTildePath_WithATooltipOnlyWhenTrimmed_AndBrowseSaysWhatItDoes() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            SheetFixture.Present(sheet);
            TextBlock label = sheet.SavePathLabelControl;
            Assert.Equal("Not chosen", label.Text);
            Assert.Equal(Brush("Foreman.Color.TextTertiary"), label.Foreground?.ToImmutable());
            Assert.Null(ToolTip.GetTip(label));
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(label);
            Assert.Equal(("Save to", "Not chosen"), (peer.GetName(), peer.GetProvider<IValueProvider>()!.Value));

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            sheet.SavePath = Path.Combine(home, "a");
            AnimationClock.Settle();
            Assert.Equal("~/a", label.Text);
            Assert.Equal(("Save to", "~/a"), (peer.GetName(), peer.GetProvider<IValueProvider>()!.Value));
            Assert.Null(ToolTip.GetTip(label));
            Assert.Equal(Brush("Foreman.Color.Text"), label.Foreground?.ToImmutable());

            sheet.SavePath = LongPathUnderHome();
            AnimationClock.Settle();
            Assert.StartsWith("~/", label.Text);
            Assert.Equal(label.Text, ToolTip.GetTip(label));

            Assert.Equal("Choose where to save the image.", Avalonia.Automation.AutomationProperties.GetHelpText(sheet.BrowseButtonControl));
            Assert.Equal(ImageExportSheet.SavePanelTitle, sheet.Title);
        }

        [AvaloniaFact]
        public void ImageExport_ButtonRow_IsTrailingAutoWidth_CancelLeftOfExport_NeverFullBleed() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (Window window, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);

            ButtonRow row = Assert.IsType<ButtonRow>(sheet.Buttons);
            Button[] buttons = [.. row.Items.Cast<Button>()];
            Assert.Equal(["Cancel", "Export"], buttons.Select(b => b.Content));
            Assert.Equal(ButtonRowRole.Cancel, ButtonRow.GetRole(buttons[0]));
            Assert.Equal(ButtonRowRole.Default, ButtonRow.GetRole(buttons[1]));
            Assert.Same(buttons[1], row.ResolveDefaultButton());
            Assert.True(buttons[0].Bounds.Right < buttons[1].Bounds.Left);
            Assert.Equal(row.Bounds.Width, buttons[1].Bounds.Right, 0.5);
            Assert.True(buttons[1].Bounds.Width < row.Bounds.Width / 2);
            Assert.Equal(buttons[1].DesiredSize.Width, buttons[1].Bounds.Width, 0.5);

            SheetFixture.Click(window, buttons[0]);
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
            Assert.False(host.IsOpen);
        }

        [AvaloniaFact]
        public async Task ExportAsync_WriteFails_ShowsTheCompactError_AndTheDefaultBecomesTryAgain() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            sheet.SavePath = folder;

            await sheet.ExportAsync().WaitAsync(Timeout);
            AnimationClock.Settle();
            SheetFixture.AssertNoAlert(host);

            Assert.False(result.IsCompleted);
            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            Assert.Equal("Couldn’t save the image. The folder is read-only or no longer there.", strip.Sentence);
            Button export = sheet.Buttons!.ResolveDefaultButton()!;
            Assert.Equal("Try Again", export.Content);
            Assert.True(export.IsEffectivelyEnabled);
            Assert.True(sheet.SavePathLabelControl.IsEffectivelyVisible);

            string path = TempPngPath();
            sheet.SavePath = path;
            await sheet.ExportAsync().WaitAsync(Timeout);
            Assert.True(await result);
            File.Delete(path);
        }

        //---- in progress: the paint is recorded on the UI thread, the pixels are encoded and written on a worker ----

        [AvaloniaFact]
        public async Task ExportAsync_WhileEncoding_ShowsTheRenderingStrip_AndCancelDiscardsTheLateResult() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            var gate = new TaskCompletionSource();
            var gated = new TaskCompletionSource();
            int writes = 0;
            sheet.EncodeGate = () => { gated.TrySetResult(); return gate.Task; };
            sheet.WriteImageStub = (_, _) => { writes++; return Task.CompletedTask; };
            string path = TempPngPath();
            sheet.SavePath = path;

            Task run = sheet.ExportAsync();
            await gated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AnimationClock.Settle(500);

            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Loading, strip.Shape);
            Assert.Matches(@"^Rendering the graph — \d{1,3} %\.$", strip.Sentence);
            Assert.InRange(strip.Progress!.Value, 0, 1);
            Button export = sheet.Buttons!.ResolveDefaultButton()!;
            Assert.False(export.IsEnabled);
            Button cancel = sheet.Buttons.Items.Cast<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Cancel);
            Assert.True(cancel.IsEffectivelyEnabled);

            cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(10)));

            gate.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, writes);
            Assert.False(File.Exists(path));
        }

        [AvaloniaFact]
        public async Task ExportAsync_GateReleasedOnAnOpenSheet_WritesOnceAndCompletesTrue() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            (_, _, Task<bool> result) = SheetFixture.Present(sheet);
            var gate = new TaskCompletionSource();
            string? writtenPath = null;
            byte[]? writtenBytes = null;
            sheet.EncodeGate = () => gate.Task;
            sheet.WriteImageStub = (p, b) => { writtenPath = p; writtenBytes = b; return Task.CompletedTask; };
            string path = TempPngPath();
            sheet.SavePath = path;

            Task run = sheet.ExportAsync();
            Assert.False(result.IsCompleted);
            gate.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(path, writtenPath);
            using SKBitmap bitmap = SKBitmap.Decode(writtenBytes)!;
            Assert.Equal(SKColors.Red, bitmap.GetPixel(70, 70));
        }
    }
}
