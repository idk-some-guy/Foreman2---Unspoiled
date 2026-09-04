using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
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
        private readonly TextBlock disclosureGlyph;
        private readonly Button headerButton;
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

        //Title row is a real Button - pointer press/release and Enter/Space (when focused) both fire
        //Click for free, toggling IsExpanded the same way.
        public InspectorGroup() {
            titleText = new TextBlock();
            titleText.Classes.Add("foreman-section-header");
            disclosureGlyph = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            disclosureGlyph.Classes.Add("foreman-caption");
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { disclosureGlyph, titleText } };
            headerButton = new Button { Name = "GroupHeader", Content = headerRow, HorizontalAlignment = HorizontalAlignment.Left };
            headerButton.Classes.Add("foreman-group-header");
            headerButton.Click += (_, _) => IsExpanded = !IsExpanded;
            contentHost = new ContentPresenter();
            Content = new StackPanel { Spacing = 4, Children = { headerButton, contentHost } };
            DataContextChanged += (_, _) => SyncGroupContentDataContext();
            UpdateDisclosureGlyph();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TitleProperty)
                titleText.Text = Title;
            else if (change.Property == IsExpandedProperty) {
                contentHost.IsVisible = IsExpanded;
                UpdateDisclosureGlyph();
            } else if (change.Property == GroupContentProperty) {
                contentHost.Content = GroupContent;
                SyncGroupContentDataContext();
            }
        }

        private void UpdateDisclosureGlyph() => disclosureGlyph.Text = IsExpanded ? "▾" : "▸";

        private void SyncGroupContentDataContext() {
            if (GroupContent is Control content)
                content.DataContext = DataContext;
        }
    }
}
