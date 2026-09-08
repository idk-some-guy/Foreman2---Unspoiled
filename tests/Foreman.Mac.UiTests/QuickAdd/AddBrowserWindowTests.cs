using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System.Linq;
using System.Reflection;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    public sealed class AddBrowserWindowTests {
        private static (AddBrowserWindow win, AddBrowserViewModel vm) Open(AppSettings? settings = null) {
            var fx = CatalogFixture.WithGroups();
            settings ??= new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            return (win, vm);
        }

        [AvaloniaFact]
        public void TitleSegmentsAndDefaultGroup() {
            var (win, vm) = Open();

            Assert.Equal("Add to Graph", win.Title);
            Assert.Equal(BrowserSegment.Items, vm.Segment);
            Assert.Equal(["Logistics", "Production"], vm.Groups.Select(g => g.Name));
            Assert.True(vm.Groups[0].IsSelected);
            Assert.Equal(["Storage", "Belt"], vm.Sections.Select(s => s.Title));
            Assert.Equal(["Wooden Chest", "Iron Chest"], vm.Sections[0].Cells.Select(c => c.Label));
        }

        [AvaloniaFact]
        public void SwitchingSegmentShowsRecipes_AndRemembersGroupPerSegment() {
            var (_, vm) = Open();

            vm.SelectGroup(vm.Groups[1].Group);
            vm.Segment = BrowserSegment.Recipes;
            Assert.Equal("Search recipes…", vm.SearchPlaceholder);
            Assert.All(vm.Sections.SelectMany(s => s.Cells), c => Assert.Equal(AddKind.Recipe, c.Candidate.Kind));

            vm.Segment = BrowserSegment.Items;
            Assert.True(vm.Groups[1].IsSelected);
        }

        //AddCatalog.Search ties-break by FriendlyName, so "chest" returns Iron before Wooden alphabetically.
        [AvaloniaFact]
        public void SearchFiltersAcrossAllGroups_AndClearingRestoresTheGroupView() {
            var (_, vm) = Open();

            vm.SearchText = "chest";
            Assert.Equal(["Iron Chest", "Wooden Chest"], vm.Sections.SelectMany(s => s.Cells).Select(c => c.Label));

            vm.SearchText = "";
            Assert.Equal(["Storage", "Belt"], vm.Sections.Select(s => s.Title));
        }

        [AvaloniaFact]
        public void TogglesAreLive_AndPersistToSettings() {
            var settings = new AppSettings { ShowHidden = false };
            var (_, vm) = Open(settings);

            vm.Segment = BrowserSegment.Recipes;
            vm.SelectGroup(vm.Groups[1].Group);
            int before = vm.Sections.Sum(s => s.Cells.Count);

            vm.ShowHidden = true;

            Assert.Equal(before + 1, vm.Sections.Sum(s => s.Cells.Count));
            Assert.True(settings.ShowHidden);
        }

        [AvaloniaFact]
        public void QualitySelectListsAvailableQualities_WithTheMockLabel() {
            var fx = CatalogFixture.WithQualities(["normal", "legendary"]);
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, new AppSettings()), fx.Cache, new AppSettings(), (_, _) => null);

            vm.Quality = vm.Qualities[1];

            Assert.Equal("Quality: Legendary", vm.QualityLabel);
        }

        //Mirrors the retired on-canvas chooser's quality picker (ported from its own population test).
        [AvaloniaFact]
        public void QualitySelectExcludesDisabledQualities() {
            var fx = CatalogFixture.WithQualities(["normal", "epic", "legendary"], disabled: ["legendary"]);
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, new AppSettings()), fx.Cache, new AppSettings(), (_, _) => null);

            Assert.Equal(["Normal", "Epic"], vm.Qualities.Select(q => q.FriendlyName));
        }

        [AvaloniaFact]
        public void CellsAreMockSized_UnavailableDimmed_SelectedRinged() {
            var (win, vm) = Open(new AppSettings { ShowUnavailable = true });

            var cellButtons = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            Assert.All(cellButtons, b => Assert.Equal(40, b.FindDescendantOfType<Border>()!.Bounds.Width, 0.5));

            var unavailable = cellButtons.Single(b => ((BrowserCell)b.DataContext!).IsAvailable == false);
            Assert.Equal(0.45, unavailable.Opacity, 0.01);

            vm.SelectCell((BrowserCell)cellButtons[0].DataContext!);
            Assert.Contains("selected", cellButtons[0].Classes);

            //R12: a long label wraps to at most 2 lines, ellipsizing rather than growing the cell.
            var label = cellButtons[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("foreman-browser-cell-label"));
            Assert.Equal(2, label.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        }

        [AvaloniaFact]
        public void SelectionSurvivesRefresh_AndClearsWhenTheGroupChangesItAway() {
            var (win, vm) = Open();
            Dispatcher.UIThread.RunJobs();

            var woodenChestButton = win.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("foreman-browser-cell") && ((BrowserCell)b.DataContext!).Label == "Wooden Chest");
            vm.SelectCell((BrowserCell)woodenChestButton.DataContext!);

            vm.SearchText = "chest";
            Dispatcher.UIThread.RunJobs();
            vm.SearchText = "";
            Dispatcher.UIThread.RunJobs();

            var survivingButton = win.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("foreman-browser-cell") && ((BrowserCell)b.DataContext!).Label == "Wooden Chest");
            Assert.Contains("selected", survivingButton.Classes);
            Assert.Equal("Wooden Chest", vm.SelectedCell?.Label);

            vm.SelectGroup(vm.Groups[1].Group);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.SelectedCell);
        }

        [AvaloniaFact]
        public void MockFidelity_SidebarGroupSubheadAndSearchField() {
            var (win, _) = Open();
            Dispatcher.UIThread.RunJobs();

            var sidebar = win.FindControl<Border>("Sidebar")!;
            Assert.Equal(new Thickness(10, 8), sidebar.Padding);

            var groupButtons = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-group")).ToList();
            Assert.All(groupButtons, b => Assert.Equal(new CornerRadius(6), b.CornerRadius));
            Assert.All(groupButtons, b => Assert.Equal(new Thickness(6, 8), b.Padding));
            Assert.All(groupButtons, b => Assert.Equal(12.5, b.FontSize));
            var selectedGroup = groupButtons.Single(b => b.Classes.Contains("selected"));
            Assert.Equal(FontWeight.SemiBold, selectedGroup.FontWeight);

            var subhead = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("foreman-browser-subhead"));
            Assert.Equal("STORAGE", subhead.Text);
            Assert.Equal(11, subhead.FontSize);
            Assert.Equal(0.44, subhead.LetterSpacing, 0.01);
            Assert.Equal(10, subhead.Margin.Bottom);

            var searchField = win.FindControl<TextBox>("SearchBox")!;
            Assert.Equal(new CornerRadius(7), searchField.CornerRadius);
            Assert.Equal(12.5, searchField.FontSize);
        }

        [AvaloniaFact]
        public void MainWindowOwnsOneBrowser_ReopensSeeded_AndCloseKeepsTheInstance() {
            using var shell = ShellFixture.Open();

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "gear"));
            var first = shell.Window.BrowserWindow!;
            Assert.True(first.IsVisible);
            Assert.Equal(BrowserSegment.Recipes, ((AddBrowserViewModel)first.DataContext!).Segment);
            Assert.Equal("gear", ((AddBrowserViewModel)first.DataContext!).SearchText);

            first.Close();

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items));
            Assert.Same(first, shell.Window.BrowserWindow);
            Assert.True(first.IsVisible);
        }

        [AvaloniaFact]
        public void BrowserOpensOnTheGraphsDefaultAssemblerQuality() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            DataCache cache = shell.Window.DataCache!;
            shell.Window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.Qualities["legendary"];

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items));

            Assert.Equal("legendary", shell.BrowserVm.Quality!.Name);
        }

        [AvaloniaFact]
        public void OwnerClosingOrAppShutdownIsNotCancelled() {
            var (win, _) = Open();
            var onClosing = typeof(AddBrowserWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            //WindowClosingEventArgs's own constructor is internal to Avalonia.Controls - reflection is the
            //only way to build one from here to drive OnClosing directly with a specific CloseReason.
            var ctor = typeof(WindowClosingEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(WindowCloseReason), typeof(bool)])!;

            var ownerClosing = (WindowClosingEventArgs)ctor.Invoke([WindowCloseReason.OwnerWindowClosing, true]);
            onClosing.Invoke(win, [ownerClosing]);
            Assert.False(ownerClosing.Cancel);

            var appShutdown = (WindowClosingEventArgs)ctor.Invoke([WindowCloseReason.ApplicationShutdown, true]);
            onClosing.Invoke(win, [appShutdown]);
            Assert.False(appShutdown.Cancel);
        }
    }
}
