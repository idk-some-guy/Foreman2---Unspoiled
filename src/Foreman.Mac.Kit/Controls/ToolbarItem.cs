using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Foreman.Mac.Kit;
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

        private TextBlock? label;
        private IDisposable? labelOpacitySub;
        private NotifyCollectionChangedEventHandler? classesChanged;

        //Test-only visibility into subscription lifetime (mirrors Segmented's counter properties).
        internal int LabelVisibilityApplyCount { get; private set; }
        internal bool HasLiveLabelOpacitySubscription => labelOpacitySub is not null;

        static ToolbarItem() {
            IsOnProperty.Changed.AddClassHandler<ToolbarItem>((item, e) => item.PseudoClasses.Set(":on", (bool)e.NewValue!));
        }

        public Geometry? Glyph {
            get => GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public bool IsOn {
            get => GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
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

        //The icon-only style fades PART_Label's Opacity; IsVisible only flips to false once that fade actually reaches zero (never in the same pass as Opacity 0).
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
