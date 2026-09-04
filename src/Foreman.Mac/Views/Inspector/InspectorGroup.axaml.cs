using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

namespace Foreman.Mac.Views.Inspector {
    //Collapsible section shell for the inspector's segment-specific content (task 4/5 recipe/flow panels);
    //this task only ships the shell itself, unused until those tasks nest content inside SegmentHost.
    public partial class InspectorGroup : UserControl {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<InspectorGroup, string?>(nameof(Title));

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<InspectorGroup, bool>(nameof(IsExpanded), defaultValue: true);

        public static readonly StyledProperty<object?> GroupContentProperty =
            AvaloniaProperty.Register<InspectorGroup, object?>(nameof(GroupContent));

        public string? Title {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public bool IsExpanded {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        [Content]
        public object? GroupContent {
            get => GetValue(GroupContentProperty);
            set => SetValue(GroupContentProperty, value);
        }

        public InspectorGroup() => InitializeComponent();

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
