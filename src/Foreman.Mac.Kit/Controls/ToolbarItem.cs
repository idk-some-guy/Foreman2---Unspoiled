using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System;
using System.Collections.Specialized;

namespace Foreman.Mac.Kit.Controls {
    public class ToolbarItem : Button {
        public static readonly StyledProperty<Geometry?> GlyphProperty =
            AvaloniaProperty.Register<ToolbarItem, Geometry?>(nameof(Glyph));

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<ToolbarItem, string?>(nameof(Label));

        public static readonly StyledProperty<bool> IsOnProperty =
            AvaloniaProperty.Register<ToolbarItem, bool>(nameof(IsOn));

        public static readonly StyledProperty<string?> AutomationNameProperty =
            AvaloniaProperty.Register<ToolbarItem, string?>(nameof(AutomationName));

        public static readonly StyledProperty<string?> OverflowLabelProperty =
            AvaloniaProperty.Register<ToolbarItem, string?>(nameof(OverflowLabel));

        private TextBlock? label;
        private IDisposable? labelOpacitySub;
        private NotifyCollectionChangedEventHandler? classesChanged;

        internal int LabelVisibilityApplyCount { get; private set; }
        internal bool HasLiveLabelOpacitySubscription => labelOpacitySub is not null;

        static ToolbarItem() {
            IsOnProperty.Changed.AddClassHandler<ToolbarItem>((item, e) => item.PseudoClasses.Set(":on", (bool)e.NewValue!));
            AutomationNameProperty.Changed.AddClassHandler<ToolbarItem>((item, _) => item.SyncAutomationName());
            LabelProperty.Changed.AddClassHandler<ToolbarItem>((item, _) => item.SyncAutomationName());
        }

        public Geometry? Glyph {
            get => GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string? OverflowLabel {
            get => GetValue(OverflowLabelProperty);
            set => SetValue(OverflowLabelProperty, value);
        }

        public bool IsOn {
            get => GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        public string? AutomationName {
            get => GetValue(AutomationNameProperty);
            set => SetValue(AutomationNameProperty, value);
        }

        private void SyncAutomationName() => AutomationProperties.SetName(this, AutomationName ?? Label);

        internal void PerformClick() => OnClick();

        protected override AutomationPeer OnCreateAutomationPeer() => new ToolbarItemAutomationPeer(this);

        private sealed class ToolbarItemAutomationPeer(ToolbarItem owner) : ControlAutomationPeer(owner), IInvokeProvider, IToggleProvider {
            private ToolbarItem Item => (ToolbarItem)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            void IInvokeProvider.Invoke() => Item.PerformClick();

            ToggleState IToggleProvider.ToggleState => Item.IsOn ? ToggleState.On : ToggleState.Off;
            void IToggleProvider.Toggle() { }
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            label = e.NameScope.Find<TextBlock>("PART_Label");
            if (label is not null)
                label.Transitions = Motion.Transition(TransitionKind.CrossFade, Motion.TabLabelCrossFadeDuration);
            ApplyLabelVisibility();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            classesChanged ??= (_, _) => ApplyLabelVisibility();
            Classes.CollectionChanged += classesChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            if (classesChanged is not null)
                Classes.CollectionChanged -= classesChanged;
            labelOpacitySub?.Dispose();
            labelOpacitySub = null;
        }

        private void ApplyLabelVisibility() {
            if (label is null)
                return;
            LabelVisibilityApplyCount++;
            bool iconOnly = Classes.Contains("icon-only");
            labelOpacitySub?.Dispose();
            labelOpacitySub = null;

            if (!iconOnly) {
                label.IsVisible = true;
            } else if (label.Opacity <= 0) {
                label.IsVisible = false;
            } else {
                labelOpacitySub = label.GetObservable(OpacityProperty).Subscribe(new OpacitySink(v => {
                    if (v > 0)
                        return;
                    label.IsVisible = false;
                    labelOpacitySub?.Dispose();
                    labelOpacitySub = null;
                }));
            }
        }

        private sealed class OpacitySink(Action<double> onNext) : IObserver<double> {
            public void OnNext(double value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
