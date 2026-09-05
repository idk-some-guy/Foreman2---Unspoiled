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

        //Task 9 (contract §03): Rate/Fuel/Node are plain sections with no chevron and no toggle - only
        //Assembler/Modules/Beacon collapse. Defaults true so every existing collapsible group (Beacon,
        //InspectorGroupTests' own fixture) needs no XAML change.
        public static readonly StyledProperty<bool> IsCollapsibleProperty =
            AvaloniaProperty.Register<InspectorGroup, bool>(nameof(IsCollapsible), defaultValue: true);

        public static readonly StyledProperty<object?> GroupContentProperty =
            AvaloniaProperty.Register<InspectorGroup, object?>(nameof(GroupContent));

        //Fix round 1 (reviewer finding, Critical 1): the group padding and per-group bottom border the
        //brief called for never actually landed on this control - every group rendered flush against its
        //neighbours. IsLastGroup lets the host StackPanel mark whichever group is visually last (Beacon in
        //RecipeSegmentView, Node in FlowSegmentView) so it skips the border, matching the mock's own
        //`border-bottom:none` on the final group.
        public static readonly StyledProperty<bool> IsLastGroupProperty =
            AvaloniaProperty.Register<InspectorGroup, bool>(nameof(IsLastGroup), defaultValue: false);

        private readonly TextBlock titleText;
        private readonly TextBlock disclosureGlyph;
        private readonly Button headerButton;
        private readonly ContentPresenter contentHost;
        private readonly Border container;

        public string? Title {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public bool IsExpanded {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public bool IsCollapsible {
            get => GetValue(IsCollapsibleProperty);
            set => SetValue(IsCollapsibleProperty, value);
        }

        public bool IsLastGroup {
            get => GetValue(IsLastGroupProperty);
            set => SetValue(IsLastGroupProperty, value);
        }

        [Content]
        public object? GroupContent {
            get => GetValue(GroupContentProperty);
            set => SetValue(GroupContentProperty, value);
        }

        //Title row is a real Button - pointer press/release and Enter/Space (when focused) both fire
        //Click for free, toggling IsExpanded the same way. A non-collapsible group keeps the same Button
        //(so every other part of the header stays identical) but with hit-testing and focus turned off,
        //so it never shows a pointerover tint or takes a click.
        public InspectorGroup() {
            titleText = new TextBlock();
            titleText.Classes.Add("foreman-section-header");
            disclosureGlyph = new TextBlock { Name = "DisclosureGlyph", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.5 };
            disclosureGlyph.Classes.Add("foreman-caption");
            var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(disclosureGlyph, 1);
            headerRow.Children.Add(titleText);
            headerRow.Children.Add(disclosureGlyph);
            headerButton = new Button {
                Name = "GroupHeader", Content = headerRow,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            headerButton.Classes.Add("foreman-group-header");
            headerButton.Click += (_, _) => SetCurrentValue(IsExpandedProperty, !IsExpanded);
            contentHost = new ContentPresenter();
            container = new Border { Child = new StackPanel { Spacing = 4, Children = { headerButton, contentHost } } };
            container.Classes.Add("foreman-inspector-group");
            Content = container;
            DataContextChanged += (_, _) => SyncGroupContentDataContext();
            UpdateDisclosureGlyph();
            UpdateCollapsible();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TitleProperty)
                titleText.Text = Title?.ToUpperInvariant();
            else if (change.Property == IsExpandedProperty) {
                contentHost.IsVisible = IsExpanded;
                UpdateDisclosureGlyph();
            } else if (change.Property == GroupContentProperty) {
                contentHost.Content = GroupContent;
                SyncGroupContentDataContext();
            } else if (change.Property == IsCollapsibleProperty)
                UpdateCollapsible();
            else if (change.Property == IsLastGroupProperty)
                container.Classes.Set("last", IsLastGroup);
        }

        private void UpdateCollapsible() {
            disclosureGlyph.IsVisible = IsCollapsible;
            headerButton.IsHitTestVisible = IsCollapsible;
            headerButton.Focusable = IsCollapsible;
            if (!IsCollapsible)
                SetCurrentValue(IsExpandedProperty, true);
        }

        private void UpdateDisclosureGlyph() => disclosureGlyph.Text = IsExpanded ? "⌄" : "⌃";

        private void SyncGroupContentDataContext() {
            if (GroupContent is Control content)
                content.DataContext = DataContext;
        }
    }
}
