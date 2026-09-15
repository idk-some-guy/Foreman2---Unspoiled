using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using ProgressBarControl = Foreman.Mac.Kit.Controls.ProgressBar;
using SpinnerControl = Foreman.Mac.Kit.Controls.Spinner;

namespace Foreman.Mac.Kit.Composites {
    //Loading first so it is the default value - the one shape a bare `new StatePanel()` may carry without a button.
    public enum StatePanelShape { Loading, Empty, NoResults, Error }

    //A25's four shapes: glyph or spinner, headline, sentence, [progress], centered buttons - or one compact line under 196x260.
    public class StatePanel : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(StatePanel);

        public const double RegionMinWidth = 260;
        public const double RegionMinHeight = 196;

        public static readonly StyledProperty<StatePanelShape> ShapeProperty =
            AvaloniaProperty.Register<StatePanel, StatePanelShape>(nameof(Shape));

        public static readonly StyledProperty<string?> HeadlineProperty =
            AvaloniaProperty.Register<StatePanel, string?>(nameof(Headline));

        public static readonly StyledProperty<string?> SentenceProperty =
            AvaloniaProperty.Register<StatePanel, string?>(nameof(Sentence));

        public static readonly StyledProperty<ButtonRow?> ButtonsProperty =
            AvaloniaProperty.Register<StatePanel, ButtonRow?>(nameof(Buttons));

        public static readonly StyledProperty<double?> ProgressProperty =
            AvaloniaProperty.Register<StatePanel, double?>(nameof(Progress));

        public static readonly StyledProperty<bool> MovesFocusOnAppearProperty =
            AvaloniaProperty.Register<StatePanel, bool>(nameof(MovesFocusOnAppear));

        public static readonly DirectProperty<StatePanel, bool> IsCompactProperty =
            AvaloniaProperty.RegisterDirect<StatePanel, bool>(nameof(IsCompact), o => o.IsCompact);

        public StatePanelShape Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
        public string? Headline { get => GetValue(HeadlineProperty); set => SetValue(HeadlineProperty, value); }
        public string? Sentence { get => GetValue(SentenceProperty); set => SetValue(SentenceProperty, value); }
        public ButtonRow? Buttons { get => GetValue(ButtonsProperty); set => SetValue(ButtonsProperty, value); }
        public double? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

        //Opt-in: moves focus to the primary button on appear; left false, Return still activates it wherever focus already is.
        public bool MovesFocusOnAppear { get => GetValue(MovesFocusOnAppearProperty); set => SetValue(MovesFocusOnAppearProperty, value); }

        private bool isCompact;
        public bool IsCompact { get => isCompact; private set => SetAndRaise(IsCompactProperty, ref isCompact, value); }

        private StackPanel? fullLayout;
        private Grid? compactLayout;
        private Path? glyph;
        private SpinnerControl? spinner;
        private Path? compactGlyph;
        private Viewbox? compactGlyphViewbox;
        private SpinnerControl? compactSpinner;
        private TextBlock? headlineText;
        private ProgressBarControl? progressBar;
        private ContentPresenter? buttonsHost;
        private ContentPresenter? compactButtonsHost;
        private bool templateApplied;
        private bool isAttached;
        private readonly List<IDisposable> glyphSubs = [];

        static StatePanel() {
            ShapeProperty.Changed.AddClassHandler<StatePanel>((p, _) => p.OnShapeChanged());
            HeadlineProperty.Changed.AddClassHandler<StatePanel>((p, _) => p.ApplyHeadline());
            SentenceProperty.Changed.AddClassHandler<StatePanel>((p, _) => p.ApplySentence());
            ButtonsProperty.Changed.AddClassHandler<StatePanel>((p, _) => p.OnButtonsChanged());
            ProgressProperty.Changed.AddClassHandler<StatePanel>((p, _) => p.ApplyProgress());
        }

