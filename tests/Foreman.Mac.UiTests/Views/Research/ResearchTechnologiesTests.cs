using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Research;
using Foreman.Models.Research;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Foreman.Mac.UiTests.Views.Research.ResearchWindowTests;

namespace Foreman.Mac.UiTests.Views.Research {
    public class ResearchTechnologiesTests {
        private const string Steel = "steel-processing";
        private const string Damage = "physical-projectile-damage-3";
        private const string Mining = "mining-productivity-3";
        private const string Silo = "rocket-silo";

        private static ResearchTechnology Single(string name, bool researched) => new(name, researched, researched ? 1 : 0, 1, 1);

        private static ResearchState WithTechnologies(params ResearchTechnology[] technologies) {
            var values = new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = 1.2 };
            return new ResearchState(Source(), values, values, new Dictionary<string, double>(), new Dictionary<string, double>(), technologies);
        }

        private static ResearchState Mixed() => WithTechnologies(
            Single(Steel, true),
            new ResearchTechnology(Damage, false, 4, 1, 7),
            new ResearchTechnology(Mining, false, 12, 3, ResearchTechnology.Unlimited),
            Single(Silo, false));

        private static ResearchWindow OpenTechnologies(Rig rig, ResearchState state) {
            ResearchWindow window = rig.Open(state);
            window.SelectSegment(2);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return window;
        }

        private static Button PanelButton(StatePanel panel) => panel.Buttons!.Items.OfType<Button>().Single();

        private static void Click(Button button) {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        private static void TabFromFocus(ResearchWindow window) =>
            CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);

