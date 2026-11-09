using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Models.Nodes;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    [Collection(ScaleTestGroup.Name)]
    public sealed class AddBrowserWindowTests {
        private static (AddBrowserWindow win, AddBrowserViewModel vm) Open(AppSettings? settings = null) {
            WindowScaler.SetScale(1.0);
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

        //The browser's search box lives in its own window, never a descendant of
        //GraphCanvasControl, so the palette's KeyDown-swallowing regression never touched it - this pins
        //that down with the same real-keyboard-path check the palette itself now gets.
        [AvaloniaFact]
        public void TypingChestThroughTheRealWindowKeyboardPath_FillsTheSearchBox() {
            var (win, vm) = Open();
            var searchField = win.FindControl<TextBox>("SearchBox")!;
            searchField.Focus();

            foreach (char letter in "chest") {
                win.KeyPressQwerty(CanvasKeys.PhysicalKeyFor(letter), RawInputModifiers.None);
                win.KeyTextInput(letter.ToString());
            }

            Assert.Equal("chest", vm.SearchText);
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

            UnavailableCell_IsDimmed_AndADoubleClickStillAddsIt();
        }

        //A22 amended: opacity means "unavailable", never "inert".
        private static void UnavailableCell_IsDimmed_AndADoubleClickStillAddsIt() {
            var settings = new AppSettings { ShowUnavailable = true };
            var (win, vm) = Open(settings);
            var picks = new List<BrowserPick>();
            vm.PlaceRequested += (_, pick) => picks.Add(pick);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            ChooserCell steel = win.GetVisualDescendants().OfType<ChooserCell>().Single(c => c.FullName == "Steel Chest");
            ChooserCell iron = win.GetVisualDescendants().OfType<ChooserCell>().Single(c => c.FullName == "Iron Chest");
            Border Frame(ChooserCell cell) => cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? unavailable);
            Assert.True(steel.IsUnavailable);
            Assert.Equal(Assert.IsType<double>(unavailable), Frame(steel).Opacity);
            Assert.False(iron.IsUnavailable);
            Assert.Equal(1, Frame(iron).Opacity);

            RealClick.DoubleClick(steel);

            BrowserPick pick = Assert.Single(picks);
            Assert.Equal("Steel Chest", pick.Candidate.Name);
            Assert.Equal(NodeType.Supplier, pick.Role);
        }

        [AvaloniaFact]
        public void QualitySelectListsAvailableQualities_WithTheMockLabel() {
            var fx = CatalogFixture.WithQualities(["normal", "legendary"]);
            var settings = new AppSettings();
            var bakes = new List<(SkiaSharp.SKBitmap Bitmap, int Size, IImage Image)>();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (bitmap, size) => {
                var image = new DrawingImage(new GeometryDrawing());
                bakes.Add((bitmap, size, image));
                return image;
            });
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            PopUp quality = win.FindControl<PopUp>("QualityPopUp")!;

            Assert.Equal(["Normal", "Legendary"], quality.Items);
            Assert.Equal(quality.Items.Count, quality.ItemIcons.Count);
            for (int i = 0; i < quality.Items.Count; i++)
                Assert.Contains(bakes, b => ReferenceEquals(b.Image, quality.ItemIcons[i]) && ReferenceEquals(b.Bitmap, vm.Qualities[i].Icon) && b.Size == 16);
            Assert.Same(quality.ItemIcons[0], quality.Icon);
            vm.Quality = vm.Qualities[1];
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Quality: Legendary", quality.DisplayText);
            Assert.Equal("Legendary", quality.SelectedItem);
            Assert.Same(quality.ItemIcons[1], quality.Icon);
            quality.Command!.Execute("Normal");
            Assert.Same(vm.Qualities[0], vm.Quality);
        }

        //Mirrors the retired on-canvas chooser's quality picker (ported from its own population test).
        [AvaloniaFact]
        public void QualitySelectExcludesDisabledQualities() {
            var fx = CatalogFixture.WithQualities(["normal", "epic", "legendary"], disabled: ["legendary"]);
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, new AppSettings()), fx.Cache, new AppSettings(), (_, _) => null);

            Assert.Equal(["Normal", "Epic"], vm.Qualities.Select(q => q.FriendlyName));
        }

        [AvaloniaFact]
        public void SelectionSurvivesRefresh_AndClearsWhenTheGroupChangesItAway() {
            var (win, vm) = Open();
            Dispatcher.UIThread.RunJobs();

            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Label == "Wooden Chest"));

            vm.SearchText = "chest";
            Dispatcher.UIThread.RunJobs();
            vm.SearchText = "";
            Dispatcher.UIThread.RunJobs();

            ChooserCell surviving = win.GetVisualDescendants().OfType<ChooserCell>().Single(c => c.FullName == "Wooden Chest");
            Assert.True(surviving.IsSelected);
            Assert.Equal("Wooden Chest", vm.SelectedCell?.Label);

            vm.SelectGroup(vm.Groups[1].Group);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.SelectedCell);
        }

        //An unresolved Factorio locale placeholder becomes a readable phrase; a normal
        //FriendlyName (distinct from the raw internal Name) is left alone.
        [Fact]
        public void HumanizeStripsAnUnresolvedLocalePlaceholder_AndLeavesARealFriendlyNameAlone() {
            Assert.Equal("Extraction items", AddBrowserViewModel.Humanize("§§SG:EXTRACTION_ITEMS", "extraction-items"));
            Assert.Equal("Storage", AddBrowserViewModel.Humanize("Storage", "storage"));
        }

        //The placeholder's own kind marker ("SG:", "G:", any case) sits right after "§§" and
        //must go too, not just the "§§" itself - a colon elsewhere in a real name is left alone.
        [Fact]
        public void HumanizeStripsTheKindMarkerAfterTheLocalePlaceholder_CaseInsensitively() {
            Assert.Equal("Extraction items", AddBrowserViewModel.Humanize("§§sg:extraction_items", "extraction-items"));
            Assert.Equal("Extra group", AddBrowserViewModel.Humanize("§§g:extra_group", "extra-group"));
            Assert.Equal("Mod: Things", AddBrowserViewModel.Humanize("Mod: Things", "mod-things"));
        }

        //A localised FriendlyName can carry embedded newlines - Humanize collapses any
        //whitespace run to one space regardless of the source.
        [Fact]
        public void HumanizeCollapsesEmbeddedWhitespaceRuns_IncludingNewlines() {
            Assert.Equal("Foo Bar Baz", AddBrowserViewModel.Humanize("Foo\nBar\r\n  Baz", "foo-bar-baz"));
        }

        //A subgroup whose FriendlyName is Factorio's own unresolved locale key surfaces as a readable
        //heading instead of the raw "§§SG:..." text.
        [AvaloniaFact]
        public void SubheadHumanizesAnUnresolvedLocalePlaceholder() {
            var fx = CatalogFixture.WithSubgroupNamed("§§SG:EXTRACTION_ITEMS");
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);

            Assert.Contains("Extraction items", vm.Sections.Select(s => s.Title));
        }

        //A group whose FriendlyName is Factorio's own unresolved locale key surfaces in
        //the sidebar title-cased, not merely sentence-cased the way Humanize alone would leave it - the
        //sidebar shows group names as-is, so HumanizeGroupName title-cases on top of Humanize's own output.
        [AvaloniaFact]
        public void SidebarGroupNameIsTitleCased_ForAnUnresolvedLocalePlaceholder() {
            var fx = CatalogFixture.WithGroupNamed("§§SG:EXTRACTION_ITEMS");
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);

            Assert.Contains("Extraction Items", vm.Groups.Select(g => g.Name));
        }

        [AvaloniaFact]
        public void SidebarWidthFitsTheWidestGroupName_CappedWithATrimmedTooltippedLabel() {
            const string longName = "Intermediate products with a very long name";
            var fx = CatalogFixture.WithGroupNamed(longName);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var sidebar = win.FindControl<Border>("Sidebar")!;
            Assert.Equal(320, sidebar.Width);

            var label = win.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is BrowserGroup g && g.Name == longName);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(longName, ToolTip.GetTip(label.FindAncestorOfType<SidebarRow>()!)?.ToString());
        }

        //A name landing strictly between the floor and the cap must still fit uncrimped.
        [AvaloniaFact]
        public void SidebarWidthFitsAMidBandGroupName_NoClippingNoTooltip() {
            const string midName = "Fluid power";
            var fx = CatalogFixture.WithGroupNamed(midName);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var sidebar = win.FindControl<Border>("Sidebar")!;
            Assert.InRange(sidebar.Width, 168, 320);

            var label = win.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is BrowserGroup g && g.Name == midName);
            Assert.Null(ToolTip.GetTip(label.FindAncestorOfType<SidebarRow>()!));

            //Window-relative, same as GridCellsShareAUniformHeight below - Bounds is parent-relative.
            double labelRight = Avalonia.VisualExtensions.TranslatePoint(label, new Point(0, 0), win)!.Value.X + label.Bounds.Width;
            double sidebarRight = Avalonia.VisualExtensions.TranslatePoint(sidebar, new Point(0, 0), win)!.Value.X + sidebar.Bounds.Width;
            Assert.True(labelRight <= sidebarRight, $"label right edge ({labelRight}) overflows the sidebar ({sidebarRight})");
        }

        [AvaloniaFact]
        public void SidebarWidthStaysAtTheFloor_WhenGroupNamesAreShort() {
            var fx = CatalogFixture.WithGroupNamed("Ore");
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var sidebar = win.FindControl<Border>("Sidebar")!;
            Assert.Equal(168, sidebar.Width);
        }

        [AvaloniaFact]
        public void SidebarTooltip_StaysCorrect_AfterSegmentSwitchAndShowUnavailableToggle() {
            const string longName = "Intermediate products with a very long name";
            var fx = CatalogFixture.WithGroupNamed(longName);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            //Recipes has no cells in this fixture, so the switch empties Groups, then restoring Items
            //repopulates it with fresh BrowserGroup instances.
            vm.Segment = BrowserSegment.Recipes;
            Dispatcher.UIThread.RunJobs();
            vm.Segment = BrowserSegment.Items;
            Dispatcher.UIThread.RunJobs();

            var label = win.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is BrowserGroup g && g.Name == longName);
            Assert.Equal(longName, ToolTip.GetTip(label.FindAncestorOfType<SidebarRow>()!)?.ToString());

            vm.ShowUnavailable = true;
            Dispatcher.UIThread.RunJobs();

            label = win.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is BrowserGroup g && g.Name == longName);
            Assert.Equal(longName, ToolTip.GetTip(label.FindAncestorOfType<SidebarRow>()!)?.ToString());
        }

        [AvaloniaFact]
        public void SidebarWidthFitsTheRealSpaceAgePresetGroupNames_NoRightEdgeClipping() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items));
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            var sidebar = window.FindControl<Border>("Sidebar")!;
            double sidebarLeft = Avalonia.VisualExtensions.TranslatePoint(sidebar, new Point(0, 0), window)!.Value.X;
            double sidebarRight = sidebarLeft + sidebar.Bounds.Width;

            var labels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.DataContext is BrowserGroup).ToList();
            Assert.NotEmpty(labels);
            Assert.Contains(labels, l => ((BrowserGroup)l.DataContext!).Name == "Intermediate products");
            foreach (TextBlock label in labels) {
                double left = Avalonia.VisualExtensions.TranslatePoint(label, new Point(0, 0), window)!.Value.X;
                double right = left + label.Bounds.Width;
                Assert.True(left >= sidebarLeft + sidebar.Padding.Left - 0.5,
                    $"'{((BrowserGroup)label.DataContext!).Name}' left edge ({left}) is left of the sidebar's padded edge ({sidebarLeft + sidebar.Padding.Left})");
                Assert.True(right <= sidebarRight - sidebar.Padding.Right + 0.5,
                    $"'{((BrowserGroup)label.DataContext!).Name}' right edge ({right}) overflows the sidebar's padded edge ({sidebarRight - sidebar.Padding.Right})");
            }

            //Bounds-fitting alone doesn't catch this - a trimmed label still stays inside the
            //sidebar via ellipsis. "Intermediate products" must actually fit, not just get clipped to fit.
            TextBlock intermediateProducts = labels.First(l => ((BrowserGroup)l.DataContext!).Name == "Intermediate products");
            Assert.Null(ToolTip.GetTip(intermediateProducts.FindAncestorOfType<SidebarRow>()!));
        }

        //The widest bundled group name must render untrimmed at every UI scale.
        [AvaloniaFact]
        public void SidebarWidthFitsIntermediateProductsUntrimmed_AtEveryUiScale() {
            WindowScaler.SetScale(1.0);
            try {
                var fx = CatalogFixture.WithGroupNamed("Intermediate products");
                var settings = new AppSettings();
                var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
                var win = new AddBrowserWindow { DataContext = vm };
                win.Show();
                Dispatcher.UIThread.RunJobs();

                foreach (double scale in new[] { 1.0, 1.5, 2.0 }) {
                    WindowScaler.SetScale(scale);
                    Dispatcher.UIThread.RunJobs();

                    var sidebar = win.FindControl<Border>("Sidebar")!;
                    var label = win.GetVisualDescendants().OfType<TextBlock>()
                        .First(t => t.DataContext is BrowserGroup g && g.Name == "Intermediate products");

                    Assert.Null(ToolTip.GetTip(label.FindAncestorOfType<SidebarRow>()!));

                    //Both corners go through TranslatePoint (not origin + local Bounds.Width) - at scale != 1
                    //the ambient LayoutTransform means a translated origin and a local width live in different
                    //units, so adding them crosses coordinate spaces.
                    double labelRight = Avalonia.VisualExtensions.TranslatePoint(label, new Point(label.Bounds.Width, 0), win)!.Value.X;
                    double sidebarRight = Avalonia.VisualExtensions.TranslatePoint(sidebar, new Point(sidebar.Bounds.Width, 0), win)!.Value.X;
                    Assert.True(labelRight <= sidebarRight + 0.5, $"at scale {scale}, label right edge ({labelRight}) overflows the sidebar ({sidebarRight})");
                }
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        //The Foreman-internal "extra" group's FriendlyName used to join three category
        //names with embedded newlines; it's a single line now (DataCacheBootstrap), but this stays as a
        //defense-in-depth check that no sidebar row or subgroup heading ever renders a raw newline.
        [AvaloniaFact]
        public void GroupNamesWithEmbeddedNewlinesRenderOnOneLine_InTheRealSpaceAgePreset() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.Settings!.ShowUnavailable = true;
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes));
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            var groupLabels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.DataContext is BrowserGroup).ToList();
            var headings = window.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("chooser-heading"))
                .SelectMany(p => p.Children.OfType<TextBlock>()).ToList();
            Assert.NotEmpty(groupLabels);

            foreach (TextBlock label in groupLabels.Concat(headings)) {
                string text = label.Text ?? "";
                Assert.False(text.Contains('\n') || text.Contains('\r'), $"'{text}' contains a raw newline");
            }

            var groupButtons = window.GetVisualDescendants().OfType<SidebarRow>().ToList();
            SidebarRow extraGroupButton = groupButtons.Single(b => ((BrowserGroup)b.DataContext!).Group.Name == "§§g:extra_group");
            double otherRowHeight = groupButtons.First(b => b != extraGroupButton).Bounds.Height;
            Assert.Equal(otherRowHeight, extraGroupButton.Bounds.Height, 0.5);
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

        //A link-drag's own "Add..." (radial Add.../the palette's Cmd+Enter after it)
        //always seeds a Link, so it must open on Recipes - a scope-less/link-less open (the toolbar,
        //background menu, double-click) still defaults to Items.
        [AvaloniaFact]
        public void LinkDragAddOpensOnRecipes_ScopeAllWithoutALinkStillOpensOnItems() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.All, link));
            Assert.Equal(BrowserSegment.Recipes, shell.BrowserVm.Segment);

            shell.BrowserVm.Segment = BrowserSegment.Items;
            shell.BrowserVm.SelectCell(shell.BrowserVm.Sections.SelectMany(s => s.Cells).First());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Output", shell.BrowserVm.SelectedRole.Label);
            Assert.Equal("Output", shell.Window.BrowserWindow!.FindControl<PopUp>("RolePopUp")!.SelectedItem);

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.All));
            Assert.Equal(BrowserSegment.Items, shell.BrowserVm.Segment);
        }

        [AvaloniaFact]
        public void BrowserOpensOnTheGraphsDefaultAssemblerQuality() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            DataCache cache = shell.Window.DataCache!;
            shell.Window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.Qualities["legendary"];

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items));

            Assert.Equal("legendary", shell.BrowserVm.Quality!.Name);
        }

        private static readonly string[] SevenRoles = ["Source", "Passthrough", "Output", "Spoil", "Unspoil", "Plant", "Unplant"];

        private static bool RoleShortcut(Control target, Key digit) {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = digit, KeyModifiers = PlatformModifiers.Primary };
            target.RaiseEvent(e);
            Dispatcher.UIThread.RunJobs();
            return e.Handled;
        }

        [AvaloniaFact]
        public void Browser_Footer_IsOneAddAsPopUp_ListingTheSevenRolesAtFixedPositions_WithAddAsTheDefaultButton() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            AddBrowserWindow window = shell.Window.BrowserWindow!;
            Dispatcher.UIThread.RunJobs();

            Border footer = window.FindControl<Border>("Footer")!;
            PopUp roles = window.FindControl<PopUp>("RolePopUp")!;
            ChooserGrid grid = window.FindControl<ChooserGrid>("Grid")!;
            ButtonRow buttons = footer.GetVisualDescendants().OfType<ButtonRow>().Single();
            Button add = buttons.ResolveDefaultButton()!;
            Assert.Contains(footer, roles.GetVisualAncestors());
            Assert.Equal("Add as", window.FindControl<TextBlock>("AddAsLabel")!.Text);
            Assert.Equal("Add as", Avalonia.Automation.AutomationProperties.GetName(roles));
            Assert.Equal(SevenRoles, roles.Items);
            Assert.All(roles.Items, label => Assert.DoesNotMatch("UnSpoil|UnPlant|Pass-Through|…", label));
            Assert.Equal(["Close", "Add"], buttons.Items.OfType<Button>().Select(b => b.Content));

            Assert.Null(vm.SelectedCell);
            Assert.False(roles.IsEnabled);
            Assert.Equal("Source", roles.DisplayText);
            Assert.Equal("Add", add.Content);
            Assert.False(add.IsEnabled);

            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Item));
            Dispatcher.UIThread.RunJobs();
            Assert.True(roles.IsEnabled);
            Assert.Equal(SevenRoles, roles.Items);
            Assert.Equal(["Spoil", "Unspoil", "Plant", "Unplant"], roles.DisabledItems);
            Assert.Equal("Source", roles.SelectedItem);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content is "Source" or "Passthrough" or "Output");
            Assert.Contains(footer.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == $"{vm.SelectedCell!.Label} selected");
            Assert.Equal("Add", add.Content);

            RealClick.On(add);
            Assert.Single(shell.Graph.Nodes.OfType<SupplierNode>());

            Assert.True(RoleShortcut(grid, Key.D4));
            Assert.Equal("Source", vm.SelectedRole.Label);
            Assert.True(RoleShortcut(grid, Key.D9));
            Assert.Equal("Source", roles.SelectedItem);
            Assert.True(RoleShortcut(grid, Key.D3));
            Assert.Equal("Output", vm.SelectedRole.Label);
            Assert.Equal("Output", roles.SelectedItem);
            Assert.Equal("Add", add.Content);
            RealClick.On(add);
            Assert.Single(shell.Graph.Nodes.OfType<ConsumerNode>());

            Assert.True(RoleShortcut(grid, Key.D2));
            Assert.Equal("Passthrough", roles.SelectedItem);
            ChooserCell cell = window.GetVisualDescendants().OfType<ChooserCell>().First(c => c.FullName == vm.SelectedCell!.Label);
            AnimationClock.Settle();
            RealClick.DoubleClick(cell);
            Assert.Single(shell.Graph.Nodes.OfType<PassthroughNode>());

            AddAs_RemembersTheLastPickedRole_AndFallsBackToSourceWhereItIsDimmed();
        }

        //The sticky default: the last explicit pick while the selection allows it, never overwritten by a fallback.
        private static void AddAs_RemembersTheLastPickedRole_AndFallsBackToSourceWhereItIsDimmed() {
            var fx = CatalogFixture.WithItemsAndRecipes(["seed", "plate"], []);
            ((ItemPrototype)fx.Cache.Items["seed"]).PlantResult = new PlantProcessPrototype(fx.Cache, "seed-planting");
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            PopUp roles = win.FindControl<PopUp>("RolePopUp")!;
            BrowserCell seed = vm.Sections.SelectMany(s => s.Cells).Single(c => c.Label == "seed");
            BrowserCell plate = vm.Sections.SelectMany(s => s.Cells).Single(c => c.Label == "plate");

            vm.SelectCell(seed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Source", roles.SelectedItem);
            roles.Command!.Execute("Plant");
            Assert.Equal("Plant", vm.SelectedRole.Label);

            vm.SelectCell(plate);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Source", roles.SelectedItem);
            Assert.Contains("Plant", roles.DisabledItems);
            Assert.True(RoleShortcut(win.FindControl<ChooserGrid>("Grid")!, Key.D6));
            Assert.Equal("Source", vm.SelectedRole.Label);

            vm.SelectCell(seed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Plant", roles.SelectedItem);
        }

        [AvaloniaFact]
        public void Browser_FilterMenu_HoldsTheThreeFormerSwitchesPlusReset_AndShowsFilledWhenNonDefault() {
            var settings = new AppSettings();
            var (win, vm) = Open(settings);
            MenuButton filter = win.FindControl<MenuButton>("FilterButton")!;

            Assert.Equal(["Show unavailable", "Show hidden", "Ignore assembler"], filter.Items.Select(i => i.Text));
            Assert.All(filter.Items, i => Assert.False(i.IsChecked));
            Assert.False(filter.IsFilled);
            Assert.Empty(win.GetVisualDescendants().OfType<ToggleSwitch>());

            CanvasKeys.KeyDownOn(filter, Key.Down);
            Assert.True(filter.IsOpen);
            Assert.Equal(["Show unavailable", "Show hidden", "Ignore assembler", "Reset to defaults"],
                filter.Menu.Items.Where(r => !r.IsSeparator).Select(r => r.Text));
            CanvasKeys.KeyDownOn(filter, Key.Enter);

            Assert.True(vm.ShowUnavailable);
            Assert.True(settings.ShowUnavailable);
            Assert.True(filter.IsFilled);
            Assert.Equal("Filter (1)", filter.DisplayLabel);
            Assert.True(filter.IsOpen);

            vm.ShowHidden = true;
            Assert.Equal("Filter (2)", filter.DisplayLabel);

            filter.Menu.HighlightedIndex = filter.Menu.Items.Count - 1;
            CanvasKeys.KeyDownOn(filter, Key.Enter);

            Assert.False(vm.ShowUnavailable);
            Assert.False(vm.ShowHidden);
            Assert.False(filter.IsFilled);
            Assert.False(filter.IsOpen);
        }

        [AvaloniaFact]
        public void Browser_ModeSwitch_LivesInTheToolbarRow_NotTheTitlebar() {
            var fx = CatalogFixture.WithQualities(["normal", "legendary"]);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(win.ExtendClientAreaToDecorationsHint);
            Border toolbar = win.FindControl<Border>("ToolbarRow")!;
            Segmented mode = win.FindControl<Segmented>("ModeSwitch")!;
            Assert.Equal(["Items", "Recipes"], mode.Items);

            Control[] row = [mode, win.FindControl<SearchField>("SearchBox")!, win.FindControl<PopUp>("QualityPopUp")!, win.FindControl<MenuButton>("FilterButton")!];
            Assert.All(row, c => Assert.Contains(toolbar, c.GetVisualAncestors()));
            double[] lefts = [.. row.Select(c => c.TranslatePoint(new Point(0, 0), win)!.Value.X)];
            Assert.Equal(lefts.OrderBy(x => x), lefts);

            mode.SelectedIndex = 1;
            Assert.Equal(BrowserSegment.Recipes, vm.Segment);
            vm.Segment = BrowserSegment.Items;
            Assert.Equal(0, mode.SelectedIndex);

            CanvasKeys.KeyDownOn(win, Key.F, PlatformModifiers.Primary);
            Assert.True(win.FindControl<SearchField>("SearchBox")!.IsFocused);
        }

        [AvaloniaFact]
        public void Browser_SearchResults_GroupUnderGroupDotSubgroupHeadings() {
            var (win, vm) = Open();
            ChooserGrid grid = win.FindControl<ChooserGrid>("Grid")!;

            vm.SearchText = "chest";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("chest", grid.Query);
            ChooserSection storage = Assert.Single(grid.Sections);
            Assert.Equal("Logistics", storage.Group);
            Assert.Equal("Storage", storage.Subgroup);
            Assert.Equal(["Logistics", "·", "Storage"], HeadingRuns(win).Single());
            Assert.All(win.GetVisualDescendants().OfType<ChooserCell>(), c => Assert.Equal(c.FullName, ToolTip.GetTip(c)));

            vm.SearchText = "";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([["Storage"], ["Belt"]], HeadingRuns(win));
        }

        private static List<string[]> HeadingRuns(Window win) =>
            [.. win.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("chooser-heading") && p.IsVisible)
                .Select(p => p.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray())];

        private static string[] ButtonLabels(StatePanel panel) => [.. panel.Buttons!.Items.OfType<Button>().Select(b => (string)b.Content!)];

        private static void Press(Button button) {
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        //The panel speaks only about switches that are off and hide matches, never about ones that are on.
        private static void AssertNeverBlamesAnActiveFilter(StatePanel panel) {
            string[] strings = [panel.Headline ?? "", panel.Sentence ?? "", .. ButtonLabels(panel)];
            Assert.DoesNotContain(strings, t => t is "Clear Filters" or "Reset Filters");
            Assert.All(strings, t => Assert.DoesNotMatch(@"filter is|filters are|\d", t));
        }

        [AvaloniaFact]
        public void Browser_NoResults_OffersShowAll_WhenTheFilterMenuHidesMatches_AndOnlyClearSearchWhenNothingMatches() {
            var settings = new AppSettings();
            var (win, vm) = Open(settings);
            ChooserGrid grid = win.FindControl<ChooserGrid>("Grid")!;
            int filterNotifications = 0;
            vm.PropertyChanged += (_, e) => {
                if (e.PropertyName is nameof(AddBrowserViewModel.ShowUnavailable) or nameof(AddBrowserViewModel.ShowHidden) or nameof(AddBrowserViewModel.IgnoreAssembler))
                    filterNotifications++;
            };

            vm.SearchText = "steel";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BrowserState.NoResults, vm.State);
            StatePanel panel = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal(StatePanelShape.NoResults, panel.Shape);
            Assert.Equal("No results for “steel”", panel.Headline);
            Assert.Equal("Items that match are hidden by the Filter menu.", panel.Sentence);
            Assert.Equal(["Clear Search", "Show All"], ButtonLabels(panel));
            Button showAll = panel.Buttons!.ResolveDefaultButton()!;
            Assert.Equal("Show All", showAll.Content);
            Assert.Equal(ButtonRowRole.Default, ButtonRow.GetRole(showAll));
            AssertNeverBlamesAnActiveFilter(panel);
            Assert.False(settings.ShowUnavailable || settings.ShowHidden || settings.IgnoreAssemblerStatus);
            Assert.Equal(0, filterNotifications);
            Assert.True(panel.MovesFocusOnAppear);
            Assert.Same(showAll, Focused(win));

            Press(showAll);

            Assert.True(vm.ShowUnavailable && vm.ShowHidden && vm.IgnoreAssembler);
            Assert.True(settings.ShowUnavailable && settings.ShowHidden && settings.IgnoreAssemblerStatus);
            Assert.Equal("steel", vm.SearchText);
            Assert.Equal(BrowserState.Content, vm.State);
            Assert.Contains(vm.Sections.SelectMany(s => s.Cells), c => c.Label == "Steel Chest");
            Assert.Null(grid.StateContent);

            foreach (bool on in new[] { true, false }) {
                vm.ShowUnavailable = on;
                vm.ShowHidden = on;
                vm.IgnoreAssembler = on;
                vm.SearchText = "zzzz";
                Dispatcher.UIThread.RunJobs();

                panel = Assert.IsType<StatePanel>(grid.StateContent);
                Assert.Equal("No results for “zzzz”", panel.Headline);
                Assert.Equal("Nothing in this preset matches the search.", panel.Sentence);
                Assert.Equal(["Clear Search"], ButtonLabels(panel));
                AssertNeverBlamesAnActiveFilter(panel);
                Assert.Same(panel.Buttons!.ResolveDefaultButton(), Focused(win));
                vm.SearchText = "";
            }

            var recipeFx = CatalogFixture.WithItemsAndRecipes([], ["gear"]);
            recipeFx.Assembler.Enabled = false;
            var recipeVm = new AddBrowserViewModel(new AddCatalog(recipeFx.Cache, new AppSettings()), recipeFx.Cache, new AppSettings(), (_, _) => null);
            var recipeWin = new AddBrowserWindow { DataContext = recipeVm };
            recipeWin.Show();
            recipeVm.Segment = BrowserSegment.Recipes;
            recipeVm.SearchText = "gear";
            Dispatcher.UIThread.RunJobs();
            panel = Assert.IsType<StatePanel>(recipeWin.FindControl<ChooserGrid>("Grid")!.StateContent);
            Assert.Equal("Recipes that match are hidden by the Filter menu.", panel.Sentence);
            Assert.Equal(["Clear Search", "Show All"], ButtonLabels(panel));
            AssertNeverBlamesAnActiveFilter(panel);

            //R11 lists only groups with an available entry, so a category empties through hidden entries.
            var categoryFx = CatalogFixture.WithItemsAndRecipes(["ore"], [], hidden: ["ore"]);
            var categorySettings = new AppSettings();
            var categoryVm = new AddBrowserViewModel(new AddCatalog(categoryFx.Cache, categorySettings), categoryFx.Cache, categorySettings, (_, _) => null);
            var categoryWin = new AddBrowserWindow { DataContext = categoryVm };
            categoryWin.Show();
            Dispatcher.UIThread.RunJobs();
            ChooserGrid categoryGrid = categoryWin.FindControl<ChooserGrid>("Grid")!;
            Assert.Equal(BrowserState.NoResults, categoryVm.State);
            panel = Assert.IsType<StatePanel>(categoryGrid.StateContent);
            Assert.Equal("Nothing to show in Group", panel.Headline);
            Assert.Equal("Every item in this category is hidden by the Filter menu.", panel.Sentence);
            Assert.Equal(["Show All"], ButtonLabels(panel));
            AssertNeverBlamesAnActiveFilter(panel);
            Assert.False(panel.MovesFocusOnAppear);
            Assert.NotSame(panel.Buttons!.ResolveDefaultButton(), Focused(categoryWin));

            Press(panel.Buttons!.ResolveDefaultButton()!);
            Assert.Equal(BrowserState.Content, categoryVm.State);
            Assert.Contains(categoryVm.Sections.SelectMany(s => s.Cells), c => c.Label == "ore");

            NoResults_UnderALink_KeepTheLinkCopy_AndOfferOnlyWhatReveals();
        }

        private static IInputElement? Focused(Window win) => win.FocusManager?.GetFocusedElement();

        private static (AddBrowserWindow win, AddBrowserViewModel vm, AppSettings settings) OpenLinked(CatalogFixture fx, string item, NewNodeType side, string query = "") {
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            vm.Seed(new AddRequest(new DrawingPoint(0, 0), AddScope.All, fx.LinkContextFor(item, side), query));
            Dispatcher.UIThread.RunJobs();
            return (win, vm, settings);
        }

        private static StatePanel ShownPanel(AddBrowserWindow win) => Assert.IsType<StatePanel>(win.FindControl<ChooserGrid>("Grid")!.StateContent);

        private static void AssertLinkPanelRules(StatePanel panel) {
            string[] labels = ButtonLabels(panel);
            Assert.InRange(labels.Length, 1, 2);
            Assert.DoesNotContain(labels, l => l is "Browse All…" or "Clear Filters" or "Reset Filters");
            Assert.All([panel.Headline ?? "", panel.Sentence ?? "", .. labels], t => {
                Assert.DoesNotContain("being dragged", t);
                Assert.DoesNotContain("iron plate", t);
                Assert.DoesNotMatch(@"\d", t);
            });
        }

        //The designer's link ruling: a seeded constraint keeps its own copy and offers only a button that reveals something.
        private static void NoResults_UnderALink_KeepTheLinkCopy_AndOfferOnlyWhatReveals() {
            var chain = CatalogFixture.WithProductionChain();
            ((RecipePrototype)chain.Cache.Recipes["Iron Plate"]).Available = false;
            var (win, vm, settings) = OpenLinked(chain, "Iron Plate", NewNodeType.Supplier, "Iron");
            int filterNotifications = 0;
            vm.PropertyChanged += (_, e) => {
                if (e.PropertyName is nameof(AddBrowserViewModel.ShowUnavailable) or nameof(AddBrowserViewModel.ShowHidden) or nameof(AddBrowserViewModel.IgnoreAssembler))
                    filterNotifications++;
            };
            Assert.Equal(BrowserState.NoResults, vm.State);
            StatePanel panel = ShownPanel(win);
            Assert.False(panel.MovesFocusOnAppear);
            Assert.Equal("No results for “Iron”", panel.Headline);
            Assert.Equal("Matching recipes that make Iron Plate are hidden by the Filter menu.", panel.Sentence);
            Assert.Equal(["Clear Search", "Show All"], ButtonLabels(panel));
            Assert.Equal(ButtonRowRole.Default, ButtonRow.GetRole(panel.Buttons!.ResolveDefaultButton()!));
            AssertLinkPanelRules(panel);

            vm.SearchText = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrowserState.NoResults, vm.State);
            panel = ShownPanel(win);
            Assert.Equal("Nothing to show for Iron Plate", panel.Headline);
            Assert.Equal("Every recipe that makes Iron Plate is hidden by the Filter menu.", panel.Sentence);
            Assert.Equal(["Show All"], ButtonLabels(panel));
            AssertLinkPanelRules(panel);
            Assert.False(settings.ShowUnavailable || settings.ShowHidden || settings.IgnoreAssemblerStatus);
            Assert.Equal(0, filterNotifications);

            Press(panel.Buttons!.ResolveDefaultButton()!);
            Assert.True(settings.ShowUnavailable && settings.ShowHidden && settings.IgnoreAssemblerStatus);
            Assert.NotNull(vm.ChipText);
            Assert.NotNull(vm.Link);
            Assert.Equal(BrowserState.Content, vm.State);

            (win, vm, _) = OpenLinked(chain, "Iron Plate", NewNodeType.Consumer, "Copper");
            panel = ShownPanel(win);
            Assert.Equal("Only recipes that take Iron Plate are listed for this link.", panel.Sentence);
            Assert.Equal(["Clear Search", "Remove Constraint"], ButtonLabels(panel));
            Assert.Equal("Remove Constraint", panel.Buttons!.ResolveDefaultButton()!.Content);
            AssertLinkPanelRules(panel);
            Press(panel.Buttons.ResolveDefaultButton()!);
            Assert.Null(vm.Link);
            Assert.Null(vm.ChipText);
            Assert.Equal("Search recipes…", vm.SearchPlaceholder);
            Assert.Equal("Copper", vm.SearchText);
            Assert.Equal(BrowserState.Content, vm.State);
            Assert.True(win.FindControl<SearchField>("SearchBox")!.IsKeyboardFocusWithin);

            (win, vm, _) = OpenLinked(chain, "Iron Plate", NewNodeType.Consumer, "zzzz");
            panel = ShownPanel(win);
            Assert.Equal("Nothing in this preset matches the search.", panel.Sentence);
            Assert.Equal(["Clear Search"], ButtonLabels(panel));
            AssertLinkPanelRules(panel);

            vm.Segment = BrowserSegment.Items;
            Dispatcher.UIThread.RunJobs();
            panel = ShownPanel(win);
            Assert.Equal("No results for “zzzz”", panel.Headline);
            Assert.Equal("Only Iron Plate is listed for this link.", panel.Sentence);
            Assert.Equal(["Clear Search", "Remove Constraint"], ButtonLabels(panel));
            AssertLinkPanelRules(panel);

            foreach ((NewNodeType side, string verb, string instead, string role) in new[] {
                (NewNodeType.Supplier, "make", "a source", "Source"), (NewNodeType.Consumer, "take", "an output", "Output") }) {
                var bare = CatalogFixture.WithItemsAndRecipes(["Iron Plate"], []);
                (win, vm, _) = OpenLinked(bare, "Iron Plate", side);
                Assert.Equal(BrowserState.NoResults, vm.State);
                panel = ShownPanel(win);
                Assert.Equal($"No recipes {verb} Iron Plate", panel.Headline);
                Assert.Equal($"You can add Iron Plate as {instead} instead.", panel.Sentence);
                Assert.Equal(["Remove Constraint", "Show Item"], ButtonLabels(panel));
                AssertLinkPanelRules(panel);

                Press(panel.Buttons!.ResolveDefaultButton()!);
                Assert.Equal(BrowserSegment.Items, vm.Segment);
                Assert.Equal("Iron Plate", vm.SelectedCell?.Candidate.Item?.FriendlyName);
                Assert.Equal(role, win.FindControl<PopUp>("RolePopUp")!.SelectedItem);
                ChooserGrid grid = win.FindControl<ChooserGrid>("Grid")!;
                Assert.Same(grid, Focused(win));
                Assert.Equal("Iron Plate", grid.SelectedItem?.Name);
            }
        }

        [AvaloniaFact]
        public void Browser_Loading_ShowsTheStatePanelLoading_WithTheSidebarStillLive() {
            WindowScaler.SetScale(1.0);
            var fx = CatalogFixture.WithGroups();
            var settings = new AppSettings();
            var deferred = new List<Action>();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null, defer: deferred.Add);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            ChooserGrid grid = win.FindControl<ChooserGrid>("Grid")!;

            Assert.True(vm.IsLoading);
            StatePanel panel = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal(StatePanelShape.Loading, panel.Shape);
            Assert.Equal("Loading objects", panel.Headline);
            Assert.Equal("Reading the current preset's items and recipes.", panel.Sentence);
            Assert.Null(panel.Buttons);

            List<SidebarRow> rows = [.. win.GetVisualDescendants().OfType<SidebarRow>()];
            Assert.Equal(["Logistics", "Production"], rows.Select(r => r.Label));
            RealClick.On(rows[1]);
            Assert.True(vm.Groups[1].IsSelected);

            bool broken = true;
            vm.CatalogueFault = () => {
                if (broken)
                    throw new InvalidOperationException("damaged recipe at /presets/broken.json");
            };
            foreach (Action resolve in deferred.ToList())
                resolve();
            deferred.Clear();
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsLoading);
            Assert.Equal(BrowserState.Error, vm.State);
            panel = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal(StatePanelShape.Error, panel.Shape);

            AnimationClock.Settle(300);
            Assert.Equal(1, panel.Opacity);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(panel);
            int announcements = 0;
            peer.PropertyChanged += (_, e) => {
                if (e.Property == AutomationElementIdentifiers.NameProperty)
                    announcements++;
            };
            int opacityWrites = 0;
            panel.PropertyChanged += (_, e) => {
                if (e.Property == Visual.OpacityProperty)
                    opacityWrites++;
            };

            Press(panel.Buttons!.ResolveDefaultButton()!);
            Assert.Equal(BrowserState.Loading, vm.State);
            Assert.Same(panel, grid.StateContent);
            RunDeferred(deferred);
            Assert.Equal(BrowserState.Error, vm.State);
            Assert.Same(panel, grid.StateContent);
            Assert.Equal(1, panel.Opacity);
            Assert.Equal(0, opacityWrites);
            Assert.Equal(1, announcements);

            broken = false;
            Press(panel.Buttons!.ResolveDefaultButton()!);
            Assert.Same(panel, grid.StateContent);
            AnimationClock.Settle((int)Motion.StatePanelDelay.TotalMilliseconds + 100);
            StatePanel loading = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal(StatePanelShape.Loading, loading.Shape);
            Assert.Equal(TimeSpan.Zero, loading.LoadingDelay);
            while (deferred.Count > 0) {
                Action next = deferred[0];
                deferred.RemoveAt(0);
                next();
            }
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsLoading);
            Assert.Equal(BrowserState.Content, vm.State);
            Assert.Null(grid.StateContent);
            Assert.Contains(win.GetVisualDescendants().OfType<ChooserCell>(), c => c.FullName == "Assembling Machine 1");

            Errors_ShowTheRuledPanelAroundALiveClose_AndTryAgainRecovers();
        }

        private static void RunDeferred(List<Action> deferred) {
            while (deferred.Count > 0) {
                Action next = deferred[0];
                deferred.RemoveAt(0);
                next();
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void AssertNoInternals(StatePanel panel) {
            string[] strings = [panel.Headline ?? "", panel.Sentence ?? "", .. ButtonLabels(panel)];
            Assert.All(strings, t => Assert.DoesNotMatch(@"Exception|at |/|\\", t));
        }

        //The designer's error ruling: two causes with their own copy; one undecodable icon is not an error.
        private static void Errors_ShowTheRuledPanelAroundALiveClose_AndTryAgainRecovers() {
            var (win, vm) = Open();
            ChooserGrid grid = win.FindControl<ChooserGrid>("Grid")!;
            Button add = win.FindControl<Border>("Footer")!.GetVisualDescendants().OfType<ButtonRow>().Single().ResolveDefaultButton()!;
            Button close = win.FindControl<Border>("Footer")!.GetVisualDescendants().OfType<ButtonRow>().Single().Items.OfType<Button>().Single(b => b.Content is "Close");
            Control[] dimmedByCatalogue = [win.FindControl<SearchField>("SearchBox")!, win.FindControl<PopUp>("QualityPopUp")!,
                win.FindControl<MenuButton>("FilterButton")!, win.FindControl<PopUp>("RolePopUp")!, add, win.FindControl<Border>("Sidebar")!];

            bool broken = true;
            vm.CatalogueFault = () => {
                if (broken)
                    throw new InvalidOperationException("damaged recipe at /presets/broken.json");
            };
            vm.Refresh(forceRebuild: true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BrowserState.Error, vm.State);
            StatePanel panel = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal(StatePanelShape.Error, panel.Shape);
            Assert.Equal("Couldn’t read this preset", panel.Headline);
            Assert.Equal("Some of its items or recipes are damaged, so they can’t be listed.", panel.Sentence);
            Assert.Equal(["Open Presets…", "Try Again"], ButtonLabels(panel));
            Assert.Equal(ButtonRowRole.Default, ButtonRow.GetRole(panel.Buttons!.ResolveDefaultButton()!));
            Assert.Equal("Try Again", panel.Buttons.ResolveDefaultButton()!.Content);
            Assert.False(panel.MovesFocusOnAppear);
            AssertNoInternals(panel);
            Assert.Empty(vm.Groups);
            Assert.All(dimmedByCatalogue, c => Assert.False(c.IsEnabled));
            Assert.True(close.IsEnabled);

            broken = false;
            Press(panel.Buttons.ResolveDefaultButton()!);

            Assert.Equal(BrowserState.Content, vm.State);
            Assert.Null(grid.StateContent);
            Assert.NotEmpty(vm.Groups);
            Assert.True(win.FindControl<Border>("Sidebar")!.IsEnabled);
            Assert.True(win.FindControl<SearchField>("SearchBox")!.IsEnabled);

            vm.SearchFault = _ => throw new InvalidOperationException("search index at /tmp/index");
            vm.SearchText = "iron";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BrowserState.Error, vm.State);
            panel = Assert.IsType<StatePanel>(grid.StateContent);
            Assert.Equal("Couldn’t search this preset", panel.Headline);
            Assert.Equal("Browsing by category still works; clear the search to use it.", panel.Sentence);
            Assert.Equal(["Clear Search", "Try Again"], ButtonLabels(panel));
            Assert.True(panel.MovesFocusOnAppear);
            AssertNoInternals(panel);
            Assert.True(win.FindControl<Border>("Sidebar")!.IsEnabled);
            Assert.True(win.FindControl<SearchField>("SearchBox")!.IsEnabled);
            Assert.False(add.IsEnabled);

            Press(panel.Buttons!.ResolveDefaultButton()!);
            Assert.Equal(BrowserState.Error, vm.State);
            Assert.Same(panel, grid.StateContent);

            Press(panel.Buttons.Items.OfType<Button>().Single(b => b.Content is "Clear Search"));
            Assert.Equal("", vm.SearchText);
            Assert.NotEqual(BrowserState.Error, vm.State);

            var fx = CatalogFixture.WithGroups();
            var settings = new AppSettings();
            int cellBakes = 0;
            var iconVm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, size) =>
                size == 40 && cellBakes++ == 0 ? throw new InvalidOperationException("undecodable icon") : null);
            var iconWin = new AddBrowserWindow { DataContext = iconVm };
            iconWin.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrowserState.Content, iconVm.State);
            ChooserCell[] cells = [.. iconWin.GetVisualDescendants().OfType<ChooserCell>()];
            Assert.True(cells.Length > 1);
            ChooserCell missing = Assert.Single(cells, c => c.IsIconMissing);
            Assert.Equal(iconVm.Sections.SelectMany(s => s.Cells).First().Label, missing.FullName);
            BrowserPick? placed = null;
            iconVm.PlaceRequested += (_, pick) => placed = pick;
            iconVm.SelectCell(iconVm.Sections.SelectMany(s => s.Cells).First());
            iconVm.Add();
            Assert.Equal(missing.FullName, placed?.Candidate.Name);
        }

        [AvaloniaFact]
        public void Browser_Empty_ShowsNothingToBrowseYet_WithImportAPreset() {
            var fx = CatalogFixture.WithItemsAndRecipes([], []);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            int importRequests = 0;
            vm.ImportPresetRequested += (_, _) => importRequests++;
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            StatePanel panel = Assert.IsType<StatePanel>(win.FindControl<ChooserGrid>("Grid")!.StateContent);
            Assert.Equal(StatePanelShape.Empty, panel.Shape);
            Assert.Equal("Nothing to browse yet", panel.Headline);
            Assert.Equal("This preset holds no items or recipes; import one from a Factorio install to fill it.", panel.Sentence);
            Button import = panel.Buttons!.ResolveDefaultButton()!;
            Assert.Equal("Import a Preset…", import.Content);

            import.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, importRequests);
        }

        [AvaloniaFact]
        public void Browser_RemembersItsSizeAndLastCategory_ForTheNextWindow() {
            var settings = new AppSettings();
            var (win, vm) = Open(settings);
            vm.SelectGroup(vm.Groups[1].Group);
            win.Width = 900;
            win.Height = 600;
            Dispatcher.UIThread.RunJobs();
            win.Hide();

            Assert.Equal(900, settings.BrowserWidth);
            Assert.Equal(600, settings.BrowserHeight);

            var fx = CatalogFixture.WithGroups();
            var next = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var reopened = new AddBrowserWindow { DataContext = next };
            reopened.Show();

            Assert.True(next.Groups[1].IsSelected);
            Assert.Equal(900, reopened.Width);
            Assert.Equal(600, reopened.Height);
        }

        [AvaloniaFact]
        public void Browser_Tab_MovesFromTheSidebarToTheGridThenTheFooter() {
            var (win, vm) = Open();
            Dispatcher.UIThread.RunJobs();
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First());
            Dispatcher.UIThread.RunJobs();
            ButtonRow footerButtons = win.FindControl<Border>("Footer")!.GetVisualDescendants().OfType<ButtonRow>().Single();

            SidebarRow selected = win.GetVisualDescendants().OfType<SidebarRow>().Single(r => r.IsSelected);
            Assert.Equal(2, win.GetVisualDescendants().OfType<SidebarRow>().Count());
            IInputElement[] expected = [
                win.FindControl<ChooserGrid>("Grid")!,
                win.FindControl<PopUp>("RolePopUp")!,
                footerButtons.Items.OfType<Button>().Single(b => b.Content is "Close"),
                footerButtons.Items.OfType<Button>().Single(b => b.Content is "Add"),
            ];

            selected.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            foreach (IInputElement stop in expected) {
                CanvasKeys.KeyDownOn((Control)win.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.Same(stop, win.FocusManager!.GetFocusedElement());
            }
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
