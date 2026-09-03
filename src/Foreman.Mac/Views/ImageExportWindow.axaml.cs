using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports ImageExportForm(+.Designer.cs) (reference io-reference.md §3, upstream ImageExportForm.cs): the
    //PNG export dialog - scale selector, transparency/view-limit toggles, a live size label, and the
    //Browse/Export pair. GraphExportBounds/AnnotationLoader.GetExportBounds already carry the bounds math;
    //this window owns the SKSurface render and the StorageProvider picker seam.
    public partial class ImageExportWindow : Window {
        private static readonly float[] Multipliers = [0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 3f];
        private static readonly string[] MultiplierNames = ["1/20", "1/10", "1/5", "1/2", "1", "2", "3"];
        private const int DefaultScaleIndex = 4;

        private readonly GraphViewer viewer;
        private readonly TextBox fileTextBox;
        private readonly Button browseButton;
        private readonly ComboBox scaleSelectionBox;
        private readonly CheckBox transparencyCheckBox;
        private readonly CheckBox viewLimitCheckBox;
        private readonly TextBlock imageSizeLabel;
        private readonly Button exportButton;

        //Test-only seam: lets a test supply the Browse picker's result without a real modal SaveFileDialog
        //(same convention as MainWindow/GraphSummaryWindow's own SaveFilePathStub).
        internal Func<Task<string?>>? SaveFilePathStub { get; set; }
        internal byte[]? LastPngBytes { get; private set; }

        //Test-only seam: lets a test capture the two warning messages (directory-doesn't-exist, nothing-to-
        //export) without a real modal MessageDialog (same convention as SettingsWindow's WarningDialogStub).
        internal Func<string, string, Task>? WarningDialogStub { get; set; }

        public ImageExportWindow() : this(new GraphViewer(new Viewport(), new GridManager())) {
        }

        public ImageExportWindow(GraphViewer viewer) {
            InitializeComponent();
            this.viewer = viewer;

            fileTextBox = this.FindControl<TextBox>("FileTextBox")!;
            browseButton = this.FindControl<Button>("BrowseButton")!;
            scaleSelectionBox = this.FindControl<ComboBox>("ScaleSelectionBox")!;
            transparencyCheckBox = this.FindControl<CheckBox>("TransparencyCheckBox")!;
            viewLimitCheckBox = this.FindControl<CheckBox>("ViewLimitCheckBox")!;
            imageSizeLabel = this.FindControl<TextBlock>("ImageSizeLabel")!;
            exportButton = this.FindControl<Button>("ExportButton")!;

            scaleSelectionBox.ItemsSource = MultiplierNames;
            scaleSelectionBox.SelectedIndex = DefaultScaleIndex;
            UpdateSizeLabel();

            browseButton.Click += (_, _) => Async.Fire(BrowseAsync(), nameof(BrowseAsync));
            exportButton.Click += (_, _) => Async.Fire(ExportAsync(), nameof(ExportAsync));
            scaleSelectionBox.SelectionChanged += (_, _) => UpdateSizeLabel();
            viewLimitCheckBox.IsCheckedChanged += (_, _) => UpdateSizeLabel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports button1_Click (reference §3): the picker only fills the path textbox, it doesn't export.
        internal async Task BrowseAsync() {
            string? path = await (SaveFilePathStub?.Invoke() ?? RealPickSaveImagePathAsync()).ConfigureAwait(true);
            if (path is not null)
                fileTextBox.Text = path;
        }

        private async Task<string?> RealPickSaveImagePathAsync() {
            if (StorageProvider is not IStorageProvider storage)
                return null;

            Directory.CreateDirectory(AppPaths.ExportedGraphsDirectory);
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "Export an Image",
                SuggestedFileName = "Foreman Production Flowchart.png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.ExportedGraphsDirectory)).ConfigureAwait(true),
                FileTypeChoices = [new FilePickerFileType("PNG files") { Patterns = ["*.png"] }],
            }).ConfigureAwait(true);
            return file?.Path.LocalPath;
        }

        //Ports ExportButton_Click/ExportBitmap (reference §3): the view-limited branch always renders
        //(upstream never guards it against an empty graph); only the full-graph branch checks
        //GraphExportBounds.IsExportable and shows the nothing-to-export message.
        internal async Task ExportAsync() {
            string path = fileTextBox.Text ?? "";
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
                await ShowWarningAsync("", "Directory doesn't exist!").ConfigureAwait(true);
                return;
            }

            viewer.ClearSelection();
            float scale = Multipliers[scaleSelectionBox.SelectedIndex];
            bool transparent = transparencyCheckBox.IsChecked == true;

            Rectangle bounds;
            GraphViewer.ExportTransform transform;
            if (viewLimitCheckBox.IsChecked == true) {
                bounds = ViewLimitedBounds();
                transform = new GraphViewer.ExportTransform(
                    viewer.Viewport.Width * scale / viewer.Viewport.ViewScale,
                    viewer.Viewport.Height * scale / viewer.Viewport.ViewScale,
                    scale,
                    viewer.Viewport.ViewOffset);
            } else {
                bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
                if (!GraphExportBounds.IsExportable(bounds)) {
                    await ShowWarningAsync("", "There is nothing to export. Add nodes or annotations to the graph first.").ConfigureAwait(true);
                    return;
                }
                transform = new GraphViewer.ExportTransform(0, 0, scale, new Point(-bounds.X, -bounds.Y));
            }

            int width = GraphExportBounds.ScaledWidth(bounds, scale);
            int height = GraphExportBounds.ScaledHeight(bounds, scale);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            if (!transparent)
                canvas.Clear(viewer.BackgroundColor);
            viewer.Paint(canvas, fullGraph: true, clearBackground: false, exportTransform: transform);

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            LastPngBytes = data.ToArray();

            try {
                File.WriteAllBytes(path, LastPngBytes);
                Close();
            } catch (Exception exception) {
                await ShowWarningAsync("", "Error saving image. See log for more details.").ConfigureAwait(true);
                ErrorLogging.LogException(exception, "Error saving image");
            }
        }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        //Ports ViewLimitedBounds (reference §3): graph-space extent of the current viewport, independent of
        //ViewOffset - only used to size the export image.
        private Rectangle ViewLimitedBounds() =>
            new(0, 0, (int)(viewer.Viewport.Width / viewer.Viewport.ViewScale), (int)(viewer.Viewport.Height / viewer.Viewport.ViewScale));

        //Ports UpdateSizeLabel (reference §3), verbatim including the em dash in the nothing-to-export text.
        private void UpdateSizeLabel() {
            float scale = Multipliers[scaleSelectionBox.SelectedIndex];
            Rectangle bounds = viewLimitCheckBox.IsChecked == true
                ? ViewLimitedBounds()
                : AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);

            if (!GraphExportBounds.IsExportable(bounds)) {
                imageSizeLabel.Text = "Image Size: — (nothing to export)";
                return;
            }

            int x = GraphExportBounds.ScaledWidth(bounds, scale);
            int y = GraphExportBounds.ScaledHeight(bounds, scale);
            imageSizeLabel.Text = $"Image Size: {x:N0} x {y:N0}";
        }

        //-------------------------------------------------------------------------------------------------------Test-only seams

        internal TextBox FileTextBoxControl => fileTextBox;
        internal ComboBox ScaleSelectionBoxControl => scaleSelectionBox;
        internal CheckBox TransparencyCheckBoxControl => transparencyCheckBox;
        internal CheckBox ViewLimitCheckBoxControl => viewLimitCheckBox;
        internal TextBlock ImageSizeLabelControl => imageSizeLabel;
    }
}
