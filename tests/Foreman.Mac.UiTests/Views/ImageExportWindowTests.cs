using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Views;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers io-reference.md §3's PNG export dialog contract (phase 6 Task 3): scale mapping, the live
    //Image Size label (incl. its nothing-to-export text), the view-limited vs. full-graph transform
    //branches, transparency, and the export guards. ShapeAnnotationElement stands in for a node here - its
    //fixed graph-space rect and settable FillColor/BorderWidth make pixel sampling exact, and none of this
    //dialog's math touches DataCache at all, so no preset needs loading.
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
            var window = new ImageExportWindow(NewViewer(400, 300));

            Assert.Equal(ExpectedScaleNames, window.ScaleSelectionBoxControl.ItemsSource);
            Assert.Equal(4, window.ScaleSelectionBoxControl.SelectedIndex);
        }

        //---- size label math (reference §3 UpdateSizeLabel) --------------------------------------------

        [AvaloniaFact]
        public void ImageSizeLabel_FullGraphAtDefaultScale_MatchesExportBoundsMath() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);

            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            int expectedX = GraphExportBounds.ScaledWidth(bounds, 1f);
            int expectedY = GraphExportBounds.ScaledHeight(bounds, 1f);
            Assert.Equal($"Image Size: {expectedX:N0} x {expectedY:N0}", window.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ScaleChangedToHalf_HalvesReportedDimensions() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);

            window.ScaleSelectionBoxControl.SelectedIndex = 3; //"1/2" -> 0.5x

            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            int expectedX = GraphExportBounds.ScaledWidth(bounds, 0.5f);
            int expectedY = GraphExportBounds.ScaledHeight(bounds, 0.5f);
            Assert.Equal($"Image Size: {expectedX:N0} x {expectedY:N0}", window.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_ViewLimitChecked_SwitchesToViewportPixelDimensions() {
            var window = new ImageExportWindow(NewViewer(400, 300));

            window.ViewLimitCheckBoxControl.IsChecked = true;

            Assert.Equal("Image Size: 400 x 300", window.ImageSizeLabelControl.Text);
        }

        [AvaloniaFact]
        public void ImageSizeLabel_EmptyGraphFullGraphMode_ShowsNothingToExportText() {
            var window = new ImageExportWindow(NewViewer(400, 300));

            Assert.Equal("Image Size: — (nothing to export)", window.ImageSizeLabelControl.Text);
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
            var window = new ImageExportWindow(viewer);
            window.FileTextBoxControl.Text = TempPngPath();

            await window.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(window.LastPngBytes)!;
            Assert.Equal(140, bitmap.Width);
            Assert.Equal(140, bitmap.Height);
            Assert.Equal(SKColors.Red, bitmap.GetPixel(70, 70));
        }

        [AvaloniaFact]
        public async Task ExportAsync_NodeOutsideViewport_ViewLimitOn_IsAbsentFromExportedImage() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(2000, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);
            window.ViewLimitCheckBoxControl.IsChecked = true;
            window.FileTextBoxControl.Text = TempPngPath();

            await window.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(window.LastPngBytes)!;
            Assert.Equal(400, bitmap.Width);
            Assert.Equal(300, bitmap.Height);
            Assert.DoesNotContain(SKColors.Red, bitmap.Pixels);
        }

        //---- transparency (reference §3 ExportBitmap's conditional Clear) ------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyChecked_BackgroundPixelIsFullyTransparent() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);
            window.TransparencyCheckBoxControl.IsChecked = true;
            window.FileTextBoxControl.Text = TempPngPath();

            await window.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(window.LastPngBytes)!;
            Assert.Equal(0, bitmap.GetPixel(5, 5).Alpha);
        }

        [AvaloniaFact]
        public async Task ExportAsync_TransparencyUnchecked_BackgroundPixelIsOpaque() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);
            window.FileTextBoxControl.Text = TempPngPath();

            await window.ExportAsync();

            using SKBitmap bitmap = SKBitmap.Decode(window.LastPngBytes)!;
            Assert.Equal(255, bitmap.GetPixel(5, 5).Alpha);
        }

        //---- guards (reference §3 ExportButton_Click) ---------------------------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_EmptyGraphFullGraphMode_ShowsNothingToExportWarningAndWritesNoFile() {
            var window = new ImageExportWindow(NewViewer(400, 300));
            string? capturedMessage = null;
            window.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };
            string path = TempPngPath();
            window.FileTextBoxControl.Text = path;

            await window.ExportAsync();

            Assert.Equal("There is nothing to export. Add nodes or annotations to the graph first.", capturedMessage);
            Assert.False(File.Exists(path));
            Assert.Null(window.LastPngBytes);
        }

        [AvaloniaFact]
        public async Task ExportAsync_DirectoryDoesNotExist_ShowsVerbatimWarningAndWritesNoFile() {
            var window = new ImageExportWindow(NewViewer(400, 300));
            string? capturedMessage = null;
            window.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "export.png");
            window.FileTextBoxControl.Text = path;

            await window.ExportAsync();

            Assert.Equal("Directory doesn't exist!", capturedMessage);
            Assert.False(File.Exists(path));
        }

        //---- happy path -----------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ExportAsync_ValidPath_WritesDecodablePngAndClosesWindow() {
            GraphViewer viewer = NewViewer(400, 300);
            AddSolidShape(viewer, new Point(0, 0), 40, SKColors.Red);
            var window = new ImageExportWindow(viewer);
            window.Show();
            string path = TempPngPath();
            window.FileTextBoxControl.Text = path;

            try {
                await window.ExportAsync();

                Assert.True(File.Exists(path));
                using SKBitmap bitmap = SKBitmap.Decode(path);
                Assert.NotNull(bitmap);
                Assert.False(window.IsVisible);
            } finally {
                File.Delete(path);
            }
        }

        //---- Browse fills the textbox but does not export (reference §3 button1_Click) ------------------

        [AvaloniaFact]
        public async Task BrowseAsync_StubReturnsPath_FillsTextBoxWithoutExporting() {
            var window = new ImageExportWindow(NewViewer(400, 300));
            string path = TempPngPath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);

            await window.BrowseAsync();

            Assert.Equal(path, window.FileTextBoxControl.Text);
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
    }
}
