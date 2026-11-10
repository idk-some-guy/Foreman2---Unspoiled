using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Spinner = Foreman.Mac.Kit.Controls.Spinner;
using Foreman.Models.Research;
using System;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchSourceBar : Border {
        private readonly Border box = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        private readonly Path glyph = new() { StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock nameText = new() { Classes = { "headline" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock metaText = new() { Classes = { "caption", "secondary" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock warningText = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly TextBlock editsLabel = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel editsRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
        private readonly Spinner spinner = new() { Classes = { "sm" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };

        public Button ChooseButton { get; }
        public Button SyncButton { get; }
        public Button RevertAllButton { get; }

        public string NameText => nameText.Text ?? "";
        public string MetaText => metaText.Text ?? "";
        public string? EditsText => editsRow.IsVisible ? editsLabel.Text : null;
        public string? WarningText => warningText.IsVisible ? warningText.Text : null;
        public bool IsSyncing => spinner.IsVisible;

        public ResearchSourceBar() {
            Padding = new Thickness(16, 12);
            BorderThickness = new Thickness(0, 0, 0, 1);
            LiveBrush.Bind(this, BackgroundProperty, "Foreman.Color.WindowBackground");
            LiveBrush.Bind(this, BorderBrushProperty, "Foreman.Color.Border");
            AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);
            AutomationProperties.SetName(this, "Source");

            foreach (TextBlock text in new[] { nameText, metaText, warningText, editsLabel })
                text.Theme = KitTheme("Kit.Text");
            metaText.FontFeatures = FontFeatureCollection.Parse("tnum");
            glyph.Theme = KitTheme("Kit.Glyph");
            glyph.Data = Application.Current?.TryGetResource("Kit.Glyph.Research.17", null, out object? geometry) == true ? geometry as Geometry : null;
            glyph.Stretch = Stretch.None;
            box.Child = glyph;
            LiveBrush.Bind(warningText, TextBlock.ForegroundProperty, "Foreman.Color.Warning");
            LiveBrush.Bind(editsLabel, TextBlock.ForegroundProperty, "Foreman.Color.AccentText");

            ChooseButton = NewButton("Choose Save…");
            SyncButton = NewButton("Sync");
            RevertAllButton = NewMiniButton("Revert All");
            AutomationProperties.SetName(RevertAllButton, "Revert All Edits");

            editsRow.Children.Add(editsLabel);
            editsRow.Children.Add(RevertAllButton);
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { spinner, metaText } };
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameText, metaRow, warningText, editsRow } };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { ChooseButton, SyncButton } };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            Grid.SetColumn(column, 1);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(box);
            grid.Children.Add(buttons);
            grid.Children.Add(column);
            Child = grid;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"ResearchSourceBar: kit theme '{key}' not found.");
        }

        private static Button NewButton(string label) => new() { Theme = KitTheme("Kit.Button"), Content = label };

        internal static Button NewMiniButton(string label) {
            var button = NewButton(label);
            button.Height = 18;
            button.Padding = new Thickness(8, 0);
            button.FontWeight = FontWeight.Medium;
            button.Bind(TemplatedControl.FontSizeProperty, new DynamicResourceExtension("Foreman.Type.Caption.FontSize"));
            return button;
        }

        public void Update(ResearchState state, bool syncing, bool missing, DateTime nowLocal) {
            ResearchSource? source = state.Source;
            int edits = state.HandEditCount;
            bool synced = source is not null;

            nameText.Text = source?.SaveName ?? "Not synced";
            ToolTip.SetTip(nameText, source?.SaveName);
            LiveBrush.Bind(box, Border.BackgroundProperty, synced ? "Foreman.Color.ControlBackground" : "Foreman.Color.WellBackground");
            LiveBrush.Bind(box, Border.BorderBrushProperty, "Foreman.Color.BorderStrong");
            LiveBrush.Bind(glyph, Shape.StrokeProperty, synced ? "Foreman.Color.TextSecondary" : "Foreman.Color.TextTertiary");

            string? when = source is null ? null : ResearchText.SyncedWhen(source.SyncedAtUtc, nowLocal);
            string? played = source is null ? null : ResearchText.PlayTime(source.TicksPlayed);
            spinner.IsVisible = syncing;
            if (syncing)
                metaText.Text = source is null ? "Syncing…" : $"Syncing from “{source.SaveName}”…";
            else if (source is null)
                metaText.Text = "Sync from a save to read this graph’s research.";
            else
                metaText.Text = played is null ? $"synced {when}" : $"{played} played · synced {when}";

            bool showMissing = missing && synced && !syncing;
            warningText.IsVisible = showMissing;
            warningText.Text = showMissing ? "This save was moved or deleted. Choose it again to sync." : null;

            editsRow.IsVisible = edits > 0;
            RevertAllButton.IsVisible = edits > 0;
            editsLabel.Text = edits == 1 ? "1 value edited by hand" : $"{edits} values edited by hand";

            ChooseButton.IsEnabled = !syncing;
            SyncButton.IsEnabled = synced && !missing && !syncing;
            RevertAllButton.IsEnabled = !syncing;
            AutomationProperties.SetHelpText(RevertAllButton, edits == 1 ? "Sets 1 value back to the save." : $"Sets {edits} values back to the save.");
            AutomationProperties.SetHelpText(this, SpokenLabel(source, when, edits, showMissing));
        }

        private static string SpokenLabel(ResearchSource? source, string? when, int edits, bool missing) {
            string label;
            if (source is null) {
                label = "Not synced";
            } else {
                string? played = ResearchText.PlayTimeSpoken(source.TicksPlayed);
                label = played is null ? $"{source.SaveName}, synced {when}" : $"{source.SaveName}, {played} played, synced {when}";
            }
            if (edits > 0)
                label += edits == 1 ? ", 1 value edited by hand" : $", {edits} values edited by hand";
            return missing ? label + ", save moved or deleted" : label;
        }
    }
}
