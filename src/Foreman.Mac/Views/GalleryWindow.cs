using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Views {
    //Dev-only visual check window for every canvas floating panel, launched via `--gallery` (see
    //App.BootGalleryAsync/Program.Main). Loads the current preset through the same path the real app uses
    //(ShellBootstrapper.LoadPresetAsync), then hosts each panel live against a small scratch graph instead of
    //a static render - a hidden GraphCanvasControl isn't needed since EditFlowPanel/EditRecipePanel only ever
    //touch GraphViewer/Session, not the canvas itself, so a bare Viewport+GridManager+GraphViewer backs them.
    public sealed class GalleryWindow : Window {
        private const double TileColumnWidth = 1520;

        public GalleryWindow(DataCache cache, AppSettings settings) {
            Title = "Foreman Gallery";
            Width = 1600;
            Height = 1000;
            //Matches GraphViewer.Paint's canvas.Clear(SKColors.White): every floating panel here is a Border
            //that already paints its own Brushes.Black chrome (EditFlowPanel/EditRecipePanel/IRChooserPanel),
            //so the backdrop has to be the graph canvas's real white, not another black, or that chrome
            //disappears into the window instead of standing out the way it does over the actual canvas.
            Background = Brushes.White;

            IQuality quality = cache.DefaultQuality ?? cache.AvailableQualities.First();
            var viewer = new GraphViewer(new Viewport(1200, 900), new GridManager()) { Graph = { DefaultAssemblerQuality = quality } };
            viewer.Context.DCache = cache;

            RecipeShowcase? showcase = FindShowcaseRecipe(cache);
            ItemQualityPair keyItem = FindKeyItem(cache, showcase, quality);

            var tiles = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            tiles.Children.Add(Tile("ItemChooserPanel", BuildItemChooserPanel(cache, settings)));
            tiles.Children.Add(Tile("RecipeChooserPanel", BuildRecipeChooserPanel(cache, settings, keyItem)));
            tiles.Children.Add(Tile("EditFlowPanel", BuildEditFlowPanel(viewer, keyItem)));
            tiles.Children.Add(Tile("EditRecipePanel", BuildEditRecipePanel(viewer, cache, quality, showcase)));
            tiles.Children.Add(Tile("QualityPicker", ChromeWrap(BuildQualityPicker(cache))));

            Content = new ScrollViewer {
                Content = tiles,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        }

        private static StackPanel Tile(string caption, Control content) {
            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = caption, Foreground = Brushes.Black, FontWeight = FontWeight.Bold });
            stack.Children.Add(content);
            return stack;
        }

        //QualityPicker never floats on its own in the real app - it's always embedded inside a panel that
        //already paints Border.Background = Brushes.Black on itself (EditFlowPanel/EditRecipePanel/
        //IRChooserPanel). Reusing that same brush here shows it the way it actually renders once hosted.
        private static Border ChromeWrap(Control content) => new() { Background = Brushes.Black, Padding = new Thickness(8), Child = content };

        private static ItemChooserPanel BuildItemChooserPanel(DataCache cache, AppSettings settings) {
            var panel = new ItemChooserPanel(cache, settings);
            panel.Initialize();
            return panel;
        }

        private static RecipeChooserPanel BuildRecipeChooserPanel(DataCache cache, AppSettings settings, ItemQualityPair keyItem) {
            var panel = new RecipeChooserPanel(cache, settings, keyItem, new FRange(0, 0, true), NewNodeType.Disconnected);
            panel.Initialize();
            return panel;
        }

        private static EditFlowPanel BuildEditFlowPanel(GraphViewer viewer, ItemQualityPair keyItem) {
            NodeId nodeId = viewer.Session.Editor.CreatePassthroughNode(keyItem, new DrawingPoint(0, 0));
            if (!viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? node) || node is null)
                throw new InvalidOperationException("Gallery: failed to create the example passthrough node.");
            return new EditFlowPanel(node, viewer);
        }

        //Shows the paired composition the real app actually floats (GraphCanvasControl.EditNode, reference
        //§9): EditRecipePanel next to its own standalone RecipePanel, not the recipe card embedded inside
        //the edit panel (review finding B1).
        private static StackPanel BuildEditRecipePanel(GraphViewer viewer, DataCache cache, IQuality quality, RecipeShowcase? showcase) {
            IRecipe recipe = showcase?.Recipe ?? cache.Recipes.Values.First();
            NodeId nodeId = viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, quality), new DrawingPoint(400, 0));
            if (!viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? node) || node is not IRecipeNodeViewModel recipeNode)
                throw new InvalidOperationException("Gallery: failed to create the example recipe node.");

            if (showcase is RecipeShowcase demo && viewer.Session.Editor.RequestNodeController(nodeId) is RecipeNodeController controller) {
                controller.SetAssembler(new AssemblerQualityPair(demo.Assembler, quality));
                foreach (IModule module in demo.Modules)
                    controller.AddAssemblerModule(new ModuleQualityPair(module, quality));
                if (demo.Beacon is IBeacon beacon) {
                    controller.SetBeacon(new BeaconQualityPair(beacon, quality));
                    controller.SetBeaconCount(2);
                    controller.SetBeaconsPerAssembler(1);
                }
                if (demo.Fuel is IItem fuel)
                    controller.SetFuel(fuel);
                viewer.Graph.UpdateNodeValues();
            }

            var editPanel = new EditRecipePanel(recipeNode, viewer);
            var recipePanel = new RecipePanel([recipe], viewer.Context.AbbreviateSciPacks);
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { editPanel, recipePanel } };
        }

        private static QualityPicker BuildQualityPicker(DataCache cache) {
            var picker = new QualityPicker();
            picker.SetQualities(cache.AvailableQualities.Where(q => q.Enabled));
            return picker;
        }

        //The richest assembler this cache's data can actually back: prefers one with modules and a beacon
        //over one that merely has fuel, since no vanilla entity carries both at once (a burner assembler
        //never has module slots) - fuel only wins a tie-break when nothing offers modules or a beacon at all.
        private readonly record struct RecipeShowcase(IRecipe Recipe, IAssembler Assembler, IReadOnlyList<IModule> Modules, IBeacon? Beacon, IItem? Fuel);

        private static RecipeShowcase? FindShowcaseRecipe(DataCache cache) {
            RecipeShowcase? best = null;
            int bestScore = -1;

            foreach (IRecipe recipe in cache.Recipes.Values.Where(r => r.Available && r.Enabled)) {
                foreach (IAssembler assembler in recipe.Assemblers.Where(a => a.Enabled && a.Available)) {
                    List<IModule> modules = assembler.AllowModules && assembler.ModuleSlots > 0
                        ? [.. recipe.AssemblerModules.Intersect(assembler.Modules).Where(m => m.Enabled && m.Available).Take(2)]
                        : [];
                    IBeacon? beacon = assembler.AllowBeacons
                        ? cache.Beacons.Values.FirstOrDefault(b => b.Enabled && b.Available && recipe.BeaconModules.Any(m => b.Modules.Contains(m)))
                        : null;
                    IItem? fuel = assembler.IsBurner ? assembler.Fuels.FirstOrDefault(f => f.Available) : null;

                    int score = (modules.Count > 0 ? 2 : 0) + (beacon is not null ? 2 : 0) + (fuel is not null ? 1 : 0);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = new RecipeShowcase(recipe, assembler, modules, beacon, fuel);
                }
            }
            return best;
        }

        private static ItemQualityPair FindKeyItem(DataCache cache, RecipeShowcase? showcase, IQuality quality) {
            IItem item = FirstOrNull(showcase?.Recipe.IngredientList)
                ?? FirstOrNull(showcase?.Recipe.ProductList)
                ?? cache.AvailableItems.First();
            return new ItemQualityPair(item, quality);
        }

        private static IItem? FirstOrNull(IReadOnlyList<IItem>? items) => items is { Count: > 0 } ? items[0] : null;
    }
}
