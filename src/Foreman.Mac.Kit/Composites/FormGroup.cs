using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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

        public static readonly StyledProperty<object?> StateContentProperty =
            AvaloniaProperty.Register<FormGroup, object?>(nameof(StateContent));

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

        public object? StateContent {
            get => GetValue(StateContentProperty);
            set => SetValue(StateContentProperty, value);
        }

        private double groupGap = 16;
        private readonly List<IDisposable> subs = [];
        private ItemsPresenter? itemsPresenter;
        private ContentPresenter? statePresenter;

        static FormGroup() {
            IsBoxedProperty.Changed.AddClassHandler<FormGroup>((g, _) => g.PseudoClasses.Set(":boxed", g.IsBoxed));
            TrailingGapProperty.Changed.AddClassHandler<FormGroup>((g, _) => g.ApplyMargin());
            StateContentProperty.Changed.AddClassHandler<FormGroup>((g, _) => g.ApplyStateContent());
        }

        public FormGroup() => ApplyMargin();

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            itemsPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
            statePresenter = e.NameScope.Find<ContentPresenter>("PART_StateHost");
            ApplyStateContent();
        }

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
            subs.Add(this.GetResourceObservable("Foreman.Space.Space4").Subscribe(new ResourceSinks.DoubleSink(v => {
                groupGap = v;
                ApplyMargin();
            }, groupGap)));
        }

        private void ApplyMargin() => Margin = new Thickness(0, 0, 0, TrailingGap ?? groupGap);

        private void ApplyStateContent() {
            bool hasState = StateContent is not null;
            if (itemsPresenter is not null)
                itemsPresenter.IsVisible = !hasState;
            if (statePresenter is not null) {
                statePresenter.Content = StateContent;
                statePresenter.IsVisible = hasState;
            }
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }
    }
}
