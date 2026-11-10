using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Foreman.Graph;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models.Research;
using System;

namespace Foreman.Mac.Views.Research {
    public sealed partial class ResearchWindow : Window, IThemeAware {
        private static readonly string[] SegmentLabels = ["Bonuses", "Recipes", "Technologies"];

        private readonly Segmented segmentControl;
        private readonly Border toolbarRow;
        private readonly Border bodyHost;
        private readonly ResearchSourceBar sourceBar = new();
        private readonly WindowMinSizeScale minSizeScale;
        private readonly Panel recipesHost = new();
        private readonly Panel technologiesHost = new();

        private IProductionGraphSession? session;
        private ResearchWindowHost host = null!;
        private ResearchSyncFlow? flow;
        private ResearchViewModel viewModel = null!;
        private ResearchBonusesView bonuses = null!;
        private int segment;
        private bool selecting;
        private bool saveMissing;

        public ResearchWindow() {
            InitializeComponent();
            segmentControl = this.FindControl<Segmented>("SegmentControl")!;
            toolbarRow = this.FindControl<Border>("ToolbarRow")!;
            bodyHost = this.FindControl<Border>("BodyHost")!;
            this.FindControl<Panel>("SourceBarHost")!.Children.Add(sourceBar);

            segmentControl.Items.AddRange(SegmentLabels);
            AutomationProperties.SetName(segmentControl, "Research view");
            segmentControl.PropertyChanged += (_, e) => {
                if (e.Property == Segmented.SelectedIndexProperty && !selecting)
                    SelectSegment(segmentControl.SelectedIndex);
            };

            sourceBar.ChooseButton.Click += (_, _) => RunSync(chooseAnother: true);
            sourceBar.SyncButton.Click += (_, _) => RunSync(chooseAnother: false);
            sourceBar.RevertAllButton.Click += (_, _) => {
                viewModel.RevertAll();
                Refresh();
            };

            AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            minSizeScale = new WindowMinSizeScale(this);
            Opened += (_, _) => AppearanceHub.Current?.Subscribe(this);
            Activated += (_, _) => CheckSource();
            Closing += (_, _) => ResearchPlacement.Remember(this, host?.Settings, segment);
            Closed += (_, _) => {
                AppearanceHub.Current?.Unsubscribe(this);
                Detach();
            };
        }

        internal static ResearchWindow Create(IProductionGraphSession session, ResearchWindowHost host) {
            var window = new ResearchWindow();
            window.Attach(session, host);
            return window;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void Attach(IProductionGraphSession session, ResearchWindowHost host) {
            this.session = session;
            this.host = host;
            flow = host.Sync();
            flow.RunningChanged += OnRunningChanged;
            session.NodeValuesUpdated += OnNodeValuesUpdated;
            viewModel = new ResearchViewModel(session.Graph);
            bonuses = new ResearchBonusesView(viewModel, () => SelectSegment(1));
            bonuses.Changed += (_, _) => Refresh();
            segment = Math.Clamp(host.Settings?.ResearchSegment ?? 0, 0, 2);
            ResearchPlacement.Apply(this, host.Settings);
            ShowSegment();
            CheckSource();
            Refresh();
        }

        private void Detach() {
            if (session is not null)
                session.NodeValuesUpdated -= OnNodeValuesUpdated;
            session = null;
            if (flow is not null)
                flow.RunningChanged -= OnRunningChanged;
            flow = null;
        }

        private void OnRunningChanged(object? sender, EventArgs e) => Refresh();

        private void OnNodeValuesUpdated(object? sender, EventArgs e) {
            if (Dispatcher.UIThread.CheckAccess())
                Refresh();
            else
                Dispatcher.UIThread.Post(Refresh);
        }

        private void RunSync(bool chooseAnother) {
            if (flow is not null)
                Async.Fire(flow.RunAsync(this, chooseAnother), nameof(ResearchSyncFlow.RunAsync));
        }

        internal void SetTitle(string graphName) => Title = $"Research — {Dialogs.MiddleTruncate(graphName, 40)}";

        internal void Refresh() {
            if (session is null)
                return;
            bonuses.Refresh();
            UpdateSourceBar();
        }

        internal void CheckSource() {
            if (session is null)
                return;
            string? path = session.Graph.Research.Source?.SavePath;
            saveMissing = path is not null && !host.SaveExists(path);
            UpdateSourceBar();
        }

        private void UpdateSourceBar() =>
            sourceBar.Update(session!.Graph.Research, flow?.IsRunning == true, saveMissing, host.Clock.GetLocalNow().DateTime);

        internal void SelectSegment(int index) {
            segment = Math.Clamp(index, 0, 2);
            if (host?.Settings is AppSettings settings)
                settings.ResearchSegment = segment;
            ShowSegment();
        }

        private void ShowSegment() {
            if (segmentControl.SelectedIndex != segment) {
                selecting = true;
                segmentControl.SelectedIndex = segment;
                selecting = false;
            }
            bodyHost.Child = segment switch {
                1 => recipesHost,
                2 => technologiesHost,
                _ => bonuses,
            };
        }

        public void RefreshTheme() => minSizeScale.Apply();

        private void OnWindowKeyDown(object? sender, KeyEventArgs e) {
            bool meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (meta && e.Key == Key.W) {
                Close();
                e.Handled = true;
            } else if (meta && !alt && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is >= Key.D1 and <= Key.D3) {
                SelectSegment(e.Key - Key.D1);
                e.Handled = true;
            } else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && FocusManager?.GetFocusedElement() is not TextBox) {
                Close();
                e.Handled = true;
            }
        }

        internal Segmented SegmentControl => segmentControl;
        internal Border ToolbarRowControl => toolbarRow;
        internal ResearchSourceBar SourceBar => sourceBar;
        internal ResearchBonusesView BonusesView => bonuses;
        internal Panel RecipesHost => recipesHost;
        internal Panel TechnologiesHost => technologiesHost;
        internal ResearchViewModel ViewModel => viewModel;
        internal ResearchBonusRow BonusRow(ResearchField field) => bonuses.BonusRow(field);
        internal ResearchRecipeProductivityRow RecipeProductivityRow => bonuses.RecipeProductivityRow;
    }
}
