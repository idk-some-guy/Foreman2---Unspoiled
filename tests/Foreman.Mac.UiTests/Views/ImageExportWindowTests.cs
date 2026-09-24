using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
            Assert.Equal($"{expectedX:N0} × {expectedY:N0} px", sheet.ImageSizeLabelControl.Text);
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
            Assert.Equal($"{expectedX:N0} × {expectedY:N0} px", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ViewLimitChecked_SwitchesToViewportPixelDimensions() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            sheet.ViewLimitCheckBoxControl.IsChecked = true;

            Assert.Equal("400 × 300 px", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_EmptyGraphFullGraphMode_ShowsNothingToExportText() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            Assert.Equal("— (nothing to export)", sheet.ImageSizeLabelControl.Text);
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
            sheet.FileTextBoxControl.Text = TempPngPath();

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
            sheet.FileTextBoxControl.Text = TempPngPath();

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
            sheet.FileTextBoxControl.Text = TempPngPath();

            await sheet.ExportAsync().WaitAsync(Timeout);

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(0, bitmap.GetPixel(5, 5).Alpha);
        }

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyUnchecked_BackgroundPixelIsOpaque() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.FileTextBoxControl.Text = TempPngPath();

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
            sheet.FileTextBoxControl.Text = path;

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
        public async Task ExportAsync_FolderDoesNotExist_IsTheSaveToRowError_FocusesThePath_AndWritesNoFile() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (_, SheetHost host, Task<bool> result) = SheetFixture.Present(sheet);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "export.png");
            sheet.FileTextBoxControl.Text = path;

            await sheet.ExportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("This folder doesn’t exist. Choose another location.", SaveToRow(sheet).Error);
            Assert.False(SaveToRow(sheet).ErrorIsWarning);
            Assert.True(sheet.FileTextBoxControl.IsFocused);
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
            Assert.True(sheet.FileTextBoxControl.IsFocused);

            sheet.FileTextBoxControl.Text = TempPngPath();
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
            sheet.FileTextBoxControl.Text = path;

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
        public async Task BrowseAsync_StubReturnsPath_FillsTextBoxWithoutExporting() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            string path = TempPngPath();
            sheet.SaveFilePathStub = () => Task.FromResult<string?>(path);

            await sheet.BrowseAsync();

            Assert.Equal(path, sheet.FileTextBoxControl.Text);
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

        [AvaloniaFact]
        public void ImageExport_Width_IsTheNamedSheetWidth() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Export an image", SheetFixture.FrameOf(host, sheet).Title);
            FormRow[] rows = [.. sheet.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(["Save to", "Scale", "Options"], rows.Select(r => r.Label));
            Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Window, r.LabelColumn));
            Assert.Equal("Browse…", sheet.BrowseButtonControl.Content);
            Assert.Equal("Transparent background", sheet.TransparencyCheckBoxControl.Content);
            Assert.Equal("Limit to the visible area", sheet.ViewLimitCheckBoxControl.Content);
            FormRow saveTo = rows[0];
            Grid rowGrid = saveTo.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Grid");
            double columnRight = rowGrid.TranslatePoint(new Avalonia.Point(rowGrid.Bounds.Width, 0), sheet)!.Value.X;
            Button browse = sheet.BrowseButtonControl;
            TextBox field = sheet.FileTextBoxControl;
            Grid pair = (Grid)field.Parent!;
            Assert.Equal(columnRight, pair.TranslatePoint(new Avalonia.Point(pair.Bounds.Width, 0), sheet)!.Value.X, 0.5);
            Assert.Equal(columnRight, browse.TranslatePoint(new Avalonia.Point(browse.Bounds.Width, 0), sheet)!.Value.X, 0.5);
            Assert.Equal(GridUnitType.Star, pair.ColumnDefinitions[0].Width.GridUnitType);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, field.HorizontalAlignment);
            Assert.Equal(sheet.Bounds.Width, columnRight, 0.5);
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
            sheet.FileTextBoxControl.Text = folder;

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
            Assert.True(sheet.FileTextBoxControl.IsEffectivelyVisible);

            string path = TempPngPath();
            sheet.FileTextBoxControl.Text = path;
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
            sheet.FileTextBoxControl.Text = path;

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
            sheet.FileTextBoxControl.Text = path;

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
