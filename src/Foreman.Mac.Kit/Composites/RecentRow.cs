using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    public sealed class RecentRowRemoveRequestedEventArgs(object? item) : EventArgs {
        public object? Item { get; } = item;
    }

    public class RecentRow : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(RecentRow);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(Label));

        public static readonly StyledProperty<string?> PresetLabelProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(PresetLabel));

        public static readonly StyledProperty<string?> UnavailableMetaProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(UnavailableMeta));

        public static readonly StyledProperty<string?> HelpHintsProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(HelpHints));

        public static readonly StyledProperty<int> NodeCountProperty =
            AvaloniaProperty.Register<RecentRow, int>(nameof(NodeCount));

        public static readonly StyledProperty<string?> RelativeTimeProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(RelativeTime));

        public static readonly StyledProperty<string?> AbsoluteTimeProperty =
            AvaloniaProperty.Register<RecentRow, string?>(nameof(AbsoluteTime));

        public static readonly StyledProperty<IBrush?> IconProperty =
            AvaloniaProperty.Register<RecentRow, IBrush?>(nameof(Icon));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<RecentRow, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsKeyProperty =
            AvaloniaProperty.Register<RecentRow, bool>(nameof(IsKey), true);

        public static readonly StyledProperty<bool> IsUnavailableProperty =
            AvaloniaProperty.Register<RecentRow, bool>(nameof(IsUnavailable));

        public static readonly StyledProperty<bool> IsMenuOpenProperty =
            AvaloniaProperty.Register<RecentRow, bool>(nameof(IsMenuOpen));

        public event EventHandler? OpenRequested;

        public event EventHandler<RecentRowRemoveRequestedEventArgs>? RemoveRequested;

        public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string? PresetLabel { get => GetValue(PresetLabelProperty); set => SetValue(PresetLabelProperty, value); }
        public string? UnavailableMeta { get => GetValue(UnavailableMetaProperty); set => SetValue(UnavailableMetaProperty, value); }
        public string? HelpHints { get => GetValue(HelpHintsProperty); set => SetValue(HelpHintsProperty, value); }
        public int NodeCount { get => GetValue(NodeCountProperty); set => SetValue(NodeCountProperty, value); }
        public string? RelativeTime { get => GetValue(RelativeTimeProperty); set => SetValue(RelativeTimeProperty, value); }
        public string? AbsoluteTime { get => GetValue(AbsoluteTimeProperty); set => SetValue(AbsoluteTimeProperty, value); }
        public IBrush? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
        public bool IsKey { get => GetValue(IsKeyProperty); set => SetValue(IsKeyProperty, value); }
        public bool IsUnavailable { get => GetValue(IsUnavailableProperty); set => SetValue(IsUnavailableProperty, value); }
        public bool IsMenuOpen { get => GetValue(IsMenuOpenProperty); set => SetValue(IsMenuOpenProperty, value); }

        public static readonly DirectProperty<RecentRow, Controls.MenuPanel> MenuProperty =
            AvaloniaProperty.RegisterDirect<RecentRow, Controls.MenuPanel>(nameof(Menu), o => o.Menu);

        public AvaloniaList<Controls.MenuRow> MenuItems { get; } = [];

        public Controls.MenuPanel Menu { get; } = new();

        private Border? frame;
        private Border? fill;
        private Border? selectionFill;
        private TextBlock? nameText;
        private TextBlock? nodeCountText;
        private Popup? menuHost;
        private bool hovering;
        private bool hasKeyboardFocus;

        private IBrush accentSoft = Brushes.Transparent;
        private IBrush selectionInactive = Brushes.Transparent;
        private IBrush controlBackgroundHover = Brushes.Transparent;
        private IBrush text = Brushes.Transparent;
        private IBrush textTertiary = Brushes.Transparent;
        private IBrush accentText = Brushes.Transparent;
        private readonly List<IDisposable> subs = [];

        static RecentRow() {
            FocusableProperty.OverrideDefaultValue<RecentRow>(true);
            IsSelectedProperty.Changed.AddClassHandler<RecentRow>((r, _) => r.ApplyState());
            IsKeyProperty.Changed.AddClassHandler<RecentRow>((r, _) => r.ApplyState());
            IsUnavailableProperty.Changed.AddClassHandler<RecentRow>((r, _) => { r.ApplyUnavailable(); r.ApplyState(); r.ApplyAutomation(); });
            IsMenuOpenProperty.Changed.AddClassHandler<RecentRow>((r, e) => r.OnIsMenuOpenChanged((bool)e.NewValue!));
            IsPointerOverProperty.Changed.AddClassHandler<RecentRow>((r, e) => {
                r.hovering = e.NewValue is true;
                r.ApplyState();
            });
            LabelProperty.Changed.AddClassHandler<RecentRow>((r, _) => r.ApplyAutomation());
            PresetLabelProperty.Changed.AddClassHandler<RecentRow>((r, _) => { r.ApplyMeta(); r.ApplyAutomation(); });
            HelpHintsProperty.Changed.AddClassHandler<RecentRow>((r, _) => r.ApplyAutomation());
            NodeCountProperty.Changed.AddClassHandler<RecentRow>((r, _) => { r.ApplyMeta(); r.ApplyAutomation(); });
            AbsoluteTimeProperty.Changed.AddClassHandler<RecentRow>((r, _) => r.ApplyAutomation());
        }

        public RecentRow() {
            Menu.RequestClose += (_, _) => IsMenuOpen = false;
            MenuItems.CollectionChanged += (_, _) => ApplyAutomation();
            GotFocus += (_, e) => {
                if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) {
                    hasKeyboardFocus = true;
                    UpdateRing();
                }
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                UpdateRing();
            };
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            frame = e.NameScope.Find<Border>("PART_Frame");
            fill = e.NameScope.Find<Border>("PART_Fill");
            selectionFill = e.NameScope.Find<Border>("PART_SelectionFill");
            nameText = e.NameScope.Find<TextBlock>("PART_Name");
            nodeCountText = e.NameScope.Find<TextBlock>("PART_NodeCount");
            if (menuHost is not null)
                menuHost.Closed -= OnMenuHostClosed;
            menuHost = e.NameScope.Find<Popup>("PART_Popup");
            if (menuHost is not null) {
                menuHost.Closed += OnMenuHostClosed;
                if (IsMenuOpen) {
                    menuHost.Placement = pendingPlacement;
                    Menu.HighlightedIndex = Menu.Items.Count > 0 ? 0 : -1;
                }
                menuHost.IsOpen = IsMenuOpen;
                pendingPlacement = PlacementMode.BottomEdgeAlignedLeft;
            }
            ApplyState();
            ApplyUnavailable();
            ApplyMeta();
            ApplyAutomation();
            UpdateRing();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
            IsMenuOpen = false;
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentSoft").Subscribe(new ResourceSinks.BrushSink(v => { accentSoft = v; ApplyState(); }, accentSoft)));
            subs.Add(this.GetResourceObservable("Foreman.Color.SelectionInactive").Subscribe(new ResourceSinks.BrushSink(v => { selectionInactive = v; ApplyState(); }, selectionInactive)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundHover").Subscribe(new ResourceSinks.BrushSink(v => { controlBackgroundHover = v; ApplyState(); }, controlBackgroundHover)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new ResourceSinks.BrushSink(v => { text = v; ApplyState(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextTertiary").Subscribe(new ResourceSinks.BrushSink(v => { textTertiary = v; ApplyState(); }, textTertiary)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentText").Subscribe(new ResourceSinks.BrushSink(v => { accentText = v; ApplyState(); }, accentText)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void UpdateRing() {
            if (frame is not null)
                Controls.FocusRing.SetShowAlways(frame, hasKeyboardFocus);
        }

        private void ApplyState() {
            if (fill is null || selectionFill is null)
                return;

            bool showSelectionFill = IsSelected && IsKey;
            fill.Background = IsSelected ? selectionInactive : controlBackgroundHover;
            fill.Opacity = showSelectionFill ? 0 : IsSelected || hovering ? 1 : 0;
            selectionFill.Background = accentSoft;
            selectionFill.Opacity = showSelectionFill ? 1 : 0;

            if (nameText is not null)
                nameText.Foreground = IsUnavailable ? textTertiary : IsSelected && IsKey ? accentText : text;
        }

        private void ApplyUnavailable() => PseudoClasses.Set(":unavailable", IsUnavailable);

        private static string FormatNodeCount(int count) =>
            count == 1 ? "1 node" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} nodes";

        private void ApplyMeta() {
            if (nodeCountText is not null)
                nodeCountText.Text = FormatNodeCount(NodeCount);
        }

        private void OnMenuHostClosed(object? sender, EventArgs e) => IsMenuOpen = false;

        private PlacementMode pendingPlacement = PlacementMode.BottomEdgeAlignedLeft;

        private void OnIsMenuOpenChanged(bool isOpen) {
            if (isOpen) {
                CopyMenuItems();
                Menu.HighlightedIndex = Menu.Items.Count > 0 ? 0 : -1;
            }
            if (menuHost is not null) {
                if (isOpen)
                    menuHost.Placement = pendingPlacement;
                menuHost.IsOpen = isOpen;
                pendingPlacement = PlacementMode.BottomEdgeAlignedLeft;
            }
        }

        private void OpenContextMenu(bool viaPointer) {
            pendingPlacement = viaPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft;
            IsMenuOpen = true;
        }

        public void Open() => OpenContextMenu(viaPointer: false);

        private void CopyMenuItems() {
            Menu.Items.Clear();
            foreach (Controls.MenuRow item in MenuItems)
                Menu.Items.Add(item);
        }

        private void ActivateOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

        private void ActivateRemove() {
            RemoveRequested?.Invoke(this, new RecentRowRemoveRequestedEventArgs(DataContext));
        }

        private void ApplyAutomation() {
            string phrase = $"{Label}, {PresetLabel}, {FormatNodeCount(NodeCount)}, {AbsoluteTime}";
            if (IsUnavailable)
                phrase += ", preset not installed";
            AutomationProperties.SetName(this, phrase);

            string actions = string.Join(". ", MenuItems.Where(i => !i.IsSeparator).Select(i => i.Text));
            string remove = RemoveRequested is null ? "" : " Remove: Backspace.";
            string hints = string.IsNullOrEmpty(HelpHints) ? "" : $" {HelpHints}";
            string helpText = $"{AbsoluteTime}. Open: Return or Command Down Arrow. {actions}.{remove}{hints} Shift F10 opens the context menu.";
            AutomationProperties.SetHelpText(this, helpText);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                OpenContextMenu(viaPointer: true);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            if (IsMenuOpen) {
                switch (e.Key) {
                    case Key.Down:
                        Menu.MoveHighlight(1);
                        e.Handled = true;
                        break;
                    case Key.Up:
                        Menu.MoveHighlight(-1);
                        e.Handled = true;
                        break;
                    case Key.Enter:
                    case Key.Space:
                        Menu.ActivateHighlighted();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        IsMenuOpen = false;
                        e.Handled = true;
                        break;
                }
                return;
            }

            switch (e.Key) {
                case Key.Return:
                    ActivateOpen();
                    e.Handled = true;
                    break;
                case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                    ActivateOpen();
                    e.Handled = true;
                    break;
                case Key.Back:
                    ActivateRemove();
                    e.Handled = true;
                    break;
                case Key.Apps:
                    OpenContextMenu(viaPointer: false);
                    e.Handled = true;
                    break;
                case Key.F10 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    OpenContextMenu(viaPointer: false);
                    e.Handled = true;
                    break;
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new RecentRowAutomationPeer(this);

        private sealed class RecentRowAutomationPeer(RecentRow owner) : ControlAutomationPeer(owner), IInvokeProvider {
            private RecentRow Row => (RecentRow)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

            void IInvokeProvider.Invoke() => Row.ActivateOpen();
        }
    }
}
