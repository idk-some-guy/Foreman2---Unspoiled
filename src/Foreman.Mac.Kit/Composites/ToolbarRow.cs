using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Styling;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    //The unified titlebar row (K3 composites addendum §08): title · groups · flexible gap · search · zoom; collapses as width shrinks, title and zoom never collapse.
    public class ToolbarRow : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ToolbarRow);

        //2pt within a group (Space.Space1 4 minus 2) - fixed at every scale step, like the row's 16pt group spacer.
        private const double GroupItemGap = 2;

        private static readonly Dictionary<ToolbarGroupRole, string> GroupNames = new() {
            [ToolbarGroupRole.Document] = "Document",
            [ToolbarGroupRole.Add] = "Add",
            [ToolbarGroupRole.Arrange] = "Arrange",
            [ToolbarGroupRole.ShareView] = "Share",
        };

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<ToolbarRow, string?>(nameof(Title));

        public string? Title {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public AvaloniaList<ToolbarGroup> Groups { get; } = [];

        public SearchField SearchField { get; } = new();
        public ZoomStepper ZoomStepper { get; } = new();
        public MenuButton OverflowButton { get; } = new() { Classes = { "overflow" } };

        private readonly TextBlock titleText = new() { Classes = { "headline" }, VerticalAlignment = VerticalAlignment.Center };
        private ToolbarRowPanel? panel;
        private List<StackPanel> previousGroupPanels = [];

        static ToolbarRow() {
            TitleProperty.Changed.AddClassHandler<ToolbarRow>((row, _) => row.titleText.Text = row.Title);
        }

        public ToolbarRow() {
            titleText.Text = Title;
            Groups.CollectionChanged += (_, _) => Rebuild();
            AutomationProperties.SetName(this, "Toolbar");
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            panel = e.NameScope.Find<ToolbarRowPanel>("PART_Panel");

            ApplyTheme("Kit.Text", titleText);
            ApplyTheme("Kit.SearchField", SearchField);
            ApplyTheme("Kit.ZoomStepper", ZoomStepper);
            ApplyTheme("Kit.MenuButton", OverflowButton);

            Rebuild();
        }

        //Composed children resolve their own theme from this control's resource scope, once attached - never a one-shot constructor read.
        private void ApplyTheme(string key, StyledElement target) {
            if (target.Theme is null && this.TryFindResource(key, null, out object? theme) && theme is ControlTheme controlTheme)
                target.Theme = controlTheme;
        }

        private void Rebuild() {
            if (panel is null)
                return;

            foreach (StackPanel previous in previousGroupPanels)
                previous.Children.Clear();

            List<(Control, ToolbarGapKind)> content = [(titleText, ToolbarGapKind.None)];
            List<ToolbarItem> allItems = [];
            List<StackPanel> groupPanels = [];
            StackPanel? overflowGroupPanel = null;

            for (int i = 0; i < Groups.Count; i++) {
                ToolbarGroup group = Groups[i];
                allItems.AddRange(group.Items);

                var groupPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupItemGap };
                AutomationProperties.SetName(groupPanel, GroupNames.GetValueOrDefault(group.Role, group.Role.ToString()));
                foreach (ToolbarItem item in group.Items) {
                    AutomationProperties.SetName(item, item.Label);
                    groupPanel.Children.Add(item);
                }
                groupPanels.Add(groupPanel);
                content.Add((groupPanel, ToolbarGapKind.Group));

                if (i == Groups.Count - 1) {
                    overflowGroupPanel = groupPanel;
                    PopulateOverflow(group);
                    content.Add((OverflowButton, ToolbarGapKind.Group));
                }
            }

            content.Add((SearchField, ToolbarGapKind.Flex));
            content.Add((ZoomStepper, ToolbarGapKind.Group));

            panel.SetContent(content, allItems, groupPanels, overflowGroupPanel, Groups.Count > 0 ? OverflowButton : null, SearchField);
            previousGroupPanels = groupPanels;
        }

        //The trailing group's own commands, mirrored as plain overflow actions (K3 composites addendum §08: "the least-used trailing group moves into a Kit.MenuButton overflow").
        private void PopulateOverflow(ToolbarGroup group) {
            OverflowButton.ActionItems.Clear();
            foreach (ToolbarItem item in group.Items) {
                ToolbarItem captured = item;
                OverflowButton.ActionItems.Add(new MenuButtonAction {
                    Text = captured.Label ?? "",
                    IsEnabled = captured.IsEnabled,
                    Activate = () => {
                        if (captured.Command?.CanExecute(captured.CommandParameter) == true)
                            captured.Command.Execute(captured.CommandParameter);
                    },
                });
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ToolbarRowAutomationPeer(this);

        private sealed class ToolbarRowAutomationPeer(ToolbarRow owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetNameCore() => "Toolbar";
        }
    }
}
