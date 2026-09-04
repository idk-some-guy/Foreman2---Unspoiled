using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Metadata;

namespace Foreman.Mac.Views.Inspector {
    //Built entirely in code, not loaded from an .axaml body: GroupContent's [Content] attribute makes any
    //XAML document instantiating this type route its nested children into GroupContent - including this
    //control's own document, if it had one, which would swallow its own title/host markup into GroupContent
    //instead of ever displaying it. No .axaml body sidesteps that entirely.
    public sealed class InspectorGroup : ContentControl {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<InspectorGroup, string?>(nameof(Title));

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<InspectorGroup, bool>(nameof(IsExpanded), defaultValue: true);

        public static readonly StyledProperty<object?> GroupContentProperty =
            AvaloniaProperty.Register<InspectorGroup, object?>(nameof(GroupContent));

        private readonly TextBlock titleText;
        private readonly ContentPresenter contentHost;

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

        public InspectorGroup() {
            titleText = new TextBlock();
            titleText.Classes.Add("foreman-section-header");
            contentHost = new ContentPresenter();
            Content = new StackPanel { Spacing = 4, Children = { titleText, contentHost } };
            DataContextChanged += (_, _) => SyncGroupContentDataContext();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TitleProperty)
                titleText.Text = Title;
            else if (change.Property == IsExpandedProperty)
                contentHost.IsVisible = IsExpanded;
            else if (change.Property == GroupContentProperty) {
                contentHost.Content = GroupContent;
                SyncGroupContentDataContext();
            }
        }

        private void SyncGroupContentDataContext() {
            if (GroupContent is Control content)
                content.DataContext = DataContext;
        }
    }
}
