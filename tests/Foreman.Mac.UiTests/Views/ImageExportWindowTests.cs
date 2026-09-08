using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
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

namespace Foreman.Mac.UiTests.Views {
    //Covers io-reference.md §3's PNG export dialog contract (phase 6 Task 3, moved onto SheetHost by C2c
    //Task 2): scale mapping, the live Image Size label (incl. its nothing-to-export text), the view-limited
    //vs. full-graph transform branches, transparency, and the export guards. ShapeAnnotationElement stands
    //in for a node here - its fixed graph-space rect and settable FillColor/BorderWidth make pixel sampling
    //exact, and none of this dialog's math touches DataCache at all, so no preset needs loading.
    public class ImageExportWindowTests {
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

            Assert.Equal(ExpectedScaleNames, sheet.ScaleSelectionBoxControl.ItemsSource);
            Assert.Equal(4, sheet.ScaleSelectionBoxControl.SelectedIndex);
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
            Assert.Equal($"Image Size: {expectedX:N0} x {expectedY:N0}", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ScaleChangedToHalf_HalvesReportedDimensions() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);

            sheet.ScaleSelectionBoxControl.SelectedIndex = 3; //"1/2" -> 0.5x

            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            int expectedX = GraphExportBounds.ScaledWidth(bounds, 0.5f);
            int expectedY = GraphExportBounds.ScaledHeight(bounds, 0.5f);
            Assert.Equal($"Image Size: {expectedX:N0} x {expectedY:N0}", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ViewLimitChecked_SwitchesToViewportPixelDimensions() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            sheet.ViewLimitCheckBoxControl.IsChecked = true;

            Assert.Equal("Image Size: 400 x 300", sheet.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_EmptyGraphFullGraphMode_ShowsNothingToExportText() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            Assert.Equal("Image Size: — (nothing to export)", sheet.ImageSizeLabelControl.Text);
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

            await sheet.ExportAsync();

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

            await sheet.ExportAsync();

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

            await sheet.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(0, bitmap.GetPixel(5, 5).Alpha);
        }

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyUnchecked_BackgroundPixelIsOpaque() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var sheet = new ImageExportSheet(viewer);
            sheet.FileTextBoxControl.Text = TempPngPath();

            await sheet.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(sheet.LastPngBytes)!;
            Assert.Equal(255, bitmap.GetPixel(5, 5).Alpha);
        }

        //---- guards (reference §3 ExportButton_Click) ---------------------------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_EmptyGraphFullGraphMode_ShowsNothingToExportWarningAndWritesNoFile() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            string? capturedMessage = null;
            sheet.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };
            string path = TempPngPath();
            sheet.FileTextBoxControl.Text = path;

            await sheet.ExportAsync();

            Assert.Equal("There is nothing to export. Add nodes or annotations to the graph first.", capturedMessage);
            Assert.False(File.Exists(path));
            Assert.Null(sheet.LastPngBytes);
        }

        [AvaloniaFact]
        public async Task ExportAsync_DirectoryDoesNotExist_ShowsVerbatimWarningAndWritesNoFile() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));
            string? capturedMessage = null;
            sheet.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "export.png");
            sheet.FileTextBoxControl.Text = path;

            await sheet.ExportAsync();

            Assert.Equal("Directory doesn't exist!", capturedMessage);
            Assert.False(File.Exists(path));
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
                await sheet.ExportAsync();

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

        //---- SheetHost contract (C2c Task 2): width from the sheets table, Enter/Esc map to the former
        //Export/close results ---------------------------------------------------------------------------

        [AvaloniaFact]
        public void SheetWidth_Is348() {
            var sheet = new ImageExportSheet(NewViewer(400, 300));

            Assert.Equal(348, sheet.SheetWidth);
        }

        [AvaloniaFact]
        public async Task Enter_TriggersExport_AndEscape_ClosesWithoutExporting() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var sheet1 = new ImageExportSheet(viewer);
            string path = TempPngPath();
            sheet1.FileTextBoxControl.Text = path;
            Task<bool> task1 = host.ShowAsync(sheet1);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet1.FileTextBoxControl, Key.Enter);
            Assert.True(await task1);
            File.Delete(path);
            AnimationClock.Settle();

            var sheet2 = new ImageExportSheet(viewer);
            Task<bool> task2 = host.ShowAsync(sheet2);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet2.FileTextBoxControl, Key.Escape);
            Assert.False(await task2);
            Assert.False(host.IsOpen);
        }
    }
}