        [AvaloniaFact]
        public void Technologies_LevelsAndFilter() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, WithTechnologies(
                new ResearchTechnology("unlimited", false, 12, 3, ResearchTechnology.Unlimited),
                new ResearchTechnology("capped", false, 4, 1, 7),
                new ResearchTechnology("offset", false, 2, 3, 7),
                new ResearchTechnology("single", true, 1, 1, 1),
                new ResearchTechnology("untouched", false, 0, 1, 1)));
            ResearchTechnologiesView view = window.TechnologiesView;

            Assert.Equal("12 of ∞", view.Row("unlimited").LevelsText.Text);
            Assert.Equal("4 of 7", view.Row("capped").LevelsText.Text);
            Assert.Equal("2 of 5", view.Row("offset").LevelsText.Text);
            Assert.Equal("", view.Row("single").LevelsText.Text);
            Assert.Equal("Partly researched", view.Row("unlimited").StateText.Text);
            Assert.Equal("Researched", view.Row("single").StateText.Text);
            Assert.Equal("Not researched", view.Row("untouched").StateText.Text);
            Assert.Equal(["All", "Researched", "Not researched"], view.StateFilter.Items);
            Assert.Equal(200, view.Filter.Width);
            Assert.Equal("Filter", view.Filter.PlaceholderText);

            view.StateFilter.SelectedIndex = 1;
            Assert.Equal(["capped", "offset", "single", "unlimited"], view.ShownNames);

            view.StateFilter.SelectedIndex = 2;
            Assert.Equal(["untouched"], view.ShownNames);

            view.StateFilter.SelectedIndex = 0;
            Assert.Equal(5, view.ShownNames.Count);
        }

        [AvaloniaFact]
        public void Technologies_Footer_CountsTheWholeList() {
            using var rig = new Rig();
            ResearchTechnology[] many = [.. Enumerable.Range(0, 214).Select(i => Single($"tech-{i:000}", i < 120))];
            ResearchWindow window = OpenTechnologies(rig, WithTechnologies(many));

            Assert.Equal("214 technologies · 120 researched", window.TechnologiesView.Footer.Text);
            window.TechnologiesView.StateFilter.SelectedIndex = 2;
            Assert.Equal("214 technologies · 120 researched", window.TechnologiesView.Footer.Text);
            Assert.True(window.TechnologiesView.Footer.IsVisible);
        }

        [AvaloniaFact]
        public void Technologies_Empty_OffersChooseSave() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, WithTechnologies());
            ResearchTechnologiesView view = window.TechnologiesView;

            Assert.True(view.EmptyPanel.IsVisible);
            Assert.False(view.NoMatchPanel.IsVisible);
            Assert.False(view.HiddenPanel.IsVisible);
            Assert.False(view.Table.IsVisible);
            Assert.False(view.Footer.IsVisible);
            Assert.Equal("No technologies yet", view.EmptyPanel.Headline);
            Assert.Equal("Sync from a save to list its technologies and their levels.", view.EmptyPanel.Sentence);
            Assert.Equal("Choose Save…", PanelButton(view.EmptyPanel).Content);
        }

        [AvaloniaFact]
        public void Technologies_NoResults_ClearsTheFilter() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            ResearchTechnologiesView view = window.TechnologiesView;

            view.Filter.Text = "zz";
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.NoMatchPanel.IsVisible);
            Assert.False(view.HiddenPanel.IsVisible);
            Assert.False(view.Table.IsVisible);
            Assert.Equal("No technologies match “zz”", view.NoMatchPanel.Headline);
            Assert.Equal("Check the spelling, or clear the filter.", view.NoMatchPanel.Sentence);
            Assert.Equal("Clear Filter", PanelButton(view.NoMatchPanel).Content);

            Click(PanelButton(view.NoMatchPanel));
            Assert.Equal("", view.Filter.Text ?? "");
            Assert.False(view.NoMatchPanel.IsVisible);
            Assert.Equal(4, view.ShownNames.Count);
        }

        [AvaloniaFact]
        public void Technologies_FilterHidden_ShowAllResetsTheStateFilter() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, WithTechnologies(Single(Steel, true), Single(Silo, true)));
            ResearchTechnologiesView view = window.TechnologiesView;

            view.StateFilter.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.HiddenPanel.IsVisible);
            Assert.False(view.NoMatchPanel.IsVisible);
            Assert.False(view.EmptyPanel.IsVisible);
            Assert.True(view.Footer.IsVisible);
            Assert.Equal("No technologies to show", view.HiddenPanel.Headline);
            Assert.Equal("Every technology is hidden by the filter.", view.HiddenPanel.Sentence);
            Assert.Equal("Show All", PanelButton(view.HiddenPanel).Content);

            Click(PanelButton(view.HiddenPanel));
            Assert.Equal(0, view.StateFilter.SelectedIndex);
            Assert.False(view.HiddenPanel.IsVisible);
            Assert.Equal(2, view.ShownNames.Count);
        }

        [AvaloniaFact]
        public void Technologies_EmptyPanel_ChooseSaveRunsTheSourceBarAction() {
            using var rig = new Rig();
            ResearchWindow window = rig.OpenDirect(WithTechnologies(), rig.Host());
            window.SelectSegment(2);
            Dispatcher.UIThread.RunJobs();
            int clicks = 0;
            window.SourceBar.ChooseButton.Click += (_, _) => clicks++;

            Click(PanelButton(window.TechnologiesView.EmptyPanel));

            Assert.Equal(1, clicks);
        }

        [AvaloniaFact]
        public void Technologies_NotInPreset_ShowsInternalName() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, WithTechnologies(Single(Steel, true), Single("foreman-modded-tech", false)));
            ResearchTechnologiesView view = window.TechnologiesView;

            ResearchTechnologyRow known = view.Row(Steel);
            Assert.Equal("Steel processing", known.NameText.Text);
            Assert.NotNull(known.IconImage.Source);
            Assert.Equal(16, known.IconImage.Width);
            Assert.NotEqual(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(known.NameText.Foreground).Color);

            ResearchTechnologyRow unknown = view.Row("foreman-modded-tech");
            Assert.Equal("foreman-modded-tech", unknown.NameText.Text);
            Assert.Null(unknown.IconImage.Source);
            Assert.Equal(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(unknown.NameText.Foreground).Color);
            Assert.Equal(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(unknown.StateText.Foreground).Color);
            Assert.NotEqual(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(known.StateText.Foreground).Color);
        }

        private static Avalonia.Media.Color Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value), key);
            return Assert.IsType<Avalonia.Media.Color>(value);
        }

        [AvaloniaFact]
        public void Technologies_VoiceOver() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            ResearchTechnologiesView view = window.TechnologiesView;

            Assert.Equal("Steel processing, researched", AutomationProperties.GetName(view.Row(Steel).Item));
            Assert.Equal("Physical projectile damage 3, 4 of 7 levels, partly researched", AutomationProperties.GetName(view.Row(Damage).Item));
            Assert.Equal("Mining productivity 3, 12 levels done, no maximum, partly researched", AutomationProperties.GetName(view.Row(Mining).Item));
            Assert.Equal("Rocket silo, not researched", AutomationProperties.GetName(view.Row(Silo).Item));
            Assert.Equal("Technology table", AutomationProperties.GetName(view.Table));
            Assert.Equal("Technology state", AutomationProperties.GetName(view.StateFilter));
            Assert.Equal("Filter", AutomationProperties.GetName(view.Filter));
        }

        [AvaloniaFact]
        public void Technologies_SortsByNameThenColumns() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            ResearchTechnologiesView view = window.TechnologiesView;

            Assert.Equal([Mining, Damage, Silo, Steel], view.ShownNames);

            view.SortBy(0);
            Assert.Equal([Steel, Silo, Damage, Mining], view.ShownNames);

            view.SortBy(1);
            Assert.Equal([Mining, Damage, Steel, Silo], view.ShownNames);

            view.SortBy(1);
            Assert.Equal([Silo, Steel, Damage, Mining], view.ShownNames);

            view.SortBy(2);
            Assert.Equal([Silo, Mining, Damage, Steel], view.ShownNames);

            view.SortBy(2);
            Assert.Equal([Steel, Mining, Damage, Silo], view.ShownNames);
        }

        [AvaloniaFact]
        public void Technologies_StateFilter_KeyboardAndRemembered() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            ResearchTechnologiesView view = window.TechnologiesView;

            CanvasKeys.KeyDownOn(window, Key.D2, KeyModifiers.Meta | KeyModifiers.Alt);
            Assert.Equal(1, view.StateFilter.SelectedIndex);
            Assert.Equal(1, rig.Window.Settings!.ResearchTechnologyFilter);
            Assert.Equal(3, view.ShownNames.Count);

            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta | KeyModifiers.Alt);
            Assert.Equal(2, view.StateFilter.SelectedIndex);
            Assert.Equal([Silo], view.ShownNames);

            CanvasKeys.KeyDownOn(window, Key.D1, KeyModifiers.Meta | KeyModifiers.Alt);
            Assert.Equal(0, view.StateFilter.SelectedIndex);
            Assert.Equal(2, window.SegmentControl.SelectedIndex);

            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta | KeyModifiers.Alt);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, rig.Window.Settings!.ResearchTechnologyFilter);

            ResearchWindow again = rig.Open();
            Assert.Equal(2, again.TechnologiesView.StateFilter.SelectedIndex);
        }

        [AvaloniaFact]
        public void Technologies_StateShortcuts_OnlyOnTheTechnologiesSegment() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            window.SelectSegment(1);
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta | KeyModifiers.Alt);

            Assert.Equal(0, window.TechnologiesView.StateFilter.SelectedIndex);
            Assert.Equal(1, window.SegmentControl.SelectedIndex);
        }

        [AvaloniaFact]
        public void Technologies_CommandF_FocusesFilter_EscapeClearsThenCloses() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());

            CanvasKeys.KeyDownOn(window, Key.F, KeyModifiers.Meta);
            Assert.True(window.TechnologiesView.Filter.IsFocused);

            window.TechnologiesView.Filter.Text = "steel";
            Assert.Equal([Steel], window.TechnologiesView.ShownNames);
            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.Equal("", window.TechnologiesView.Filter.Text ?? "");
            Assert.True(window.IsVisible);
            Assert.Equal(4, window.TechnologiesView.ShownNames.Count);

            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public void Technologies_Tab_TableIsOneStopAfterTheSourceBar_ThenLeaves() {
            using var rig = new Rig();
            ResearchWindow window = OpenTechnologies(rig, Mixed());
            ResearchTechnologiesView view = window.TechnologiesView;

            window.SourceBar.SyncButton.Focus(NavigationMethod.Tab);
            Assert.True(window.SourceBar.SyncButton.IsKeyboardFocusWithin);
            TabFromFocus(window);
            Assert.True(view.StateFilter.IsKeyboardFocusWithin, $"reached {window.FocusManager!.GetFocusedElement()}");
            TabFromFocus(window);
            Assert.True(view.Filter.IsKeyboardFocusWithin, $"reached {window.FocusManager!.GetFocusedElement()}");
            TabFromFocus(window);
            Assert.True(view.Table.IsKeyboardFocusWithin, $"reached {window.FocusManager!.GetFocusedElement()}");

            CanvasKeys.KeyDownOn(view.Row(view.ShownNames[0]).Item, Key.Down);
            Assert.Equal(view.ShownNames[1], view.SelectedName);
            TabFromFocus(window);
            Assert.False(view.Table.IsKeyboardFocusWithin, $"Tab should leave the table, reached {window.FocusManager!.GetFocusedElement()}");
            Assert.True(window.SegmentControl.IsKeyboardFocusWithin);
        }

        [AvaloniaFact]
        public void Technologies_SegmentSwitch_CrossFades_AndIsInstantWithReduceMotion() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Mixed());
            Border host = window.BodyHostControl;

            window.SelectSegment(2);
            Assert.Equal(0, host.Opacity);
            Assert.Equal(Motion.CrossFadeDuration, host.Transitions!.OfType<Avalonia.Animation.DoubleTransition>().Single().Duration);
            Dispatcher.UIThread.RunJobs();

            Motion.ReduceMotion = true;
            try {
                window.SelectSegment(0);
                Assert.Equal(1, host.Opacity);
                Assert.Null(host.Transitions);
            } finally {
                Motion.ReduceMotion = false;
            }
        }
    }
}
