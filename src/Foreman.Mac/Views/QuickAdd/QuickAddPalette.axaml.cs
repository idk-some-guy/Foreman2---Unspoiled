using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Foreman.Mac.Views.QuickAdd {
    public sealed partial class QuickAddPalette : UserControl {
        internal const int MaxRows = 6;

        private readonly PalettePanel panel;
        //Value-keyed: the panel keeps its previous result instances when a requery yields an equal set.
        private readonly Dictionary<PaletteResult, QuickAddRow> rowFor = [];
        private readonly Dictionary<IImage, IBrush> iconBrushes = [];
        private QuickAddViewModel? subscribedVm;

        //Parameterless so the XAML runtime loader can find it (AVLN3001); the caller assigns DataContext separately.
        public QuickAddPalette() {
            InitializeComponent();
            panel = this.FindControl<PalettePanel>("Panel")!;
            panel.FooterHints.Add(new PaletteFooterHint { KeyText = "↵", KeyAccessibleName = "Return", Description = "Place" });
            panel.FooterHints.Add(new PaletteFooterHint { KeyText = "⌥↵", KeyAccessibleName = "Option Return", Description = "Place, stay open" });
            panel.FooterHints.Add(new PaletteFooterHint { KeyText = "⌘↵", KeyAccessibleName = "Command Return", Description = "Browse…" });
            panel.FooterHints.Add(new PaletteFooterHint { KeyText = "esc", KeyAccessibleName = "Escape", Description = "Close" });
            panel.Committed += OnCommitted;
            panel.CommittedAsSource += OnCommittedKeepingOpen;
            panel.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
            panel.Search.GetObservable(TextBox.TextProperty).Subscribe(new TextSink(OnSearchTextChanged));
            //Tunnel on an ancestor of the panel, so ⌘Return is caught before the panel's own Return commits.
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            DataContextChanged += OnDataContextChanged;
        }

        public event EventHandler<AddCandidate>? PickCommitted;
        public event EventHandler<string>? BrowseRequested;
        public event EventHandler? CloseRequested;

        internal SearchField Search => panel.Search;

        private QuickAddViewModel? Vm => DataContext as QuickAddViewModel;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void FocusInitialControl() => panel.Search.Focus();

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (subscribedVm is not null) {
                subscribedVm.RowsChanged -= OnRowsChanged;
                subscribedVm.PropertyChanged -= OnVmPropertyChanged;
                subscribedVm.ConstraintChanged -= OnConstraintChanged;
            }
            subscribedVm = Vm;
            if (subscribedVm is null)
                return;
            subscribedVm.RowsChanged += OnRowsChanged;
            subscribedVm.PropertyChanged += OnVmPropertyChanged;
            subscribedVm.ConstraintChanged += OnConstraintChanged;
            panel.Search.Text = subscribedVm.Query;
            ApplyCopy();
            ApplyConstraint();
            OnRowsChanged(this, EventArgs.Empty);
        }

        private void OnSearchTextChanged(string? text) {
            if (Vm is QuickAddViewModel vm && vm.Query != (text ?? ""))
                vm.Query = text ?? "";
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(QuickAddViewModel.Query) && Vm is QuickAddViewModel vm && panel.Search.Text != vm.Query)
                panel.Search.Text = vm.Query;
            ApplyCopy();
        }

        private void ApplyCopy() {
            if (Vm is not QuickAddViewModel vm)
                return;
            panel.Search.PlaceholderText = vm.Watermark;
            panel.NoResultsHeadline = vm.NoResultsHeadline;
            panel.NoResultsSentence = vm.NoResultsSentence;
        }

        private void OnRowsChanged(object? sender, EventArgs e) {
            if (Vm is not QuickAddViewModel vm)
                return;
            rowFor.Clear();
            List<PaletteResult> results = [];
            foreach (QuickAddRow row in vm.Rows.Take(MaxRows)) {
                var result = new PaletteResult(row.Name, row.Sub, IconBrush(row.Icon));
                rowFor.TryAdd(result, row);
                results.Add(result);
            }
            panel.Results.Clear();
            panel.Results.AddRange(results);
        }

        //One brush per baked bitmap, so an unchanged row compares equal across queries and the panel replays nothing.
        private IBrush? IconBrush(IImage? icon) {
            if (icon is not IImageBrushSource source)
                return null;
            if (!iconBrushes.TryGetValue(icon, out IBrush? brush))
                iconBrushes[icon] = brush = new ImageBrush(source) { Stretch = Stretch.Uniform };
            return brush;
        }

        private void OnCommitted(object? sender, PaletteResult result) {
            if (!rowFor.TryGetValue(result, out QuickAddRow? row))
                return;
            PickCommitted?.Invoke(this, row.Candidate);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnCommittedKeepingOpen(object? sender, PaletteResult result) {
            if (rowFor.TryGetValue(result, out QuickAddRow? row))
                PickCommitted?.Invoke(this, row.Candidate);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta)) {
                RequestBrowse();
                e.Handled = true;
            }
        }

        private void RequestBrowse() => BrowseRequested?.Invoke(this, Vm?.Query ?? "");

        private void OnConstraintChanged(object? sender, EventArgs e) {
            ApplyConstraint();
            panel.Search.Focus();
        }

        private void ApplyConstraint() {
            if (Vm is not QuickAddViewModel vm)
                return;
            bool constrained = vm.ConstraintText is not null;
            panel.EmptyContent = constrained ? null : BuildEmptyState();
            panel.NoResultsButtons = constrained
                ? Buttons((QuickAddViewModel.BrowseAllLabel, ButtonRowRole.Default, RequestBrowse), (QuickAddViewModel.RemoveConstraintLabel, ButtonRowRole.Other, vm.RemoveConstraint))
                : Buttons((QuickAddViewModel.BrowseAllLabel, ButtonRowRole.Default, RequestBrowse));
            panel.HeaderAccessory = constrained ? BuildChip(vm) : null;
        }

        private StatePanel BuildEmptyState() => new() {
            Theme = KitTheme("Kit.StatePanel"),
            Shape = StatePanelShape.Empty,
            Headline = QuickAddViewModel.EmptyHeadline,
            Sentence = QuickAddViewModel.EmptySentence,
            Buttons = Buttons((QuickAddViewModel.BrowseAllLabel, ButtonRowRole.Default, RequestBrowse)),
        };

        private static ButtonRow Buttons(params (string Label, ButtonRowRole Role, Action Act)[] specs) {
            var row = new ButtonRow { Theme = KitTheme("Kit.ButtonRow") };
            foreach ((string label, ButtonRowRole role, Action act) in specs) {
                var button = new Button { Theme = KitTheme("Kit.Button"), Content = label };
                ButtonRow.SetRole(button, role);
                button.Click += (_, _) => act();
                row.Items.Add(button);
            }
            return row;
        }

        private Chip BuildChip(QuickAddViewModel vm) {
            var chip = new Chip { Theme = KitTheme("Kit.Chip"), Label = vm.ConstraintText, RemoveAccessibleName = QuickAddViewModel.RemoveConstraintLabel };
            ChipHost.SetFocusFallback(chip, panel.Search);
            chip.Removed += (_, _) => vm.RemoveConstraint();
            return chip;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"QuickAddPalette: kit theme '{key}' not found.");
        }

        private sealed class TextSink(Action<string?> onNext) : IObserver<string?> {
            public void OnNext(string? value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
