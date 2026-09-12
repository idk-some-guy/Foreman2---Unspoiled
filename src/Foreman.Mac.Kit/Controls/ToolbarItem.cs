using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Foreman.Mac.Kit.Controls {
    public class ToolbarItem : Button {
        public static readonly StyledProperty<Geometry?> GlyphProperty =
            AvaloniaProperty.Register<ToolbarItem, Geometry?>(nameof(Glyph));

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<ToolbarItem, string?>(nameof(Label));

        public static readonly StyledProperty<bool> IsOnProperty =
            AvaloniaProperty.Register<ToolbarItem, bool>(nameof(IsOn));

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
    }
}
