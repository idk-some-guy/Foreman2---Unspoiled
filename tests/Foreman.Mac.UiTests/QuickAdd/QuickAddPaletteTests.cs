using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models;
using Foreman.Mac.UiTests.support;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AvaloniaVector = Avalonia.Vector;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    public sealed class QuickAddPaletteTests {
        private static (QuickAddPalette palette, QuickAddViewModel vm, AvaloniaWindow window) Open(string seed = "") {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate", "Iron Plating"], recipes: ["Iron Plate"]);
            var vm = new QuickAddViewModel(new AddCatalog(fx.Cache, new AppSettings()),
                new AddRequest(new DrawingPoint(0, 0), AddScope.All, SeedText: seed), null, _ => null);
            var palette = new QuickAddPalette { DataContext = vm };
            var window = new AvaloniaWindow { Content = palette, Width = 600, Height = 400 };
            window.Show();
            palette.FocusInitialControl();
            Dispatcher.UIThread.RunJobs();
            return (palette, vm, window);
        }

        private static PaletteRow[] Rows(Control palette) => [.. palette.GetVisualDescendants().OfType<PaletteRow>()];

        private static StatePanel? ShownState(Control palette) =>
            palette.GetVisualDescendants().OfType<StatePanel>().SingleOrDefault(s => s.IsEffectivelyVisible && TopLevel.GetTopLevel(s) is not null);

        private static Border FramePanel(GraphCanvasControl canvas) =>
            canvas.PaletteFrame.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        private static double PaletteWidth(Control scope) {
            Assert.True(scope.TryFindResource("Foreman.Size.PaletteWidth", scope.ActualThemeVariant, out object? width));
            return Assert.IsType<double>(width);
        }

        private static QuickAddPalette OpenFromLinkDrag(CanvasFixture fx, Point tab, Point drop) {
            fx.DragFrom(tab).To(drop).Release();
            fx.MoveAndClickAt(fx.Control.OpenRadial!.Ring.Bounds.Center + new AvaloniaVector(0, -64));
            Dispatcher.UIThread.RunJobs();
            return fx.Control.OpenPalette!;
        }

        private static HashSet<Color> TokenColours(ThemeVariant variant) {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) { Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml") };
            var provider = (kit.Loaded as IResourceDictionary)!.ThemeDictionaries[variant];
            IResourceDictionary dictionary = provider is ResourceInclude include ? include.Loaded : (IResourceDictionary)provider;
            HashSet<Color> colours = [];
            foreach (string key in dictionary.Keys.Cast<string>().Where(k => k.StartsWith("Foreman.Color.", StringComparison.Ordinal))) {
                Application.Current!.TryGetResource(key, variant, out object? value);
                if (value is ISolidColorBrush brush)
                    colours.Add(brush.Color);
                else if (value is Color colour)
                    colours.Add(colour);
            }
            return colours;
        }

        private static List<(Control Owner, string Property, Color Colour)> ShownBrushes(Control root) {
            List<(Control, string, Color)> brushes = [];
            void Add(Control owner, string property, IBrush? brush) {
                if (brush is ISolidColorBrush solid)
                    brushes.Add((owner, property, solid.Color));
            }
            foreach (Visual visual in root.GetVisualDescendants().Prepend(root)) {
                switch (visual) {
                    case Border border:
                        Add(border, "Background", border.Background);
                        Add(border, "BorderBrush", border.BorderBrush);
                        break;
                    case TextBlock text:
                        Add(text, "Foreground", text.Foreground);
                        break;
                    case Avalonia.Controls.Shapes.Shape shape:
                        Add(shape, "Fill", shape.Fill);
                        Add(shape, "Stroke", shape.Stroke);
                        break;
                    case Panel panel:
                        Add(panel, "Background", panel.Background);
                        break;
                }
            }
            return brushes;
        }

        [AvaloniaFact]
        public void AnAppearanceSwitchWhileOpen_RepaintsEveryBrushThePaletteShows() {
            var app = (App)Application.Current!;
            ThemeVariant? previous = app.RequestedThemeVariant;
            try {
                app.RequestedThemeVariant = ThemeVariant.Light;
                using var fx = CanvasFixture.WithVanilla();
                fx.Control.AddItemAsync(new DrawingPoint(300, 200));
                Dispatcher.UIThread.RunJobs();
                QuickAddPalette palette = fx.Control.OpenPalette!;
                palette.Search.Text = "iron";
                Dispatcher.UIThread.RunJobs();
                List<(Control Owner, string Property, Color Colour)> light = ShownBrushes(palette);
                Assert.True(light.Count >= 10, $"only {light.Count} brushes found");

                app.RequestedThemeVariant = ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();
                List<(Control Owner, string Property, Color Colour)> dark = ShownBrushes(palette);

                HashSet<Color> lightTokens = TokenColours(ThemeVariant.Light);
                HashSet<Color> darkTokens = TokenColours(ThemeVariant.Dark);
                lightTokens.ExceptWith(darkTokens);
                Assert.NotEmpty(lightTokens);
                Assert.Empty(dark.Where(b => lightTokens.Contains(b.Colour)).Select(b => $"{b.Owner.GetType().Name}.{b.Property} {b.Colour}"));
                Assert.NotEqual(light.Select(b => b.Colour), dark.Select(b => b.Colour));
                Assert.Contains(dark, b => TokenColours(ThemeVariant.Dark).Contains(b.Colour));
            } finally {
                app.RequestedThemeVariant = previous;
            }
        }

        [AvaloniaFact]
        public void TypingFiltersAndSelectsTheTopRow() {
            (QuickAddPalette palette, QuickAddViewModel vm, _) = Open();
            vm.Query = "iron pl";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Iron Plate|item", "Iron Plate|recipe", "Iron Plating|item"], vm.Rows.Select(r => $"{r.Name}|{r.KindLabel}"));
            PaletteRow[] rows = Rows(palette);
            Assert.Equal(3, rows.Length);
            Assert.True(rows[0].IsSelected);
        }

        [AvaloniaFact]
        public void TheFooter_NamesReturnAsPlaceAndOptionReturnAsPlaceAndStayOpen() {
            (QuickAddPalette palette, _, _) = Open("iron");
            PalettePanel panel = palette.GetVisualDescendants().OfType<PalettePanel>().Single();

            Assert.Equal(
                [("↵", "Return", "Place"), ("⌥↵", "Option Return", "Place, stay open"), ("⌘↵", "Command Return", "Browse…"), ("esc", "Escape", "Close")],
                panel.FooterHints.Select(h => (h.KeyText, h.KeyAccessibleName, h.Description)));
        }

        [AvaloniaFact]
        public void EnterWithNoRowsDoesNothing() {
            (QuickAddPalette palette, _, _) = Open("zzz");
            int picks = 0; palette.PickCommitted += (_, _) => picks++;
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);
            Assert.Equal(0, picks);
        }

        [AvaloniaFact]
        public void CmdEnterRequestsBrowseWithTheQuery_AndEscapeCloses() {
            (QuickAddPalette palette, _, _) = Open("iron");
            string? browse = null; bool closed = false; int picks = 0;
            palette.BrowseRequested += (_, q) => browse = q;
            palette.CloseRequested += (_, _) => closed = true;
            palette.PickCommitted += (_, _) => picks++;
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter, KeyModifiers.Meta);
            Assert.Equal("iron", browse);
            Assert.Equal(0, picks);
            CanvasKeys.KeyDownOn(palette.Search, Key.Escape);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void Palette_CapsAtSixRows_WithMoreMatchesAvailable() {
            List<string> rowNames = [.. Enumerable.Range(1, 12).Select(i => $"Row {i:00}")];
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: rowNames, recipes: []);
            var canvas = new GraphCanvasControl();
            canvas.Viewport.SetSize(900, 700);
            canvas.Viewer.Context.DCache = fx.Cache;
            var window = new AvaloniaWindow { Content = canvas, Width = 900, Height = 700 };
            window.Show();

            canvas.ShowQuickAdd(new AddRequest(new DrawingPoint(0, 0), AddScope.All));
            QuickAddPalette palette = canvas.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;
            vm.Query = "row";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12, vm.Rows.Count);
            Assert.Equal(["Row 01", "Row 02", "Row 03", "Row 04", "Row 05", "Row 06"], Rows(palette).Select(r => r.Label));
            Assert.DoesNotContain(palette.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Extent.Height > s.Viewport.Height + 0.5);
        }

        [AvaloniaFact]
        public void Palette_UnderALinkDrag_ListsCompatibleRecipesOnly_AndShowsARemovableConstraintChip() {
            using var fx = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out Point inputTab);
            IItem ironPlate = fx.Cache.Items["iron-plate"];
            AddRequest? browse = null;
            fx.Control.BrowseRequested += (_, r) => browse = r;
            QuickAddPalette palette = OpenFromLinkDrag(fx, inputTab, new Point(400, 300));
            var vm = (QuickAddViewModel)palette.DataContext!;

            Assert.NotEmpty(vm.Rows);
            Assert.All(vm.Rows, r => {
                Assert.Equal(AddKind.Recipe, r.Candidate.Kind);
                Assert.True(r.Candidate.Recipe!.ProductSet.ContainsKey(ironPlate), $"{r.Name} does not make {ironPlate.Name}");
            });
            Assert.Equal($"Search recipes that make {ironPlate.FriendlyName}", palette.Search.PlaceholderText);
            Assert.Equal(PaletteWidth(fx.Control), FramePanel(fx.Control).Bounds.Width, 0.5);

            Chip chip = Assert.Single(palette.GetVisualDescendants().OfType<Chip>());
            Assert.Equal($"Compatible with {ironPlate.FriendlyName}", chip.Label);
            AutomationPeer chipPeer = ControlAutomationPeer.CreatePeerForElement(chip);
            Assert.Equal($"Compatible with {ironPlate.FriendlyName}", chipPeer.GetName());
            AutomationPeer close = Assert.Single(chipPeer.GetChildren(), p => p.GetName() == "Remove Constraint");
            Assert.NotNull(close.GetProvider<IInvokeProvider>());

            vm.Query = "zzz";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(palette);
            StatePanel state = ShownState(palette)!;
            Assert.True(state.IsCompact);
            Assert.Equal($"Only recipes that make {ironPlate.FriendlyName} are listed while a link is being dragged.", state.Sentence);
            Button action = Assert.Single(state.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
            Assert.Equal("Browse All…", action.Content);
            Assert.Single(palette.GetVisualDescendants().OfType<Chip>(), c => c.IsEffectivelyVisible);

            RealClick.On(action);

            Assert.Equal("zzz", browse!.SeedText);
            Assert.NotNull(browse.Link);
            Assert.NotNull(vm.Link);
        }

        [AvaloniaFact]
        public void Palette_UnderAnOutputLinkDrag_WordsTheConstraintAsTake_WithTheSameChip() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplierOf("iron-plate", out Point outputTab);
            IItem ironPlate = fx.Cache.Items["iron-plate"];
            QuickAddPalette palette = OpenFromLinkDrag(fx, outputTab, new Point(400, 300));
            var vm = (QuickAddViewModel)palette.DataContext!;

            Assert.Equal($"Search recipes that take {ironPlate.FriendlyName}", palette.Search.PlaceholderText);
            Assert.Equal($"Compatible with {ironPlate.FriendlyName}", Assert.Single(palette.GetVisualDescendants().OfType<Chip>()).Label);

            vm.Query = "zzz";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Only recipes that take {ironPlate.FriendlyName} are listed while a link is being dragged.", ShownState(palette)!.Sentence);

            if (!string.Equals(ironPlate.FriendlyName, ironPlate.FriendlyName.ToLowerInvariant(), System.StringComparison.Ordinal)) {
                string lower = ironPlate.FriendlyName.ToLowerInvariant();
                Assert.DoesNotContain(lower, palette.Search.PlaceholderText!);
                Assert.DoesNotContain(lower, vm.NoResultsSentence);
            }

            double unscaled = PaletteWidth(fx.Control);
            try {
                WindowScaler.SetScale(1.5);
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(unscaled, PaletteWidth(fx.Control));
                Assert.Equal(PaletteWidth(fx.Control), FramePanel(fx.Control).Bounds.Width, 0.5);
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        [AvaloniaFact]
        public void Palette_ChipRemoved_WidensTheResultSet_AndReturnsFocusToTheField() {
            const string longName = "Advanced Oil Processing Byproduct Intermediate";
            CatalogFixture catalog = CatalogFixture.WithProductionChainAndALongNamedProduct(longName, out ItemPrototype longItem);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(catalog.Cache, new ItemQualityPair(longItem, catalog.Cache.DefaultQuality!), out Point inputTab);
            var drop = new Point(400, 300);
            QuickAddPalette palette = OpenFromLinkDrag(fx, inputTab, drop);
            var vm = (QuickAddViewModel)palette.DataContext!;
            vm.Query = "iron";
            Dispatcher.UIThread.RunJobs();
            int constrained = vm.Rows.Count;
            AnimationClock.UntilReflowsSettled(palette);

            Chip chip = Assert.Single(palette.GetVisualDescendants().OfType<Chip>());
            Assert.Equal($"Compatible with {longName}", chip.Label);
            Border close = chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_CloseBox");
            Border panel = FramePanel(fx.Control);
            Rect closeInPanel = new(close.TranslatePoint(new Point(0, 0), panel)!.Value, close.Bounds.Size);
            Assert.True(new Rect(panel.Bounds.Size).Contains(closeInPanel), $"✕ at {closeInPanel}, panel {panel.Bounds.Size}");
            RealClick.On(close);

            Assert.Null(vm.Link);
            Assert.True(vm.Rows.Count > constrained, $"{vm.Rows.Count} rows after removing the constraint, {constrained} before");
            Assert.Contains(vm.Rows, r => r.Candidate.Kind == AddKind.Item);
            Assert.DoesNotContain(palette.GetVisualDescendants().OfType<Chip>(), c => TopLevel.GetTopLevel(c) is not null);
            Assert.Equal("Search…", palette.Search.PlaceholderText);
            Assert.Same(palette.Search, fx.Window.FocusManager?.GetFocusedElement());

            int linksBefore = fx.Viewer.Graph.NodeLinks.Count();
            var before = fx.Viewer.NodeElements.ToList();
            vm.Query = "copper cable";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            Assert.Equal(linksBefore, fx.Viewer.Graph.NodeLinks.Count());
            Assert.Equal(fx.Control.Viewport.ScreenToGraph(drop), created.ViewModel.Location);
        }

        [AvaloniaFact]
        public void Palette_NoResults_ShowsTheStatePanelNoResultsShape_WithItsOneAction() {
            using var fx = CanvasFixture.WithVanilla();
            AddRequest? browse = null;
            fx.Control.BrowseRequested += (_, r) => browse = r;
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            QuickAddPalette palette = fx.Control.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;

            StatePanel firstUse = ShownState(palette)!;
            Assert.Equal(StatePanelShape.Empty, firstUse.Shape);
            Assert.Equal("Search to add a node", firstUse.Headline);
            Assert.Equal("Type a few letters of a recipe or an item, then press Return to place it.", firstUse.Sentence);
            Assert.Equal("Browse All…", firstUse.Buttons!.ResolveDefaultButton()!.Content);

            vm.Query = "beaconium";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(palette);

            StatePanel state = ShownState(palette)!;
            Assert.Equal(StatePanelShape.NoResults, state.Shape);
            Assert.True(state.IsCompact);
            Assert.Equal("No matches for “beaconium”", state.Headline);
            Assert.Equal("Nothing in this preset matches the search.", state.Sentence);
            Button action = Assert.Single(state.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
            Assert.Equal("Browse All…", action.Content);

            RealClick.On(action);

            Assert.Equal("beaconium", browse!.SeedText);
            Assert.Null(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void Palette_OptionReturn_PlacesTheNode_KeepsTheQuery_AndFlashesTheNodeOnce() {
            using var fx = CanvasFixture.WithVanilla();
            TokenBridge.Refresh();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            QuickAddPalette palette = fx.Control.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;
            vm.Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            var before = fx.Viewer.NodeElements.ToList();

            CanvasKeys.KeyDownOn(palette.Search, Key.Enter, KeyModifiers.Alt);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            Assert.Equal("iron plate", vm.Query);
            Assert.Same(palette, fx.Control.OpenPalette);
            Assert.Same(created, fx.Control.PlacementFlashNode);
            Assert.True(created.IsPlacementFlashing);

            RingGeometry ring = created.PlacementFlashRing();
            Assert.Equal(ring, created.LaneARing());
            Assert.Equal(2, ring.StrokeWidth);
            Assert.Equal(13, ring.Radius);
            Assert.Null(ring.Dash);
            Assert.Equal(created.Location.X - (created.Width / 2f) - 3, ring.Rect.Left, 0.5);
            Assert.Equal(created.Location.Y - (created.Height / 2f) - 3, ring.Rect.Top, 0.5);
            Assert.Equal(created.Width + 6, ring.Rect.Width, 0.5);
            Assert.Equal(created.Height + 6, ring.Rect.Height, 0.5);
            Color accent = ThemeResources.ResolveColor("Foreman.Color.Accent", Colors.Transparent);
            Assert.Equal(new SKColor(accent.R, accent.G, accent.B, accent.A), ring.Color);

            AnimationClock.Settle(700);

            Assert.Null(fx.Control.PlacementFlashNode);
            Assert.False(created.IsPlacementFlashing);
            Assert.Null(created.LaneARing());
        }

        [AvaloniaFact]
        public void Palette_OpenedFromTheToolbar_SitsUnderTheToolbarItem() {
            using ShellFixture shell = ShellFixture.Open();
            MainWindow window = shell.Window;
            Control item = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "AddItemButton" && c.IsEffectivelyVisible);

            window.Commands.AddItemCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            PopoverFrame frame = window.GraphCanvas.PaletteFrame;
            Assert.True(frame.IsOpen);
            Point itemBottomLeft = item.TranslatePoint(new Point(0, item.Bounds.Height), window.GraphCanvas)!.Value;
            Assert.Equal(itemBottomLeft.X, Avalonia.Controls.Canvas.GetLeft(frame), 0.5);
            Assert.Equal(0, Avalonia.Controls.Canvas.GetTop(frame), 0.5);
        }
    }
}
