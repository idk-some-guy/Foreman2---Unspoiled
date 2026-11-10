using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using System;

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

            ApplyRotation();
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

    }
}
