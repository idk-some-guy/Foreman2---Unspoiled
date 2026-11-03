using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    public class WelcomeWindowTests {
        private static readonly Preset Vanilla = new("Factorio 2.0 Vanilla", false, false);
        private static readonly Preset SpaceAge = new("Factorio 2.0 Space Age Modified", false, false);
        private static readonly Preset Krastorio = new("Krastorio 2", false, false);

        private sealed class Env : IDisposable {
            public LibraryFixture Library { get; } = new();
            public AppSettings Settings { get; } = new() { CurrentPresetName = Vanilla.Name };
            public List<Preset> Installed { get; } = [Vanilla, SpaceAge, Krastorio];
            public Func<Preset, IProgress<KeyValuePair<int, string>>, CancellationToken, Task<bool>> Load { get; set; } = (_, _, _) => Task.FromResult(true);
            public List<string> Calls { get; } = [];
            public Task<bool>? NewGraphGate { get; set; }
            public bool GraphOpen { get; set; }
            public WelcomeWindow? Window { get; private set; }

            public GraphRow AddRow(string name, string preset, int nodes = 3, TimeSpan? after = null) {
                Library.Clock.Advance(after ?? TimeSpan.FromHours(1));
                return Library.Store.Create(name, preset, "{}", nodes, null, Library.Clock.GetUtcNow().UtcDateTime);
            }

            public WelcomeWindow Open() {
                var host = new WelcomeHost {
                    Library = Library.Service,
                    Settings = Settings,
                    SaveSettings = () => Calls.Add("save"),
                    InstalledPresets = () => Installed,
                    LoadPreset = (preset, progress, ct) => Load(preset, progress, ct),
                    NewGraph = () => { Calls.Add("new"); return NewGraphGate ?? Task.FromResult(true); },
                    OpenFile = () => { Calls.Add("open"); return Task.FromResult(false); },
                    OpenGraph = (row, preset) => { Calls.Add(preset is null ? "open:" + row.Name : $"open:{row.Name}:{preset.Name}"); return Task.FromResult(true); },
                    ExportGraph = row => { Calls.Add("export:" + row.Name); return Task.CompletedTask; },
                    RatePresets = _ => Task.FromResult(new List<PresetErrorPackage>()),
                    HasOpenGraph = () => GraphOpen,
                };
                Window = new WelcomeWindow(host);
                Window.Show();
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();
                return Window;
            }

            public void Dispose() {
                Window?.Close();
                WelcomeWindow.ForgetSize();
                Library.Dispose();
            }
        }

        private static IEnumerable<string> Strings(Visual root) =>
            root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

        private static void Click(Button button) {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void Title_Foreman2_MinimumClient660By420_Resizable_LeftPaneIsTheToken_TwoActions_GroupTitle_CheckboxCheckedByDefault() {
            using var env = new Env();
            WelcomeWindow window = env.Open();

            Assert.Equal("Foreman 2", window.Title);
            Assert.Equal(660, window.ClientSize.Width);
            Assert.Equal(420, window.ClientSize.Height);
            Assert.True(window.CanResize);
            Assert.Equal(660, window.MinWidth);
            Assert.Equal(420, window.MinHeight);
            Resize(window, 300, 200);
            Assert.Equal((660, 420), (window.ClientSize.Width, window.ClientSize.Height));
            Assert.Equal(SidebarToken(), window.LeftPane.Bounds.Width);
            Assert.Equal(["New Graph", "Open…"], new[] { window.NewGraphButton, window.OpenButton }.Select(b => AutomationActionLabel(b)));
            Assert.Equal(2, window.ActionsGroup.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ActionButtons").Children.OfType<Button>().Count());
            Assert.Equal("Preset for new graphs", window.GroupTitle.Text);
            Assert.Equal("Show when Foreman opens", window.ShowAtLaunchCheckBox.Content);
            Assert.True(window.ShowAtLaunchCheckBox.IsChecked);
            Assert.Contains("Foreman 2", Strings(window.LeftPane));
            Assert.Contains(AppVersion.VersionedDisplay, Strings(window.LeftPane));

            Assert.Equal(3, window.PresetList.GetVisualDescendants().OfType<ListBoxItem>().Count());
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutSize);
            foreach (ListBoxItem item in window.PresetList.GetVisualDescendants().OfType<ListBoxItem>()) {
                Assert.Equal(24, item.Bounds.Height);
                TextBlock label = item.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.Contains("callout", label.Classes);
                Assert.Equal(Assert.IsType<double>(calloutSize), label.FontSize);
            }

            IBrush accent = new ImmutableSolidColorBrush(ThemeResources.ResolveColor("Foreman.Color.Accent", Colors.Transparent));
            foreach (Button button in new[] { window.NewGraphButton, window.OpenButton }) {
                Avalonia.Controls.Shapes.Path glyph = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                Assert.Equal(accent.ToImmutable(), glyph.Stroke?.ToImmutable());
                Viewbox box = button.GetVisualDescendants().OfType<Viewbox>().Single();
                Assert.Equal((16, 16), (box.Width, box.Height));
            }

            window.ShowAtLaunchCheckBox.IsChecked = false;
            Assert.False(env.Settings.ShowWelcomeAtLaunch);
            Assert.Contains("save", env.Calls);
        }

        private static double SidebarToken() {
            Application.Current!.TryGetResource("Foreman.Size.WelcomeSidebar", Application.Current.ActualThemeVariant, out object? width);
            return Assert.IsType<double>(width);
        }

        private static void Resize(WelcomeWindow window, double width, double height) {
            window.Width = width;
            window.Height = height;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
        }

        private static double InterWidth(string text, double fontSize) {
            using Stream stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf"));
            using SKTypeface face = SKTypeface.FromStream(stream);
            using var font = new SKFont(face, (float)fontSize);
            return font.MeasureText(text);
        }

        private static void AssertFits(TextBlock label, string text) {
            double width = InterWidth(text, label.FontSize);
            Assert.True(width <= label.Bounds.Width, $"'{text}' needs {width} but has {label.Bounds.Width}");
        }

        [AvaloniaFact]
        public void Growing_RecentListGrowsAndSidebarWidthStays() {
            using var env = new Env();
            env.AddRow("Green", Vanilla.Name);
            WelcomeWindow window = env.Open();
            double sidebar = window.LeftPane.Bounds.Width;
            Rect before = window.RecentList.Bounds;

            Resize(window, 900, 600);

            Assert.Equal(sidebar, window.LeftPane.Bounds.Width);
            Assert.Equal(before.Width + 240, window.RecentList.Bounds.Width, 0.5);
            Assert.Equal(before.Height + 180, window.RecentList.Bounds.Height, 0.5);
        }

        [AvaloniaFact]
        public void ClosedAndReopened_KeepsTheLargerSize() {
            using var env = new Env();
            WelcomeWindow first = env.Open();
            Resize(first, 900, 600);
            first.Close();

            WelcomeWindow second = env.Open();

            Assert.Equal(900, second.ClientSize.Width);
            Assert.Equal(600, second.ClientSize.Height);
        }

        [AvaloniaFact]
        public void AtMinimumSize_PresetNamesAndCheckboxLabelAreUntrimmed_CheckboxInsideTheWindow() {
            using var env = new Env();
            WelcomeWindow window = env.Open();

            TextBlock[] labels = [.. window.PresetList.GetVisualDescendants().OfType<ListBoxItem>().Select(i => i.GetVisualDescendants().OfType<TextBlock>().First())];
            Assert.Equal([Vanilla.Name, SpaceAge.Name, Krastorio.Name], labels.Select(l => l.Text));
            foreach (TextBlock label in labels)
                AssertFits(label, label.Text!);
            AssertFits(window.ShowAtLaunchCheckBox.GetVisualDescendants().OfType<TextBlock>().First(), "Show when Foreman opens");

            CheckBox box = window.ShowAtLaunchCheckBox;
            Assert.True(box.Bounds.Height >= box.DesiredSize.Height - box.Margin.Top - 0.01, "checkbox row is clipped");
            double bottom = box.TranslatePoint(new Point(0, box.Bounds.Height), window.LeftPane)!.Value.Y;
            Assert.True(bottom <= window.LeftPane.Bounds.Height - 12, "checkbox sits below the pane inset");
        }

        [AvaloniaFact]
        public void LongPresetName_CarriesTheMiddleTrimming_ReadsFullToVoiceOver() {
            using var env = new Env();
            const string name = "Factorio 2.0 Space Age Modified Extended Edition 2026";
            env.Installed.Add(new Preset(name, false, false));
            WelcomeWindow window = env.Open();

            TextBlock label = window.PresetList.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == name);
            ListBoxItem item = label.FindAncestorOfType<ListBoxItem>()!;

            Assert.IsType<MiddleTrimming>(label.TextTrimming);
            Assert.Equal(name, ControlAutomationPeer.CreatePeerForElement(label).GetName());
            Assert.Equal(name, ControlAutomationPeer.CreatePeerForElement(item).GetName());
        }

        private static string AutomationActionLabel(Button button) =>
            button.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "";

        [Collection(ScaleTestGroup.Name)]
        public sealed class ScaleTests : IDisposable {
            public ScaleTests() => WindowScaler.SetScale(1.0);

            public void Dispose() {
                WindowScaler.SetScale(1.0);
                GC.SuppressFinalize(this);
            }

            [AvaloniaFact]
            public void AtOnePointFive_MinimumClient990By630_LeftPaneIs366() {
                WindowScaler.SetScale(1.5);
                using var env = new Env();
                WelcomeWindow window = env.Open();

                Assert.Equal(990, window.ClientSize.Width);
                Assert.Equal(630, window.ClientSize.Height);
                Assert.Equal(990, window.MinWidth);
                Assert.Equal(630, window.MinHeight);
                Assert.Equal(366, window.LeftPane.Bounds.Width);
            }

            [AvaloniaFact]
            public void ScaleChangeWhileOpen_CarriesTheSizeAndMinimumAcrossSteps() {
                using var env = new Env();
                WelcomeWindow window = env.Open();
                Resize(window, 800, 500);

                WindowScaler.SetScale(1.5);
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();
                Assert.Equal((990, 630), (window.MinWidth, window.MinHeight));
                Assert.Equal((1200, 750), (window.ClientSize.Width, window.ClientSize.Height));

                WindowScaler.SetScale(1.0);
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();
                Assert.Equal((800, 500), (window.ClientSize.Width, window.ClientSize.Height));
                Assert.Equal(660, window.MinWidth);
            }

            [AvaloniaFact]
            public void ClosedAtOneAndReopenedAtOnePointFive_ScalesTheRememberedSize() {
                using var env = new Env();
                WelcomeWindow first = env.Open();
                Resize(first, 800, 500);
                first.Close();

                WindowScaler.SetScale(1.5);
                WelcomeWindow second = env.Open();

                Assert.Equal((1200, 750), (second.ClientSize.Width, second.ClientSize.Height));
            }
        }

        private static SKColor Sk(Color color) => new(color.R, color.G, color.B, color.A);

        [AvaloniaFact]
        public void EmptyGraphThumbnailStroke_FollowsTheThemeLiveOnTheSameBrush() {
            var owner = new Border();
            var host = new Window { Content = owner };
            host.Show();
            Application app = Application.Current!;
            ThemeVariant? original = app.RequestedThemeVariant;
            try {
                app.RequestedThemeVariant = ThemeVariant.Light;
                Dispatcher.UIThread.RunJobs();
                var thumbnail = Assert.IsType<DrawingBrush>(WelcomeWindow.ThumbnailBrush(null, owner));
                GeometryDrawing outline = Assert.IsType<DrawingGroup>(thumbnail.Drawing).Children.OfType<GeometryDrawing>().Single(d => d.Pen is not null);
                Assert.Equal(TokenBridge.ColorIn("Foreman.Color.TextTertiary", ThemeVariant.Light), Sk(Assert.IsAssignableFrom<ISolidColorBrush>(outline.Pen!.Brush).Color));

                app.RequestedThemeVariant = ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(TokenBridge.ColorIn("Foreman.Color.TextTertiary", ThemeVariant.Dark), Sk(Assert.IsAssignableFrom<ISolidColorBrush>(outline.Pen!.Brush).Color));
            } finally {
                app.RequestedThemeVariant = original;
                host.Close();
            }
        }

        [AvaloniaFact]
        public void FiveRows_OrderedByLastOpened_HeaderCountFive_NoFilter_NineRows_FilterExists_ThumbnailFallbackAndRelativeTimes() {
            using var env = new Env();
            string[] names = ["Green", "Iron", "Blue", "Platform", "Untitled"];
            GraphRow oldest = env.AddRow(names[0], Vanilla.Name);
            foreach (string name in names.Skip(1))
                env.AddRow(name, Vanilla.Name);
            env.Library.Clock.Advance(TimeSpan.FromDays(3));
            env.Library.Service.Open(oldest.Id);
            env.Library.Service.Detach();
            WelcomeWindow window = env.Open();

            RecentRow[] rows = [.. window.RecentRows];
            Assert.Equal(5, rows.Length);
            Assert.Equal(["Green", "Untitled", "Platform", "Blue", "Iron"], rows.Select(r => r.Label));
            Assert.Equal("5", window.CountText.Text);
            Assert.False(window.FilterField.IsVisible);
            Assert.StartsWith("Today, ", rows[0].RelativeTime);
            Assert.Equal("3 days ago", rows[1].RelativeTime);
            Assert.Equal(Vanilla.Name, rows[0].PresetLabel);
            Assert.Equal(3, rows[0].NodeCount);
            Application.Current!.TryGetResource("Kit.Glyph.EmptyGraph.32", null, out object? emptyGraph);
            DrawingBrush fallback = Assert.IsType<DrawingBrush>(rows[0].Icon);
            Assert.Contains(Assert.IsType<DrawingGroup>(fallback.Drawing).Children.OfType<GeometryDrawing>(), d => ReferenceEquals(d.Geometry, emptyGraph));
            Dispatcher.UIThread.RunJobs();
            IBrush? penBrush = Assert.IsType<DrawingGroup>(fallback.Drawing).Children.OfType<GeometryDrawing>().Single(d => d.Pen is not null).Pen!.Brush;
            Color stroke = Assert.IsAssignableFrom<ISolidColorBrush>(penBrush).Color;
            SKColor expected = TokenBridge.ColorIn("Foreman.Color.TextTertiary", Application.Current!.ActualThemeVariant);
            Assert.Equal((expected.Alpha, expected.Red, expected.Green, expected.Blue), (stroke.A, stroke.R, stroke.G, stroke.B));
            window.Close();

            for (int i = 0; i < 4; i++)
                env.AddRow("Extra " + i, Vanilla.Name);
            window = env.Open();
            Assert.Equal(9, window.RecentRows.Count());
            Assert.Equal("9", window.CountText.Text);
            Assert.True(window.FilterField.IsVisible);
        }

        [AvaloniaFact]
        public void Filter_NarrowsTheList_AndNoMatchesShowsTheNoResultsPanel_ClearFilterRestores() {
            using var env = new Env();
            for (int i = 0; i < 9; i++)
                env.AddRow(i == 4 ? "Kovarex" : "Graph " + i, Vanilla.Name);
            WelcomeWindow window = env.Open();

            window.FilterField.Text = "kov";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Kovarex"], window.RecentRows.Select(r => r.Label));

            window.FilterField.Text = "zzz";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.RecentRows);
            Assert.True(window.NoResultsPanel.IsVisible);
            Assert.Equal(StatePanelShape.NoResults, window.NoResultsPanel.Shape);
            Assert.Equal("No graphs match “zzz”", window.NoResultsPanel.Headline);

            Click(Assert.IsType<Button>(window.NoResultsPanel.Buttons!.Items.Single()));
            Assert.Equal(9, window.RecentRows.Count());
            Assert.Equal("", window.FilterField.Text);

            CanvasKeys.KeyDownOn(window.RecentList, Key.F, KeyModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FilterField.IsKeyboardFocusWithin);
        }

        [AvaloniaFact]
        public void EmptyLibrary_EmptyPanelRuledStrings_NewGraphIsDefault() {
            using var env = new Env();
            WelcomeWindow window = env.Open();

            Assert.True(window.EmptyPanel.IsVisible);
            Assert.Equal(StatePanelShape.Empty, window.EmptyPanel.Shape);
            Assert.Equal("No graphs yet", window.EmptyPanel.Headline);
            Assert.Equal("Graphs you create or open appear here. New graphs use the preset chosen on the left.", window.EmptyPanel.Sentence);
            Button newGraph = Assert.IsType<Button>(window.EmptyPanel.Buttons!.Items.Single());
            Assert.Equal("New Graph", newGraph.Content);
            Assert.True(newGraph.IsDefault);
            Assert.False(window.RecentList.IsVisible);
        }

        [AvaloniaFact]
        public void StubbedLoadAt43Percent_SentenceEndsReadingIconsOneOfThree_BarExists_NewGraphDisabled_CancelInTabOrder_CancelEndsCancelled() {
            using var env = new Env();
            env.AddRow("Green", Vanilla.Name);
            env.Load = async (_, progress, ct) => {
                progress.Report(new KeyValuePair<int, string>(43, ""));
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            };
            WelcomeWindow window = env.Open();
            Assert.False(window.LoadingStrip.IsVisible);

            window.PresetList.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.LoadingStrip.IsVisible);
            double halfwayHeight = HeightAfter(75, window.LoadingStrip);
            AnimationClock.Settle();
            double fullHeight = window.LoadingStrip.Bounds.Height;
            Assert.InRange(halfwayHeight, 0.01, fullHeight - 0.01);
            Assert.EndsWith("reading icons (1 of 3).", window.LoadingSentence.Text);
            Assert.StartsWith("Loading “Krastorio 2”: ", window.LoadingSentence.Text);
            Assert.NotNull(window.LoadingBar);
            Assert.Equal(43, window.LoadingBar.Value);
            Assert.False(window.NewGraphButton.IsEnabled);
            Assert.False(window.OpenButton.IsEnabled);
            Assert.True(window.CancelButton.IsVisible && window.CancelButton.IsEnabled && window.CancelButton.Focusable && window.CancelButton.IsTabStop);
            Assert.NotEmpty(window.RecentRows);
            Assert.Equal(WelcomeLoadPhase.Loading, window.ViewModel.Phase);

            Click(window.CancelButton);
            double collapsingHeight = HeightAfter(75, window.LoadingStrip);
            Assert.InRange(collapsingHeight, 0.01, fullHeight - 0.01);
            AnimationClock.Settle();

            Assert.Equal(WelcomeLoadOutcome.Cancelled, window.ViewModel.LastOutcome);
            Assert.Equal(WelcomeLoadPhase.Idle, window.ViewModel.Phase);
            Assert.False(window.LoadingStrip.IsVisible);
            Assert.False(window.NewGraphButton.IsEnabled);

            Motion.ReduceMotion = true;
            try {
                _ = window.ViewModel.TryAgainAsync();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.LoadingStrip.IsVisible);
                Assert.Equal(fullHeight, window.LoadingStrip.Bounds.Height);
                Assert.All(window.LoadingStrip.Transitions!, t => Assert.Equal(Visual.OpacityProperty, ((Avalonia.Animation.DoubleTransition)t).Property));
            } finally {
                Motion.ReduceMotion = false;
            }
        }

        private static double HeightAfter(int milliseconds, Control strip) {
            AnimationClock.Settle(milliseconds);
            return strip.Bounds.Height;
        }

        [AvaloniaFact]
        public async Task StubbedLoadFailure_ErrorPanelRuledHeadline_TryAgain_NoAlert_NoUseDefaultPreset() {
            using var env = new Env();
            int loads = 0;
            env.Load = (_, _, _) => { loads++; return Task.FromResult(false); };
            WelcomeWindow window = env.Open();

            await window.ViewModel.SelectPresetAsync(Vanilla);
            Assert.Equal(0, loads);
            await window.ViewModel.SelectPresetAsync(Krastorio);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ErrorPanel.IsVisible);
            Assert.Equal(StatePanelShape.Error, window.ErrorPanel.Shape);
            Assert.Equal("Couldn’t load “Krastorio 2”", window.ErrorPanel.Headline);
            Assert.Equal("Its data file is missing or unreadable. Choose another preset on the left, or try again.", window.ErrorPanel.Sentence);
            Button tryAgain = Assert.IsType<Button>(window.ErrorPanel.Buttons!.Items.Single());
            Assert.Equal("Try Again", tryAgain.Content);
            Assert.Empty(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
            Assert.DoesNotContain(Strings(window).Concat(window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string ?? "")), s => s.Contains("Use Default Preset"));

            env.Load = (_, _, _) => Task.FromResult(true);
            Click(tryAgain);
            await window.ViewModel.LoadTask;
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.ErrorPanel.IsVisible);
            Assert.True(window.NewGraphButton.IsEnabled);
            Assert.Equal(WelcomeLoadOutcome.Loaded, window.ViewModel.LastOutcome);
            Assert.Equal("Krastorio 2", env.Settings.CurrentPresetName);
            Assert.Contains("save", env.Calls);
        }

        [AvaloniaFact]
        public async Task MissingPresetRow_Open_ShowsAlertCancelThenChoosePreset_ChoosePresetDefault() {
            using var env = new Env();
            GraphRow stored = env.AddRow("Space Platform Loop", "Space Age 2.0.4");
            WelcomeWindow window = env.Open();
            WelcomeRow row = window.ViewModel.Rows.Single();
            Assert.True(row.Unavailable);
            Assert.Equal("Space Age 2.0.4", row.PresetLabel);
            Assert.Equal("Preset “Space Age 2.0.4” isn't installed", row.UnavailableMeta);

            Task open = window.OpenRowAsync(row);
            Dispatcher.UIThread.RunJobs();

            AlertSheet alert = SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Single();
            Assert.Equal("“Space Platform Loop” can’t be opened.", alert.Title);
            Assert.Equal(["Cancel", "Choose Preset…"], alert.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Same(alert.YesButtonControl, alert.Buttons.ResolveDefaultButton());
            alert.Cancel();
            await open;
            Assert.DoesNotContain("open:Space Platform Loop", env.Calls);

            window.ChoosePresetOverride = _ => Task.FromResult<Preset?>(SpaceAge);
            open = window.OpenRowAsync(row);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            alert = SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Single();
            Click(alert.YesButtonControl);
            await open;

            Assert.Contains($"open:Space Platform Loop:{SpaceAge.Name}", env.Calls);
            Assert.Equal("Space Age 2.0.4", env.Library.Store.Get(stored.Id)!.PresetName);
        }

        [AvaloniaFact]
        public void RowMenu_HeadersExactlyOpenDuplicateRenameExportSeparatorDelete_PlusChoosePresetWhenUnavailable_NoFinderNewWindowOrRemoveFromRecents() {
            using var env = new Env();
            env.AddRow("Available", Vanilla.Name);
            env.AddRow("Missing", "Gone");
            WelcomeWindow window = env.Open();

            string[] Menu(string name) => [.. window.RecentRows.Single(r => r.Label == name).MenuItems.Select(i => i.IsSeparator ? "—" : i.Text)];

            Assert.Equal(["Open", "Duplicate", "Rename…", "Export as .fjson…", "—", "Delete Graph…"], Menu("Available"));
            Assert.Equal(["Open", "Duplicate", "Rename…", "Export as .fjson…", "Choose Preset…", "—", "Delete Graph…"], Menu("Missing"));
            string[] all = [.. Menu("Available"), .. Menu("Missing")];
            Assert.DoesNotContain("Show in Finder", all);
            Assert.DoesNotContain("Open in New Window", all);
            Assert.DoesNotContain("Remove from Recents", all);
        }

        [AvaloniaFact]
        public async Task RowMenu_DuplicateAndRename_ActOnTheRowWithoutOpeningIt() {
            using var env = new Env();
            GraphRow stored = env.AddRow("Green", Vanilla.Name);
            WelcomeWindow window = env.Open();
            WelcomeRow row = window.ViewModel.Rows.Single();

            await window.DuplicateRowAsync(row);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, env.Library.Store.ListByLastOpened().Count);
            Assert.Equal(2, window.RecentRows.Count());
            Assert.Null(env.Library.Service.OpenGraphId);

            Task rename = window.RenameRowAsync(row);
            Dispatcher.UIThread.RunJobs();
            RenameGraphSheet sheet = SheetHost.For(window).GetVisualDescendants().OfType<RenameGraphSheet>().Single();
            sheet.NameInputControl.Text = "Renamed";
            Click(sheet.RenameButtonControl);
            await rename;

            Assert.Equal("Renamed", env.Library.Store.Get(stored.Id)!.Name);
            Assert.Contains("Renamed", window.RecentRows.Select(r => r.Label));
        }

        [AvaloniaFact]
        public async Task DeleteGraph_IsADestructiveAlert_CancelIsTheDefault_CommandBackspaceConfirms() {
            using var env = new Env();
            GraphRow stored = env.AddRow("Green", Vanilla.Name);
            WelcomeWindow window = env.Open();
            WelcomeRow row = window.ViewModel.Rows.Single();

            Task delete = window.DeleteRowAsync(row);
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Single();
            Assert.Equal("Delete “Green”?", alert.Title);
            Assert.Equal(["Delete", "Cancel"], alert.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Same(alert.NoButtonControl, alert.Buttons.ResolveDefaultButton());
            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            await delete;
            Assert.NotNull(env.Library.Store.Get(stored.Id));

            window.RecentList.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window.RecentList, Key.Back, KeyModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            alert = SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Single();
            CanvasKeys.KeyDownOn(alert.NoButtonControl, Key.Back, KeyModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Null(env.Library.Store.Get(stored.Id));
            Assert.Empty(window.RecentRows);
            Assert.True(window.EmptyPanel.IsVisible);
        }

        [AvaloniaFact]
        public void ReturnAndCommandDownArrowOnTheSelectedRow_OpenIt_AndTheWindowCloses() {
            using var env = new Env();
            env.AddRow("Green", Vanilla.Name);
            foreach ((Key key, KeyModifiers modifiers) in new[] { (Key.Return, KeyModifiers.None), (Key.Down, KeyModifiers.Meta) }) {
                env.Calls.Clear();
                WelcomeWindow window = env.Open();
                bool closed = false;
                window.Closed += (_, _) => closed = true;
                window.RecentList.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();

                CanvasKeys.KeyDownOn(window.RecentList, key, modifiers);
                Dispatcher.UIThread.RunJobs();

                Assert.Contains("open:Green", env.Calls);
                Assert.True(closed);
            }
        }

        [AvaloniaFact]
        public void NewGraphAndOpenButtons_CallTheHost_AndNewGraphClosesTheWindow() {
            using var env = new Env();
            WelcomeWindow window = env.Open();

            Click(window.OpenButton);
            Assert.Contains("open", env.Calls);

            bool closed = false;
            window.Closed += (_, _) => closed = true;
            Click(window.NewGraphButton);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("new", env.Calls);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void Escape_ClosesTheWindowOnlyWhenAGraphIsOpen() {
            using var env = new Env();
            WelcomeWindow window = env.Open();
            bool closed = false;
            window.Closed += (_, _) => closed = true;

            CanvasKeys.KeyDownOn(window.NewGraphButton, Key.Escape);
            Assert.False(closed);

            env.GraphOpen = true;
            CanvasKeys.KeyDownOn(window.NewGraphButton, Key.Escape);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void VoiceOver_TwoGroups_RowReadings_UnavailableSaysPresetNotInstalled_HelpTextNamesOnlyKeysTheWindowHandles() {
            using var env = new Env();
            env.AddRow("Green", Vanilla.Name, nodes: 118);
            env.AddRow("Gone", "Missing 1");
            WelcomeWindow window = env.Open();

            Assert.Equal("Actions and presets", Avalonia.Automation.AutomationProperties.GetName(window.ActionsGroup));
            Assert.Equal("Recent graphs, 2 graphs", Avalonia.Automation.AutomationProperties.GetName(window.RecentsGroup));
            RecentRow green = window.RecentRows.Single(r => r.Label == "Green");
            RecentRow gone = window.RecentRows.Single(r => r.Label == "Gone");
            Assert.StartsWith("Green, Factorio 2.0 Vanilla, 118 nodes, ", Avalonia.Automation.AutomationProperties.GetName(green));
            Assert.Equal($"Gone, Missing 1, 3 nodes, {gone.AbsoluteTime}, preset not installed", Avalonia.Automation.AutomationProperties.GetName(gone));
            foreach (RecentRow row in new[] { green, gone }) {
                string help = Avalonia.Automation.AutomationProperties.GetHelpText(row)!;
                Assert.DoesNotContain("Backspace", help);
                Assert.Contains("Delete Graph: Command Delete.", help);
                Assert.Contains("Command Down Arrow", help);
            }
        }

        [AvaloniaFact]
        public void TabOrder_NewGraph_Open_Presets_ShowAtLaunch_Filter_Recents_ThenCancelWhileLoading() {
            using var env = new Env();
            for (int i = 0; i < 9; i++)
                env.AddRow("Graph " + i, Vanilla.Name);
            env.Load = async (_, _, ct) => {
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            };
            WelcomeWindow window = env.Open();
            Control[] idle = [window.NewGraphButton, window.OpenButton, window.PresetList, window.ShowAtLaunchCheckBox, window.FilterField, window.RecentList];

            window.NewGraphButton.Focus(NavigationMethod.Tab);
            Assert.True(idle[0].IsKeyboardFocusWithin);
            for (int i = 1; i < idle.Length; i++) {
                CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.True(idle[i].IsKeyboardFocusWithin, $"Tab {i} should reach {idle[i].GetType().Name}, reached {window.FocusManager.GetFocusedElement()}");
            }

            window.PresetList.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.NewGraphButton.IsEnabled);
            CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);
            Assert.True(window.CancelButton.IsKeyboardFocusWithin, $"Tab should reach Cancel, reached {window.FocusManager.GetFocusedElement()}");
        }

        private static MainWindow ShellWindow(LibraryFixture library, bool welcomeAtLaunch) {
            var window = library.Track(new MainWindow());
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            window.Settings = new AppSettings { CurrentPresetName = Vanilla.Name, ShowWelcomeAtLaunch = welcomeAtLaunch };
            window.SettingsService = new SettingsService(library.NewTempDir());
            window.Library = library.Service;
            return window;
        }

        [AvaloniaFact]
        public void ClosingMainWindow_WelcomeOn_ShowsWelcome_WelcomeOff_NoWindowAppStillRuns() {
            using var library = new LibraryFixture();
            WelcomeLauncher.Current?.Close();

            MainWindow withWelcome = ShellWindow(library, welcomeAtLaunch: true);
            withWelcome.Close();
            Dispatcher.UIThread.RunJobs();
            WelcomeWindow shown = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
            Assert.True(shown.IsVisible);
            Assert.False(withWelcome.IsVisible);
            shown.Close();
            Assert.Null(WelcomeLauncher.Current);

            MainWindow withoutWelcome = ShellWindow(library, welcomeAtLaunch: false);
            withoutWelcome.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(WelcomeLauncher.Current);
            Assert.False(withoutWelcome.IsVisible);
            Assert.False(ShellHost.HasLiveMainWindow);
        }

        [AvaloniaFact]
        public async Task LibraryFixture_Dispose_LeavesNoWelcomeWindowNoShellContextAndNeverRecreatesTheDatabase() {
            ShellHost.Context = null;
            var library = new LibraryFixture();
            WelcomeLauncher.Current?.Close();
            ShellWindow(library, welcomeAtLaunch: true);

            library.Dispose();
            AppearanceHub.Current!.Refresh();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(WelcomeLauncher.Current);
            Assert.False(Directory.Exists(library.Root));
            Assert.True(ShellHost.Context is null || !ReferenceEquals(ShellHost.Context.Library, library.Service));

            ShellHost.Sync();
            await ShellHost.ReopenAsync();
            await ShellHost.LiveWindowAsync();

            Assert.False(Directory.Exists(library.Root));
            Assert.Null(WelcomeLauncher.Current);
        }

        [AvaloniaFact]
        public async Task UntouchedNewGraphRow_IsPrunedWhenItsWindowCloses_EveryOtherRowStays() {
            using var library = new LibraryFixture();
            MainWindow window = ShellWindow(library, welcomeAtLaunch: false);
            await window.NewGraphAsync();
            Guid untouched = library.Service.OpenGraphId!.Value;
            Assert.NotNull(library.Store.Get(untouched));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(library.Store.Get(untouched));

            async Task<Guid> ClosedWithNewGraph(Action<Guid> change) {
                MainWindow shell = ShellWindow(library, welcomeAtLaunch: false);
                await shell.NewGraphAsync();
                Guid id = library.Service.OpenGraphId!.Value;
                change(id);
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                return id;
            }

            Guid edited = await ClosedWithNewGraph(id => {
                library.Clock.Advance(TimeSpan.FromMinutes(1));
                library.Store.WriteAutosave(id, "{}", 0, null, library.Service.Now);
            });
            Guid withNodes = await ClosedWithNewGraph(id => library.Store.WriteAutosave(id, "{}", 1, null, library.Service.Now));
            Guid saved = await ClosedWithNewGraph(id => library.Store.AddVersion(id, GraphVersionKind.Saved, library.Service.Now));
            Guid duplicate = Guid.Empty;
            Guid original = await ClosedWithNewGraph(_ => duplicate = library.Service.Duplicate("copy").Id);
            MainWindow importer = ShellWindow(library, welcomeAtLaunch: false);
            Guid imported = library.Service.Create("Imported", Vanilla.Name, "{}", 0, "/graphs/empty.fjson").Id;
            importer.Close();
            Dispatcher.UIThread.RunJobs();

            foreach (Guid id in new[] { edited, withNodes, saved, original, duplicate, imported })
                Assert.NotNull(library.Store.Get(id));
        }

        private const string VanillaName = "Factorio 2.0 Vanilla";
        private const string SpaceAgeName = "Factorio 2.0 Space Age";
        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static readonly Dictionary<string, DataCache> Caches = [];

        private static async Task<DataCache> CacheAsync(string presetName) {
            await CacheGate.WaitAsync();
            try {
                if (!Caches.TryGetValue(presetName, out DataCache? cache)) {
                    cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(presetName, presetName == VanillaName, presetName == VanillaName), new Progress<KeyValuePair<int, string>>());
                    Caches[presetName] = cache;
                }
                return cache;
            } finally {
                CacheGate.Release();
            }
        }

        private static MainWindow WiredWindow(LibraryFixture library, DataCache cache) {
            var window = library.Track(new MainWindow());
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = cache.PresetName!, ShowWelcomeAtLaunch = false };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: library.NewTempDir());
            window.SettingsService = new SettingsService(library.NewTempDir());
            window.Library = library.Service;
            window.ApplyLoadedSettings();
            return window;
        }

        private static string EmptyGraphJson(string savedPresetName) =>
            Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = savedPresetName,
                IncludedMods = new Dictionary<string, string>(),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(new Foreman.ProductionGraph()),
            });

        private static string GraphJsonFor(DataCache cache) =>
            Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = cache.PresetName,
                IncludedMods = new Dictionary<string, string>(cache.IncludedMods),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(new Foreman.ProductionGraph()),
            });

        [AvaloniaFact]
        public async Task OpeningARecent_WithAnotherPreset_LoadsThatGraphsOwnPreset() {
            using var library = new LibraryFixture();
            DataCache vanilla = await CacheAsync(VanillaName);
            DataCache spaceAge = await CacheAsync(SpaceAgeName);
            MainWindow main = WiredWindow(library, vanilla);
            GraphRow row = library.Store.Create("Space Platform Loop", SpaceAgeName, GraphJsonFor(spaceAge), 0, null, library.Service.Now);
            var requested = new List<string>();
            var notices = new List<string>();
            main.PresetSwitchInfoStub = message => { notices.Add(message); return Task.CompletedTask; };
            main.PresetSelectionDialogStub = _ => throw new InvalidOperationException("picker opened");
            main.DataLoadStub = (preset, _) => {
                requested.Add(preset.Name);
                return Task.FromResult(new DataLoadResult(spaceAge, DataLoadOutcome.Loaded));
            };

            bool opened = await WelcomeLauncher.OpenGraphAsync(main.ShellContext!, row, null);

            Assert.True(opened);
            Assert.Equal([SpaceAgeName], requested);
            Assert.Empty(notices);
            Assert.Equal(SpaceAgeName, main.DataCache!.PresetName);
            Assert.Equal(SpaceAgeName, main.Settings!.CurrentPresetName);
            Assert.Equal(row.Id, library.Service.OpenGraphId);
        }

        [AvaloniaFact]
        public async Task MissingPresetRow_ChoosePreset_OpensWithTheChosenPresetWithoutAskingAgain_AndWritesTheRowOnlyAfterTheLoad() {
            using var library = new LibraryFixture();
            DataCache vanilla = await CacheAsync(VanillaName);
            DataCache spaceAge = await CacheAsync(SpaceAgeName);
            MainWindow main = WiredWindow(library, vanilla);
            GraphRow row = library.Store.Create("Space Platform Loop", "Gone Preset", EmptyGraphJson("Gone Preset"), 0, null, library.Service.Now);
            int pickers = 0;
            main.PresetSelectionDialogStub = _ => { pickers++; return Task.FromResult<Preset?>(null); };
            main.DataLoadStub = (_, _) => Task.FromResult(new DataLoadResult(spaceAge, DataLoadOutcome.Loaded));
            WelcomeWindow NewWelcome(Preset choice) {
                WelcomeWindow? created = null;
                created = new WelcomeWindow(WelcomeLauncher.BuildHost(main.ShellContext!, () => created)) {
                    ChoosePresetOverride = _ => Task.FromResult<Preset?>(choice),
                };
                created.Show();
                Dispatcher.UIThread.RunJobs();
                return created;
            }

            WelcomeWindow welcome = NewWelcome(new Preset(SpaceAgeName, false, false));
            Assert.True(welcome.ViewModel.Rows.Single().Unavailable);

            await welcome.ChoosePresetAsync(welcome.ViewModel.Rows.Single());

            Assert.Equal(0, pickers);
            Assert.Equal(row.Id, library.Service.OpenGraphId);
            Assert.Equal(SpaceAgeName, main.DataCache!.PresetName);
            Assert.Equal(SpaceAgeName, library.Store.Get(row.Id)!.PresetName);

            GraphRow failing = library.Store.Create("Failing", "Gone Preset", EmptyGraphJson("Gone Preset"), 0, null, library.Service.Now);
            main.DataLoadStub = (_, _) => Task.FromResult(new DataLoadResult(null, DataLoadOutcome.Cancelled));
            library.Service.Detach();
            welcome = NewWelcome(new Preset(VanillaName, true, true));

            await welcome.ChoosePresetAsync(welcome.ViewModel.Rows.Single(r => r.Id == failing.Id));

            Assert.Null(library.Service.OpenGraphId);
            Assert.Equal("Gone Preset", library.Store.Get(failing.Id)!.PresetName);
            Assert.True(welcome.IsVisible);
            welcome.Close();
        }

        [AvaloniaFact]
        public async Task WelcomePresetChoice_BecomesTheShellPreset_SoNewGraphInTheMainWindowUsesIt() {
            using var library = new LibraryFixture();
            DataCache vanilla = await CacheAsync(VanillaName);
            DataCache spaceAge = await CacheAsync(SpaceAgeName);
            MainWindow main = WiredWindow(library, vanilla);
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(spaceAge);
            try {
                var welcome = new WelcomeWindow(WelcomeLauncher.BuildHost(main.ShellContext!, () => null));
                welcome.Show();
                await welcome.ViewModel.SelectPresetAsync(new Preset(SpaceAgeName, false, false));
                welcome.Close();
                Assert.Equal(VanillaName, main.DataCache!.PresetName);

                await main.NewGraphAsync();

                Assert.Equal(SpaceAgeName, main.DataCache!.PresetName);
                Assert.Equal(SpaceAgeName, library.Store.Get(library.Service.OpenGraphId!.Value)!.PresetName);
            } finally {
                WelcomeLauncher.CacheLoaderOverride = null;
            }
        }

        private static NativeMenuItem AppItem(string header) =>
            NativeMenu.GetMenu(Application.Current!)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);

        private static async Task PumpAsync() {
            for (int i = 0; i < 20; i++) {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
        }

        [AvaloniaFact]
        public async Task WelcomePresetChoice_SurvivesTheMainWindowClosing_SoNewGraphFromTheAppMenuUsesIt() {
            using var library = new LibraryFixture();
            WelcomeLauncher.Current?.Close();
            DataCache vanilla = await CacheAsync(VanillaName);
            DataCache spaceAge = await CacheAsync(SpaceAgeName);
            MainWindow main = WiredWindow(library, vanilla);
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(spaceAge);
            try {
                var welcome = new WelcomeWindow(WelcomeLauncher.BuildHost(main.ShellContext!, () => null));
                welcome.Show();
                await welcome.ViewModel.SelectPresetAsync(new Preset(SpaceAgeName, false, false));
                welcome.Close();
                main.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(ShellHost.HasLiveMainWindow);

                AppItem("New Graph").Command!.Execute(null);
                await PumpAsync();

                MainWindow created = ShellHost.Context!.Window!;
                Assert.NotSame(main, created);
                Assert.Equal(SpaceAgeName, created.DataCache!.PresetName);
                Assert.Equal(SpaceAgeName, library.Store.Get(library.Service.OpenGraphId!.Value)!.PresetName);
                created.Settings!.ShowWelcomeAtLaunch = false;
                created.Close();
            } finally {
                WelcomeLauncher.CacheLoaderOverride = null;
            }
        }

        [AvaloniaFact]
        public void WelcomeMenu_OffersNewOpenRecentAndWelcome_RunsThroughTheWelcomeFlow_AndDisablesWhileAPresetLoads() {
            using var env = new Env();
            env.AddRow("Older", Vanilla.Name);
            var gate = new TaskCompletionSource<bool>();
            env.Load = (_, _, _) => gate.Task;
            WelcomeWindow window = env.Open();
            NativeMenu menu = NativeMenu.GetMenu(window)!;
            NativeMenu file = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            NativeMenu windowMenu = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Window").Menu!;
            NativeMenuItem newGraph = file.Items.OfType<NativeMenuItem>().Single(i => i.Header == "New Graph");
            NativeMenuItem open = file.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Open…");
            NativeMenuItem recent = file.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Open Recent");
            NativeMenuItem welcome = windowMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Welcome to Foreman");

            Assert.Equal(new KeyGesture(Key.N, PlatformModifiers.Primary), newGraph.Gesture);
            Assert.Equal(new KeyGesture(Key.O, PlatformModifiers.Primary), open.Gesture);
            Assert.Equal(new KeyGesture(Key.D1, PlatformModifiers.Primary | KeyModifiers.Shift), welcome.Gesture);
            Assert.All((NativeMenuItem[])[newGraph, open, recent, welcome], i => Assert.True(i.IsEnabled, i.Header));

            window.PresetList.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.All((NativeMenuItem[])[newGraph, open, recent], i => Assert.False(i.IsEnabled, i.Header));
            Assert.True(welcome.IsEnabled);
            newGraph.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("new", env.Calls);

            gate.SetResult(true);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.All((NativeMenuItem[])[newGraph, open, recent], i => Assert.True(i.IsEnabled, i.Header));

            newGraph.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("new", env.Calls);
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public void NewGraphFromMenu_CalledTwiceWhileOneIsInFlight_RunsTheHostOnce() {
            using var env = new Env();
            var gate = new TaskCompletionSource<bool>();
            env.NewGraphGate = gate.Task;
            WelcomeWindow window = env.Open();

            Task<bool> first = window.NewGraphFromMenuAsync();
            Task<bool> second = window.NewGraphFromMenuAsync();
            gate.SetResult(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(env.Calls, "new");
            Assert.True(first.IsCompletedSuccessfully);
            Assert.False(second.Result);
        }
    }
}
