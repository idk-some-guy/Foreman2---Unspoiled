using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
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

        //Gate fix 8, item 1: the browser's search box lives in its own window, never a descendant of
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

        //Gate fix 7, item 6: an unresolved Factorio locale placeholder becomes a readable phrase; a normal
        //FriendlyName (distinct from the raw internal Name) is left alone.
        [Fact]
        public void HumanizeStripsAnUnresolvedLocalePlaceholder_AndLeavesARealFriendlyNameAlone() {
            Assert.Equal("Extraction items", AddBrowserViewModel.Humanize("§§SG:EXTRACTION_ITEMS", "extraction-items"));
            Assert.Equal("Storage", AddBrowserViewModel.Humanize("Storage", "storage"));
        }

        //Gate fix 11: the placeholder's own kind marker ("SG:", "G:", any case) sits right after "§§" and
        //must go too, not just the "§§" itself - a colon elsewhere in a real name is left alone.
        [Fact]
        public void HumanizeStripsTheKindMarkerAfterTheLocalePlaceholder_CaseInsensitively() {
            Assert.Equal("Extraction items", AddBrowserViewModel.Humanize("§§sg:extraction_items", "extraction-items"));
            Assert.Equal("Extra group", AddBrowserViewModel.Humanize("§§g:extra_group", "extra-group"));
            Assert.Equal("Mod: Things", AddBrowserViewModel.Humanize("Mod: Things", "mod-things"));
        }

        //Gate fix 10: a localised FriendlyName can carry embedded newlines - Humanize collapses any
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

            Assert.Contains("EXTRACTION ITEMS", vm.Sections.Select(s => s.Heading));
        }

        //Gate fix 8, item 3: a group whose FriendlyName is Factorio's own unresolved locale key surfaces in
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
            Assert.Equal(new Thickness(9, 5), searchField.Padding);
        }

        //Gate fix 7, item 1: the chip must be a small pill vertically centred beside the search field, not
        //a block spanning the toolbar row height; the search field's own text must sit centred in its well.
        [AvaloniaFact]
        public void MockFidelity_ChipIsASmallCenteredPill_AndSearchTextIsVerticallyCentered() {
            var fx = CatalogFixture.WithItemsAndRecipes(["Iron Plate"], []);
            AddLinkContext link = fx.LinkContextFor("Iron Plate", NewNodeType.Consumer);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            vm.Seed(new AddRequest(new DrawingPoint(0, 0), AddScope.All, link));
            Dispatcher.UIThread.RunJobs();

            var chip = win.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("foreman-filter-chip"));
            Assert.True(chip.Bounds.Height <= 24, $"chip height {chip.Bounds.Height} exceeds the pill height");
            Assert.Equal(new CornerRadius(100), chip.CornerRadius);

            var searchField = win.FindControl<TextBox>("SearchBox")!;
            double chipCenterY = Avalonia.VisualExtensions.TranslatePoint(chip, new Point(0, chip.Bounds.Height / 2), win)!.Value.Y;
            double searchCenterY = Avalonia.VisualExtensions.TranslatePoint(searchField, new Point(0, searchField.Bounds.Height / 2), win)!.Value.Y;
            Assert.Equal(searchCenterY, chipCenterY, 1);

            var textPresenter = searchField.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
            double wellCenterY = Avalonia.VisualExtensions.TranslatePoint(searchField, new Point(0, searchField.Bounds.Height / 2), win)!.Value.Y;
            double presenterCenterY = Avalonia.VisualExtensions.TranslatePoint(textPresenter, new Point(0, textPresenter.Bounds.Height / 2), win)!.Value.Y;
            Assert.Equal(wellCenterY, presenterCenterY, 1);
        }

        //Gate fix 8, item 3: the mock's .filter-chip text carries font-weight 500 (Medium), not the
        //default Normal weight left over from gate fix 7's own pill-shape pass.
        [AvaloniaFact]
        public void ChipText_IsMediumWeight() {
            var fx = CatalogFixture.WithItemsAndRecipes(["Iron Plate"], []);
            AddLinkContext link = fx.LinkContextFor("Iron Plate", NewNodeType.Consumer);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            vm.Seed(new AddRequest(new DrawingPoint(0, 0), AddScope.All, link));
            Dispatcher.UIThread.RunJobs();

            var chipText = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("foreman-filter-chip-text"));
            Assert.Equal(FontWeight.Medium, chipText.FontWeight);
        }

        //Gate fix 1: past the cap the label ellipsizes and carries a tooltip.
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
            Assert.Equal(longName, ToolTip.GetTip(label));
        }

        //Round 1: a name landing strictly between the floor and the cap must still fit uncrimped.
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
            Assert.Null(ToolTip.GetTip(label));

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
            Assert.Equal(longName, ToolTip.GetTip(label));

            vm.ShowUnavailable = true;
            Dispatcher.UIThread.RunJobs();

            label = win.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is BrowserGroup g && g.Name == longName);
            Assert.Equal(longName, ToolTip.GetTip(label));
        }

        //Gate fix 2: a fixed WrapPanel ItemHeight keeps a two-line label from drifting the next row.
        [AvaloniaFact]
        public void GridCellsShareAUniformHeight_RegardlessOfOneOrTwoLineLabels() {
            string[] items = ["A", "B", "C", "D", "E", "F",
                "A label long enough to wrap across two full lines", "G", "H",
                "Another long label that also wraps across two lines"];
            var fx = CatalogFixture.WithItemsAndRecipes(items, []);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var cellButtons = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            Assert.Equal(items.Length, cellButtons.Count);

            var heights = cellButtons.Select(b => Math.Round(b.Bounds.Height, 1)).Distinct().ToList();
            Assert.Single(heights);

            //A WrapPanel cell's own Bounds are always (0,0); window-relative position shows real rows.
            var positions = cellButtons.Select(b => Avalonia.VisualExtensions.TranslatePoint(b, new Point(0, 0), win)!.Value).ToList();
            var rowYs = positions.Select(p => Math.Round(p.Y, 1)).Distinct().OrderBy(y => y).ToList();
            Assert.True(rowYs.Count >= 2, $"expected at least 2 rows, got Y values: {string.Join(", ", rowYs)}");

            double secondRowY = rowYs[1];
            var secondRowCells = positions.Where(p => Math.Round(p.Y, 1) == secondRowY).ToList();
            Assert.NotEmpty(secondRowCells);
            Assert.All(secondRowCells, p => Assert.Equal(secondRowY, Math.Round(p.Y, 1)));
        }

        //Gate fix 7, item 4: every cell is exactly the fixed pitch wide regardless of its label's length, so
        //columns line up across rows instead of drifting with the text.
        [AvaloniaFact]
        public void GridCellsShareAUniformWidth_ColumnsAlignAcrossThreeRows() {
            string[] items = [.. Enumerable.Range(1, 24).Select(i => i % 5 == 0 ? $"A very long label number {i:00} that wraps" : $"I{i:00}")];
            var fx = CatalogFixture.WithItemsAndRecipes(items, []);
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var cellButtons = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            Assert.Equal(items.Length, cellButtons.Count);
            Assert.All(cellButtons, b => Assert.Equal(88, b.Bounds.Width, 0.5));

            var positions = cellButtons.Select(b => Avalonia.VisualExtensions.TranslatePoint(b, new Point(0, 0), win)!.Value).ToList();
            var rowYs = positions.Select(p => Math.Round(p.Y, 1)).Distinct().OrderBy(y => y).ToList();
            Assert.True(rowYs.Count >= 3, $"expected at least 3 rows, got Y values: {string.Join(", ", rowYs)}");

            var columnXsByRow = rowYs.Take(3)
                .Select(y => positions.Where(p => Math.Round(p.Y, 1) == y).Select(p => Math.Round(p.X, 1)).OrderBy(x => x).ToList())
                .ToList();
            List<double> firstRowColumns = columnXsByRow[0];
            foreach (List<double> rowColumns in columnXsByRow.Skip(1))
                for (int col = 0; col < Math.Min(firstRowColumns.Count, rowColumns.Count); col++)
                    Assert.Equal(firstRowColumns[col], rowColumns[col], 0.5);
        }

        //Gate fix 7, item 5: the real Space Age preset's own group names (measured against the widest
        //realised row, not a hand-tuned chrome constant) must fit inside the sidebar with no right-edge clip.
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

            //Gate fix 9: bounds-fitting alone doesn't catch this - a trimmed label still stays inside the
            //sidebar via ellipsis. "Intermediate products" must actually fit, not just get clipped to fit.
            TextBlock intermediateProducts = labels.First(l => ((BrowserGroup)l.DataContext!).Name == "Intermediate products");
            Assert.Null(ToolTip.GetTip(intermediateProducts));
        }

        //Gate fix 9: the widest bundled group name must render untrimmed at every UI scale.
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

                    Assert.Null(ToolTip.GetTip(label));

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

        //Gate fix 10: the Foreman-internal "extra" group's FriendlyName used to join three category
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
            var headings = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("foreman-browser-subhead")).ToList();
            Assert.NotEmpty(groupLabels);

            foreach (TextBlock label in groupLabels.Concat(headings)) {
                string text = label.Text ?? "";
                Assert.False(text.Contains('\n') || text.Contains('\r'), $"'{text}' contains a raw newline");
            }

            var groupButtons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-group")).ToList();
            Button extraGroupButton = groupButtons.Single(b => ((BrowserGroup)b.DataContext!).Group.Name == "§§g:extra_group");
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

        //Gate fix 7, item 3: a link-drag's own "Add..." (radial Add.../the palette's Cmd+Enter after it)
        //always seeds a Link, so it must open on Recipes - a scope-less/link-less open (the toolbar,
        //background menu, double-click) still defaults to Items.
        [AvaloniaFact]
        public void LinkDragAddOpensOnRecipes_ScopeAllWithoutALinkStillOpensOnItems() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.All, link));
            Assert.Equal(BrowserSegment.Recipes, shell.BrowserVm.Segment);

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
