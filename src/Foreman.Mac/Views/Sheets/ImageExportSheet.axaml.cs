using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public partial class ImageExportSheet : SheetContent<bool>, IDisposable {
        private static readonly float[] Multipliers = [0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 3f];
        private static readonly string[] MultiplierNames = ["1/20", "1/10", "1/5", "1/2", "1", "2", "3"];
        private const int DefaultScaleIndex = 4;
        private const int RasterBands = 8;

        private readonly GraphViewer viewer;
        private readonly TextBlock savePathLabel;
        private readonly Button browseButton;
        private readonly PopUp scaleSelectionBox;
        private readonly CheckBox transparencyCheckBox;
        private readonly CheckBox viewLimitCheckBox;
        private readonly TextBlock imageSizeLabel;
        private readonly Button exportButton;
        private readonly FormRow saveToRow;
        private readonly CancellationTokenSource exportCancellation = new();
        private string savePath = "";

        public const string SavePanelTitle = "Export an image";

        internal Func<Task<string?>>? SaveFilePathStub { get; set; }
        internal byte[]? LastPngBytes { get; private set; }

        internal Func<Task>? EncodeGate { get; set; }
        internal Func<string, byte[], Task>? WriteImageStub { get; set; }

        public ImageExportSheet() : this(new GraphViewer(new Viewport(), new GridManager())) {
        }

        public ImageExportSheet(GraphViewer viewer) {
            InitializeComponent();
            this.viewer = viewer;
            Title = SavePanelTitle;

            savePathLabel = this.FindControl<TextBlock>("SavePathLabel")!;
            browseButton = this.FindControl<Button>("BrowseButton")!;
            scaleSelectionBox = this.FindControl<PopUp>("ScaleSelectionBox")!;
            transparencyCheckBox = this.FindControl<CheckBox>("TransparencyCheckBox")!;
            viewLimitCheckBox = this.FindControl<CheckBox>("ViewLimitCheckBox")!;
            imageSizeLabel = this.FindControl<TextBlock>("ImageSizeLabel")!;
            saveToRow = this.FindControl<FormRow>("SaveToRow")!;
            Button cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            exportButton = SheetButtons.Make("Export", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancelButton, exportButton);
            DefaultButton = exportButton;

            foreach (string name in MultiplierNames)
                scaleSelectionBox.Items.Add(name);
            scaleSelectionBox.SelectedItem = MultiplierNames[DefaultScaleIndex];
            UpdateSizeLabel();
            ShowSavePath();
            TrimmedTooltip.SetEnabled(imageSizeLabel, true);

            browseButton.Click += (_, _) => Async.Fire(BrowseAsync(), nameof(BrowseAsync));
            cancelButton.Click += (_, _) => Cancel();
            exportButton.Click += (_, _) => Async.Fire(ExportAsync(), nameof(ExportAsync));
            scaleSelectionBox.PropertyChanged += (_, e) => {
                if (e.Property == PopUp.SelectedItemProperty)
                    UpdateSizeLabel();
            };
            viewLimitCheckBox.IsCheckedChanged += (_, _) => UpdateSizeLabel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal async Task BrowseAsync() {
            string? path = await (SaveFilePathStub?.Invoke() ?? RealPickSaveImagePathAsync()).ConfigureAwait(true);
            if (path is not null)
                SavePath = path;
        }

        internal string SavePath {
            get => savePath;
            set {
                savePath = value ?? "";
                ShowSavePath();
            }
        }

        private void ShowSavePath() {
            bool chosen = savePath.Length > 0;
            savePathLabel.Text = chosen ? HomeRelative(savePath) : "Not chosen";
            savePathLabel.Classes.Set("tertiary", !chosen);
            TrimmedTooltip.SetEnabled(savePathLabel, chosen);
        }

        private static string HomeRelative(string path) {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0 && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return "~" + path[home.Length..];
            return path;
        }

        private async Task<string?> RealPickSaveImagePathAsync() {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
                return null;

            Directory.CreateDirectory(AppPaths.ExportedGraphsDirectory);
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = SavePanelTitle,
                SuggestedFileName = "Foreman Production Flowchart.png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.ExportedGraphsDirectory)).ConfigureAwait(true),
                FileTypeChoices = [new FilePickerFileType("PNG files") { Patterns = ["*.png"] }],
            }).ConfigureAwait(true);
            return file?.Path.LocalPath;
        }

        internal async Task ExportAsync() {
            ClearActivity();
            saveToRow.Error = null;
            string path = savePath;
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(path)) {
                ShowPathProblem("Choose where to save the image.");
                return;
            }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
                ShowPathProblem("This folder doesn’t exist. Choose another location.");
                return;
            }

            viewer.ClearSelection();
            float scale = SelectedMultiplier();
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
                    ReportFailure("There’s nothing to export. Add nodes or annotations to the graph first.");
                    return;
                }
                transform = new GraphViewer.ExportTransform(0, 0, scale, new Point(-bounds.X, -bounds.Y));
            }

            int width = GraphExportBounds.ScaledWidth(bounds, scale);
            int height = GraphExportBounds.ScaledHeight(bounds, scale);

            using SKPicture picture = RecordPaint(width, height, transparent, transform);
            CancellationToken token = exportCancellation.Token;
            bool rendering = true;
            ReportProgress(RenderingSentence(0), 0);
            var progress = new Progress<double>(fraction => {
                if (rendering && !token.IsCancellationRequested)
                    ReportProgress(RenderingSentence(fraction), fraction);
            });
            Func<Task>? gate = EncodeGate;
            byte[] png = await Task.Run(() => RasteriseAndEncodeAsync(picture, width, height, progress, gate), CancellationToken.None).ConfigureAwait(true);
            rendering = false;
            if (token.IsCancellationRequested)
                return;

            LastPngBytes = png;
            try {
                await (WriteImageStub ?? WriteFileAsync)(path, png).ConfigureAwait(true);
            } catch (Exception exception) {
                ErrorLogging.LogException(exception, "Error saving image");
                if (!token.IsCancellationRequested)
                    ReportFailure("Couldn’t save the image. The folder is read-only or no longer there.");
                return;
            }
            if (!token.IsCancellationRequested)
                Complete(true);
        }

        private void ShowPathProblem(string sentence) {
            saveToRow.Error = sentence;
            browseButton.Focus();
        }

        private static string RenderingSentence(double fraction) =>
            string.Create(DisplayCulture.Format, $"Rendering the graph — {Math.Round(fraction * 100):0} %.");

        private SKPicture RecordPaint(int width, int height, bool transparent, GraphViewer.ExportTransform transform) {
            using var recorder = new SKPictureRecorder();
            SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0, width, height));
            if (!transparent)
                canvas.Clear(viewer.BackgroundColor);
            viewer.Paint(canvas, fullGraph: true, clearBackground: false, exportTransform: transform);
            return recorder.EndRecording();
        }

        private static async Task<byte[]> RasteriseAndEncodeAsync(SKPicture picture, int width, int height, IProgress<double> progress, Func<Task>? gate) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            int bandHeight = Math.Max(1, (height + RasterBands - 1) / RasterBands);
            for (int top = 0, band = 1; top < height; top += bandHeight, band++) {
                canvas.Save();
                canvas.ClipRect(new SKRect(0, top, width, Math.Min(height, top + bandHeight)));
                canvas.DrawPicture(picture);
                canvas.Restore();
                progress.Report(0.9 * band / RasterBands);
            }
            if (gate is not null)
                await gate().ConfigureAwait(false);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            progress.Report(1);
            return data.ToArray();
        }

        private static Task WriteFileAsync(string path, byte[] png) => File.WriteAllBytesAsync(path, png);

        public override void Cancel() {
            exportCancellation.Cancel();
            base.Cancel();
        }

        public void Dispose() {
            exportCancellation.Dispose();
            GC.SuppressFinalize(this);
        }

        private Rectangle ViewLimitedBounds() =>
            new(0, 0, (int)(viewer.Viewport.Width / viewer.Viewport.ViewScale), (int)(viewer.Viewport.Height / viewer.Viewport.ViewScale));

        private float SelectedMultiplier() => Multipliers[Math.Max(0, Array.IndexOf(MultiplierNames, scaleSelectionBox.SelectedItem))];

        private void UpdateSizeLabel() {
            float scale = SelectedMultiplier();
            Rectangle bounds = viewLimitCheckBox.IsChecked == true
                ? ViewLimitedBounds()
                : AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);

            bool exportable = GraphExportBounds.IsExportable(bounds);
            imageSizeLabel.Classes.Set("secondary", exportable);
            imageSizeLabel.Classes.Set("tertiary", !exportable);
            imageSizeLabel.Text = exportable
                ? string.Create(CultureInfo.InvariantCulture, $"{GraphExportBounds.ScaledWidth(bounds, scale)} × {GraphExportBounds.ScaledHeight(bounds, scale)} px")
                : "Nothing to export";
        }

        internal TextBlock SavePathLabelControl => savePathLabel;
        internal Button BrowseButtonControl => browseButton;
        internal PopUp ScaleSelectionBoxControl => scaleSelectionBox;
        internal CheckBox TransparencyCheckBoxControl => transparencyCheckBox;
        internal CheckBox ViewLimitCheckBoxControl => viewLimitCheckBox;
        internal TextBlock ImageSizeLabelControl => imageSizeLabel;
    }
}
