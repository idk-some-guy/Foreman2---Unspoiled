using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    public partial class DataLoadWindow : Window {
        private const string CloseWarningTitle = "Preset load interrupted";
        private const string CloseWarningMessage =
            "Preset loading is still in progress.\n\n" +
            "Closing this window does not stop it. Loading continues in the background, and Foreman finishes setting up once it completes.";

        private readonly Preset selectedPreset;
        private readonly bool filterRecipes;
        private readonly ProgressBar progressBar;
        private readonly TextBlock statusTextBlock;
        private int currentPercent;
        private string currentText = "";
        private bool loadInProgress;
        private bool loadCompleted;
        private bool closeWarningAcknowledged;

        public Task LoadTask { get; }
        public DataCache? Result { get; private set; }
        public int Progress => (int)progressBar.Value;
        public string StatusText => statusTextBlock.Text ?? "";
        public bool CloseWarningDialogShown { get; private set; }

        public DataLoadWindow() : this(new Preset(string.Empty, false, false)) {
        }

        public DataLoadWindow(Preset preset, bool filterRecipes = true) {
            InitializeComponent();
            progressBar = this.FindControl<ProgressBar>("LoadProgressBar")!;
            statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")!;
            selectedPreset = preset;
            this.filterRecipes = filterRecipes;
            LoadTask = RunLoadAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async Task RunLoadAsync() {
            var progress = new Progress<KeyValuePair<int, string>>(ApplyProgress);
            var cache = new DataCache(filterRecipes);
            loadInProgress = true;
            try {
                await cache.LoadAllData(selectedPreset, progress).ConfigureAwait(false);
                Result = cache;
                loadCompleted = true;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, string.Format(CultureInfo.InvariantCulture, "Failed to load preset '{0}'", selectedPreset.Name));
                Result = null;
            } finally {
                loadInProgress = false;
            }
            await Dispatcher.UIThread.InvokeAsync(Close);
        }

        private void ApplyProgress(KeyValuePair<int, string> value) {
            if (value.Key > currentPercent) {
                currentPercent = value.Key;
                progressBar.Value = value.Key;
            }
            if (!string.IsNullOrEmpty(value.Value) && value.Value != currentText) {
                currentText = value.Value;
                statusTextBlock.Text = value.Value;
                Title = "Preparing Foreman: " + value.Value;
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e) {
            if (loadInProgress && !loadCompleted && !closeWarningAcknowledged) {
                e.Cancel = true;
                ErrorLogging.LogLine(CloseWarningMessage);
                CloseWarningDialogShown = true;
                Async.Fire(AcknowledgeCloseWarningAsync(), nameof(AcknowledgeCloseWarningAsync));
                return;
            }
            base.OnClosing(e);
        }

        private async Task AcknowledgeCloseWarningAsync() {
            await Dialogs.ShowWarningAsync(this, CloseWarningTitle, CloseWarningMessage).ConfigureAwait(true);
            closeWarningAcknowledged = true;
            Close();
        }
    }
}
