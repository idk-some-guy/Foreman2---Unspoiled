using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    public class FormGroup : ItemsControl {
        protected override Type StyleKeyOverride => typeof(FormGroup);

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<FormGroup, string?>(nameof(Title));

        public static readonly StyledProperty<bool> IsBoxedProperty =
            AvaloniaProperty.Register<FormGroup, bool>(nameof(IsBoxed));

        public static readonly StyledProperty<double?> TrailingGapProperty =
            AvaloniaProperty.Register<FormGroup, double?>(nameof(TrailingGap));

        public string? Title {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public bool IsBoxed {
            get => GetValue(IsBoxedProperty);
            set => SetValue(IsBoxedProperty, value);
        }

        public double? TrailingGap {
            get => GetValue(TrailingGapProperty);
            set => SetValue(TrailingGapProperty, value);
        }

        private double groupGap = 16;
        private readonly List<IDisposable> subs = [];

        static FormGroup() {
            IsBoxedProperty.Changed.AddClassHandler<FormGroup>((g, _) => g.PseudoClasses.Set(":boxed", g.IsBoxed));
            TrailingGapProperty.Changed.AddClassHandler<FormGroup>((g, _) => g.ApplyMargin());
        }

        public FormGroup() => ApplyMargin();

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            //Group-to-group gap is fixed at every scale step (not in ScaleContract.MultipliedKeys), same as the row gap it shares a token with.
            subs.Add(this.GetResourceObservable("Foreman.Space.Space4").Subscribe(new ResourceSinks.DoubleSink(v => {
                groupGap = v;
                ApplyMargin();
            }, groupGap)));
        }

        private void ApplyMargin() => Margin = new Thickness(0, 0, 0, TrailingGap ?? groupGap);

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }
    }
}