        public StatePanel() => AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            fullLayout = e.NameScope.Find<StackPanel>("PART_FullLayout");
            compactLayout = e.NameScope.Find<Grid>("PART_CompactLayout");
            glyph = e.NameScope.Find<Path>("PART_Glyph");
            spinner = e.NameScope.Find<SpinnerControl>("PART_Spinner");
            compactGlyph = e.NameScope.Find<Path>("PART_CompactGlyph");
            compactGlyphViewbox = e.NameScope.Find<Viewbox>("PART_CompactGlyphViewbox");
            compactSpinner = e.NameScope.Find<SpinnerControl>("PART_CompactSpinner");
            headlineText = e.NameScope.Find<TextBlock>("PART_Headline");
            progressBar = e.NameScope.Find<ProgressBarControl>("PART_Progress");
            buttonsHost = e.NameScope.Find<ContentPresenter>("PART_ButtonsHost");
            compactButtonsHost = e.NameScope.Find<ContentPresenter>("PART_CompactButtonsHost");

            templateApplied = true;
            ValidateButtons();
            ApplyShape();
            ApplyHeadline();
            ApplySentence();
            ApplyProgress();
            ApplyCompactLayout();
            ApplyButtonsHost();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            isAttached = true;
            //Re-creates the glyph geometry/size subscriptions torn down on the previous detach.
            ApplyShape();
            ApplyAppear();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            isAttached = false;
            foreach (IDisposable sub in glyphSubs)
                sub.Dispose();
            glyphSubs.Clear();
        }

        protected override Size MeasureOverride(Size availableSize) {
            bool compact = availableSize.Width < RegionMinWidth || availableSize.Height < RegionMinHeight;
            if (compact != IsCompact) {
                IsCompact = compact;
                ApplyCompactLayout();
                ApplyButtonsHost();
            }
            return base.MeasureOverride(availableSize);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new StatePanelAutomationPeer(this);

        private sealed class StatePanelAutomationPeer(StatePanel owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetLocalizedControlTypeCore() => "status";
            protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;
        }

        //Tunnel so Return activates the primary regardless of which control inside the panel has focus.
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && Buttons?.TryInvokeDefault() == true)
                e.Handled = true;
        }

        //Fires only on an actual Shape change - Loading, the enum default, never fires this on bare construction.
        private void OnShapeChanged() {
            if (templateApplied)
                ValidateButtons();
            ApplyShape();
            ApplyHeadline();
            ApplyProgress();
            //Restarts the cross-fade (and .loading delay) instead of letting the previous shape's transition finish.
            if (isAttached)
                ApplyAppear();
        }

        private void OnButtonsChanged() {
            if (templateApplied)
                ValidateButtons();
            ApplyButtonsHost();
        }

        //A non-loading shape with no button is a build failure, not a lint rule.
        private void ValidateButtons() {
            if (Shape == StatePanelShape.Loading)
                return;
            if ((Buttons?.Items.Count ?? 0) == 0)
                throw new InvalidOperationException($"Kit.StatePanel: .{Shape} refuses to build without a button.");
        }

        private void ApplyButtonsHost() {
            if (Buttons is { } row)
                row.IsCentered = true;
            if (buttonsHost is not null)
                buttonsHost.Content = IsCompact ? null : Buttons;
            if (compactButtonsHost is not null)
                compactButtonsHost.Content = IsCompact ? BuildCompactPrimaryButton() : null;
        }

        //One optional Kit.Button.plain for the primary, never the full row; proxies its click back to ButtonRow.
        private Button? BuildCompactPrimaryButton() {
            Button? source = Buttons?.ResolveDefaultButton();
            if (source is null)
                return null;
            Button button = new Button { Theme = source.Theme, Content = source.Content };
            button.Classes.Add("plain");
            button.Click += (_, _) => Buttons?.TryInvokeDefault();
            return button;
        }

        private void ApplyShape() {
            bool loading = Shape == StatePanelShape.Loading;
            PseudoClasses.Set(":loading", loading);
            PseudoClasses.Set(":error", Shape == StatePanelShape.Error);

            if (spinner is not null)
                spinner.IsVisible = loading;
            if (compactSpinner is not null)
                compactSpinner.IsVisible = loading;
            if (glyph is not null)
                glyph.IsVisible = !loading;
            if (compactGlyph is not null)
                compactGlyph.IsVisible = !loading;

            foreach (IDisposable sub in glyphSubs)
                sub.Dispose();
            glyphSubs.Clear();
            if (!loading && glyph is not null)
                glyphSubs.Add(this.GetResourceObservable(FullGlyphKey()).Subscribe(new ResourceSinks.Sink<Geometry?>(g => glyph.Data = g, null)));
            if (!loading && compactGlyph is not null) {
                glyphSubs.Add(this.GetResourceObservable(CompactGlyphKey()).Subscribe(new ResourceSinks.Sink<Geometry?>(g => compactGlyph.Data = g, null)));
                compactGlyph.Width = CompactGlyphMasterSize();
                compactGlyph.Height = CompactGlyphMasterSize();
                if (compactGlyphViewbox is not null)
                    glyphSubs.Add(this.GetResourceObservable(CompactGlyphSizeKey()).Subscribe(new ResourceSinks.DoubleSink(v => {
                        compactGlyphViewbox.Width = v;
                        compactGlyphViewbox.Height = v;
                    }, 0)));
            }
        }

        private string FullGlyphKey() => Shape switch {
            StatePanelShape.Empty => "Kit.Glyph.EmptyGraph.32",
            StatePanelShape.NoResults => "Kit.Glyph.NoResults.32",
            StatePanelShape.Error => "Kit.Glyph.Alert.32",
            _ => "Kit.Glyph.EmptyGraph.32",
        };

        //Alert has no 16pt master (the addendum draws it only at 12/32) - the compact error glyph reuses the existing 12pt asset.
        private string CompactGlyphKey() => Shape switch {
            StatePanelShape.Empty => "Kit.Glyph.EmptyGraph.16",
            StatePanelShape.NoResults => "Kit.Glyph.NoResults.16",
            StatePanelShape.Error => "Kit.Glyph.Alert.12",
            _ => "Kit.Glyph.EmptyGraph.16",
        };

        private double CompactGlyphMasterSize() => Shape == StatePanelShape.Error ? 12 : 16;

        private string CompactGlyphSizeKey() => Shape == StatePanelShape.Error ? "Foreman.Size.GlyphMicro" : "Foreman.Size.Glyph16";

        private void ApplyHeadline() {
            if (headlineText is null)
                return;
            headlineText.Text = Headline;
            AutomationProperties.SetName(this, Headline);
        }

        private void ApplySentence() => AutomationProperties.SetHelpText(this, Sentence);

        //.loading only, and only a determinate bar - an unknown total shows the spinner alone, never an indeterminate bar alongside it.
        private void ApplyProgress() {
            if (progressBar is null)
                return;
            bool show = Shape == StatePanelShape.Loading && Progress.HasValue;
            progressBar.IsVisible = show;
            if (show) {
                progressBar.IsIndeterminate = false;
                progressBar.Value = Progress!.Value;
            }
        }

        private void ApplyCompactLayout() {
            if (fullLayout is not null)
                fullLayout.IsVisible = !IsCompact;
            if (compactLayout is not null)
                compactLayout.IsVisible = IsCompact;
            PseudoClasses.Set(":compact", IsCompact);
        }

        //Fades in on the cross-fade clock; .loading also waits StatePanelDelay so a fast solve never flashes one.
        private void ApplyAppear() {
            TimeSpan delay = Shape == StatePanelShape.Loading ? Motion.StatePanelDelay : TimeSpan.Zero;
            Opacity = 0;
            Transitions = Motion.Transition(TransitionKind.CrossFade, Motion.CrossFadeDuration, delay: delay);
            Opacity = 1;
            if (MovesFocusOnAppear)
                Dispatcher.UIThread.Post(FocusPrimary, DispatcherPriority.Loaded);
        }

        private void FocusPrimary() => Buttons?.ResolveDefaultButton()?.Focus();
    }
}
