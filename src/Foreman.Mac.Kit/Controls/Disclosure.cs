using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class Disclosure : HeaderedContentControl {
        protected override Type StyleKeyOverride => typeof(Disclosure);

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<Disclosure, bool>(nameof(IsExpanded), true);

        public bool IsExpanded {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        private Border? headerRow;
        private Path? triangle;
        private EventHandler<PointerPressedEventArgs>? headerPressed;
        private EventHandler<KeyEventArgs>? headerKeyDown;
        private double glyphMicro = 12;
        private readonly List<IDisposable> subs = [];

        static Disclosure() {
            IsExpandedProperty.Changed.AddClassHandler<Disclosure>((d, _) => d.ApplyRotation());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (headerRow is not null) {
                if (headerPressed is not null)
                    headerRow.PointerPressed -= headerPressed;
                if (headerKeyDown is not null)
                    headerRow.KeyDown -= headerKeyDown;
            }

            headerRow = e.NameScope.Find<Border>("PART_HeaderRow");
            triangle = e.NameScope.Find<Path>("PART_Triangle");

            if (headerRow is not null) {
                headerPressed = (_, _) => { if (IsEnabled) IsExpanded = !IsExpanded; };
                headerKeyDown = (_, args) => HandleHeaderKey(args);
                headerRow.PointerPressed += headerPressed;
                headerRow.KeyDown += headerKeyDown;
            }

            ApplyGlyphSize();
            ApplyRotation();
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
            subs.Add(this.GetResourceObservable("Foreman.Size.GlyphMicro").Subscribe(new Sink(v => { glyphMicro = v; ApplyGlyphSize(); })));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyGlyphSize() {
            if (triangle is null)
                return;
            triangle.Width = glyphMicro;
            triangle.Height = glyphMicro;
        }

        private void HandleHeaderKey(KeyEventArgs e) {
            switch (e.Key) {
                case Key.Space:
                case Key.Enter:
                    IsExpanded = !IsExpanded;
                    e.Handled = true;
                    break;
                case Key.Left:
                    IsExpanded = false;
                    e.Handled = true;
                    break;
                case Key.Right:
                    IsExpanded = true;
                    e.Handled = true;
                    break;
            }
        }

        private void ApplyRotation() {
            if (triangle is null)
                return;
            triangle.RenderTransform = TransformOperations.Parse(IsExpanded ? "rotate(90deg)" : "rotate(0deg)");
        }

        private sealed class Sink(Action<double> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value is double d ? d : 12.0);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
