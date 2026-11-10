using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchBonusRow {
        private readonly ResearchViewModel viewModel;

        public ResearchBonusRow(ResearchViewModel viewModel, ResearchField field, bool isStored) {
            this.viewModel = viewModel;
            Field = field;
            IsStored = isStored;
        }

        public ResearchField Field { get; }
        public bool IsStored { get; }
        public bool IsApplying { get; set; }
        public required FormRow Row { get; init; }
        public required Stepper Stepper { get; init; }
        public required Ellipse Dot { get; init; }
        public required RevertButton Revert { get; init; }

        public string? Label => Row.Label;
        public string? Note => Row.Note;
        public bool IsEdited => viewModel.State.IsHandEdited(Field);

        public TextBlock LabelText => Row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Label");
    }

    internal sealed class ResearchRecipeProductivityRow {
        public required FormRow Row { get; init; }
        public required Control Content { get; init; }
        public required TextBlock CountText { get; init; }
        public required Button ShowRecipesButton { get; init; }

        public string Text => $"{Row.Label}: {CountText.Text}";
    }

    internal sealed record ResearchGroupParts(string Title, string? Caption, IReadOnlyList<string> Labels);

    internal sealed class ResearchBonusesView : StackPanel {
        private const string NotAppliedYet = "Not applied yet";

        private static readonly (string Title, bool IsStored, ResearchField[] Fields)[] Layout = [
            ("Production", false, [ResearchField.MiningProductivity]),
            ("Labs", true, [ResearchField.LabSpeed, ResearchField.LabProductivity]),
            ("Inserters and belts", true, [ResearchField.InserterStackBonus, ResearchField.BulkInserterCapacityBonus, ResearchField.BeltStackBonus]),
            ("Robots", true, [ResearchField.RobotSpeed, ResearchField.RobotCargo, ResearchField.RobotBattery]),
            ("Trains", true, [ResearchField.TrainBrakingForce]),
        ];

        private readonly ResearchViewModel viewModel;
        private readonly Action showRecipes;
        private readonly Dictionary<ResearchField, ResearchBonusRow> rows = [];
        private readonly List<ResearchGroupParts> groups = [];

        public ResearchBonusesView(ResearchViewModel viewModel, Action showRecipes) {
            this.viewModel = viewModel;
            this.showRecipes = showRecipes;
            Build();
        }

        public event EventHandler? Changed;

        public IReadOnlyList<ResearchGroupParts> Groups => groups;
        public ResearchRecipeProductivityRow RecipeProductivityRow { get; private set; } = null!;

        public ResearchBonusRow BonusRow(ResearchField field) => rows[field];

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"ResearchBonusesView: kit theme '{key}' not found.");
        }

        private void Build() {
            for (int i = 0; i < Layout.Length; i++) {
                (string title, bool isStored, ResearchField[] fields) = Layout[i];
                var items = new List<Control>();
                var labels = new List<string>();
                foreach (ResearchField field in fields) {
                    ResearchBonusRow row = BuildBonusRow(field, isStored);
                    rows[field] = row;
                    items.Add(row.Row);
                    labels.Add(ResearchText.FieldLabel(field));
                    if (field == ResearchField.MiningProductivity) {
                        RecipeProductivityRow = BuildRecipeRow();
                        items.Add(RecipeProductivityRow.Row);
                        labels.Add(RecipeProductivityRow.Row.Label!);
                    }
                }
                groups.Add(new ResearchGroupParts(title, isStored ? NotAppliedYet : null, labels));
                Children.Add(BuildGroup(i, title, isStored ? NotAppliedYet : null, items));
            }
            Refresh();
        }

        private static StackPanel BuildGroup(int index, string title, string? caption, List<Control> items) {
            var titleText = new TextBlock { Theme = KitTheme("Kit.Text"), Text = title, VerticalAlignment = VerticalAlignment.Center };
            titleText.Bind(TextBlock.FontSizeProperty, titleText.GetResourceObservable("Foreman.Type.SectionHeader.FontSize"));
            titleText.Bind(TextBlock.FontWeightProperty, titleText.GetResourceObservable("Foreman.Type.SectionHeader.FontWeight"));
            LiveBrush.Bind(titleText, TextBlock.ForegroundProperty, "Foreman.Color.TextSecondary");
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 8), Children = { titleText } };
            AutomationProperties.SetName(header, title);
            if (caption is not null) {
                var captionText = new TextBlock { Theme = KitTheme("Kit.Text"), Classes = { "caption", "tertiary" }, Text = caption, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(captionText, 1);
                header.Children.Add(captionText);
            }

            var group = new FormGroup { Theme = KitTheme("Kit.FormGroup"), TrailingGap = 14 };
            foreach (Control item in items)
                group.Items.Add(item);

            var section = new StackPanel();
            if (index > 0) {
                var rule = new Border { Height = 1 };
                LiveBrush.Bind(rule, Border.BackgroundProperty, "Foreman.Color.Border");
                section.Children.Add(rule);
            }
            section.Children.Add(header);
            section.Children.Add(group);
            return section;
        }

        private ResearchBonusRow BuildBonusRow(ResearchField field, bool isStored) {
            bool isCount = ResearchFields.IsCount(field);
            var stepper = new Stepper {
                Theme = KitTheme("Kit.Stepper"),
                Minimum = 0,
                Maximum = 100000,
                Increment = isCount ? 1 : 10,
                Decimals = isCount ? 0 : 2,
                Unit = isCount ? null : "%",
                ShowsPlusSign = isCount,
                IsWide = true,
                Placeholder = "None",
            };
            var revert = new RevertButton { Theme = KitTheme("Kit.RevertButton"), IsReserved = true, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-9, 0, 0, 0), IsHitTestVisible = false, Opacity = 0, Transitions = Motion.Transition(TransitionKind.Fade, Motion.HoverDuration) };
            LiveBrush.Bind(dot, Shape.FillProperty, "Foreman.Color.Accent");

            var control = new Panel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { stepper, revert } }, dot } };
            var formRow = new FormRow { Theme = KitTheme("Kit.FormRow"), Label = ResearchText.FieldLabel(field), Content = control };
            AutomationProperties.SetAccessibilityView(control, AccessibilityView.Raw);

            var row = new ResearchBonusRow(viewModel, field, isStored) { Row = formRow, Stepper = stepper, Dot = dot, Revert = revert };
            AutomationProperties.SetName(stepper, formRow.Label);
            AutomationProperties.SetName(revert, "Revert " + formRow.Label);

            stepper.PropertyChanged += (_, e) => {
                if (row.IsApplying)
                    return;
                if (e.Property == Stepper.ValueProperty || (e.Property == Stepper.IsEmptyProperty && !stepper.IsEmpty))
                    Commit(row);
            };
            revert.Click += (_, _) => {
                viewModel.Revert(field);
                RaiseChanged();
                FocusStepper(stepper);
            };
            formRow.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => {
                if (e.Key != Key.Back || e.KeyModifiers != KeyModifiers.Meta)
                    return;
                e.Handled = true;
                if (viewModel.State.IsHandEdited(field)) {
                    viewModel.Revert(field);
                    RaiseChanged();
                }
            }, RoutingStrategies.Tunnel);
            return row;
        }

        private ResearchRecipeProductivityRow BuildRecipeRow() {
            var count = new TextBlock { Theme = KitTheme("Kit.Text"), Classes = { "callout" }, VerticalAlignment = VerticalAlignment.Center };
            Button show = ResearchSourceBar.NewMiniButton("Show Recipes");
            show.VerticalAlignment = VerticalAlignment.Center;
            show.Click += (_, _) => showRecipes();
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { count, show } };
            var formRow = new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Recipe productivity", Content = content };
            return new ResearchRecipeProductivityRow { Row = formRow, Content = content, CountText = count, ShowRecipesButton = show };
        }

        private static void FocusStepper(Stepper stepper) => stepper.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();

        private void Commit(ResearchBonusRow row) {
            viewModel.SetField(row.Field, row.Stepper.IsEmpty ? null : row.Stepper.Value);
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void Refresh() {
            ResearchState state = viewModel.State;
            foreach (ResearchBonusRow row in rows.Values)
                RefreshRow(row, state);
            int count = state.RecipeProductivity.Count;
            RecipeProductivityRow.CountText.Text = count == 1 ? "1 recipe" : $"{count} recipes";
            AutomationProperties.SetName(RecipeProductivityRow.Content, RecipeProductivityRow.Text);
        }

        private void RefreshRow(ResearchBonusRow row, ResearchState state) {
            ResearchField field = row.Field;
            double? display = viewModel.DisplayValue(field);
            double? synced = state.GetSynced(field);
            bool edited = state.IsHandEdited(field);
            bool isCount = ResearchFields.IsCount(field);

            row.IsApplying = true;
            row.Stepper.IsEmpty = display is null;
            if (display is double shown)
                row.Stepper.Value = shown;
            row.IsApplying = false;

            row.Dot.Opacity = edited ? 1 : 0;
            row.Revert.IsReserved = !edited;
            row.Row.IsEmphasized = edited;
            row.Row.Note = state.Source is null ? null
                : synced is double known ? (edited ? "Synced value: " + ResearchText.Value(field, known) : null)
                : "Not in this save.";

            var help = new List<string>();
            if (edited)
                help.Add("Edited by hand.");
            if (edited && synced is double spokenSource)
                help.Add($"Synced value {Spoken(spokenSource, isCount)}.");
            else if (synced is null && state.Source is not null)
                help.Add("Not in this save.");
            if (row.IsStored)
                help.Add(NotAppliedYet + ".");
            AutomationProperties.SetHelpText(row.Stepper, help.Count == 0 ? null : string.Join(' ', help));
            AutomationProperties.SetHelpText(row.Revert, synced is double back
                ? $"Sets it back to {ResearchText.Value(field, back)} from the save."
                : "Clears the value.");
        }

        private static string Spoken(double value, bool isCount) =>
            isCount ? "plus " + value.ToString("0.##", DisplayCulture.Format) : (value * 100).ToString("0.##", DisplayCulture.Format) + " percent";
    }
}
