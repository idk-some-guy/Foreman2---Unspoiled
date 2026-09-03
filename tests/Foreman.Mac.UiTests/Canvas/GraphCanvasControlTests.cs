using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    public class GraphCanvasControlTests {
        private static bool ColorPresentNear(SKSurface surface, int x, int y, SKColor expected, int radius = 1) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (pixmap.GetPixelColor(x + dx, y + dy) == expected)
                        return true;
            return false;
        }

        //Regression: upstream's ArrowsOnLinks defaults false (both the fresh ProductionGraphViewer field
        //and its Settings.Designer.cs backing value) - a fresh GraphViewer, before any AppSettings ever
        //reaches it, must match that cold-start default instead of showing arrowheads nobody asked for.
        [AvaloniaFact]
        public void Construction_Defaults_ArrowsOnLinksMatchesUpstreamColdStartFalse() {
            var control = new GraphCanvasControl();

            Assert.False(control.Viewer.Context.ArrowsOnLinks);
        }

        [AvaloniaFact]
        public void Render_GridEnabled_DrawsGridLinesThroughFullTransformPipeline() {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(200, 200);
            control.Grid.ShowGrid = true;
            control.Grid.CurrentGridUnit = 20;

            using SKSurface surface = SKSurface.Create(new SKImageInfo(200, 200));
            control.Render(surface.Canvas);

            Assert.True(ColorPresentNear(surface, 120, 100, new SKColor(230, 230, 230)));
        }

        [AvaloniaFact]
        public void Render_GridDisabled_LeavesCanvasWhite() {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(200, 200);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(200, 200));
            control.Render(surface.Canvas);

            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            Assert.Equal(SKColors.White, pixmap.GetPixelColor(100, 100));
        }

        [AvaloniaFact]
        public void Viewport_TracksControlSizeChanges() {
            var control = new GraphCanvasControl();
            var window = new global::Avalonia.Controls.Window { Content = control, Width = 640, Height = 480 };
            window.Show();

            Assert.Equal(control.Bounds.Width, control.Viewport.Width);
            Assert.Equal(control.Bounds.Height, control.Viewport.Height);
        }

        //IsPassthroughBusModifierHeld tracks PlatformModifiers.Primary (docs/upstream-divergences.md, phase 8
        //Task 2) - Cmd on macOS, Ctrl on Linux via the UseIsMacOs seam.
        [AvaloniaFact]
        public void IsPassthroughBusModifierHeld_OnMacOs_TracksMetaKey() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(true);
            var control = new GraphCanvasControl();
            var window = new AvaloniaWindow { Content = control, Width = 200, Height = 200 };
            window.Show();
            control.Focus();

            window.KeyPressQwerty(PhysicalKey.MetaLeft, RawInputModifiers.Meta);
            Assert.True(control.IsPassthroughBusModifierHeld);

            window.KeyReleaseQwerty(PhysicalKey.MetaLeft, RawInputModifiers.None);
            Assert.False(control.IsPassthroughBusModifierHeld);
        }

        [AvaloniaFact]
        public void IsPassthroughBusModifierHeld_OnLinux_TracksCtrlKey() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var control = new GraphCanvasControl();
            var window = new AvaloniaWindow { Content = control, Width = 200, Height = 200 };
            window.Show();
            control.Focus();

            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
            Assert.True(control.IsPassthroughBusModifierHeld);

            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Assert.False(control.IsPassthroughBusModifierHeld);
        }

        //Rider 6 (final fix wave): every ChooserSettings access with no AnnotationSettings assigned used to
        //build a fresh AppSettings, so IRChooserPanel.PersistSettings had nowhere to write - the very next
        //chooser open discarded whatever the user just toggled (Show Hidden, Ignore Assembler, ...).
        [AvaloniaFact]
        public void ChooserSettings_NoAnnotationSettings_ReturnsSameInstanceAcrossAccesses() {
            var control = new GraphCanvasControl();
            PropertyInfo property = typeof(GraphCanvasControl).GetProperty("ChooserSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var first = (AppSettings)property.GetValue(control)!;
            var second = (AppSettings)property.GetValue(control)!;

            Assert.Same(first, second);
        }

        //Rider 7 (final fix wave): EditNode had no DCache guard, unlike every other chooser/edit-panel entry
        //point in this file - a RecipeNodeElement edit before DataCache finishes loading threw straight out
        //of EditRecipePanel's ctor instead of safely no-op'ing like its siblings.
        [AvaloniaFact]
        public void EditNode_NoDataCache_DoesNotThrowAndOpensNoPanel() {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo storeField = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)storeField.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            var item = new ItemPrototype(cache, "iron-ore", "iron-ore", subgroup, "z", false);
            store.Items[item.Name] = item;

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(400, 300);
            var window = new AvaloniaWindow { Content = control, Width = 400, Height = 300 };
            window.Show();
            //Deliberately not setting control.Viewer.Context.DCache.
            control.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(item, quality), Point.Empty);
            var viewModel = control.Viewer.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(control.Viewer.Context, viewModel);
            element.PrePaint();
            control.NodeElements.Add(element);

            control.EditNode(element);

            Assert.False(control.FloatingPanelHost.IsOpen);
        }

        //Ports EditRecipeNode's dispatch (upstream ProductionGraphViewer.cs 538-576, reference §9's
        //composition note): editing a recipe node opens two independently-floated cooperating panels -
        //EditRecipePanel anchored left of the node, a standalone RecipePanel anchored right of it - rather
        //than one merged panel with the recipe card embedded inside it.
        [AvaloniaFact]
        public void EditNode_RecipeNode_OpensPairedEditAndRecipePanels() {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo storeField = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)storeField.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var ore = new ItemPrototype(cache, "ore", "ore", subgroup, "a") { Available = true };
            var plate = new ItemPrototype(cache, "plate", "plate", subgroup, "a") { Available = true };
            store.Items[ore.Name] = ore;
            store.Items[plate.Name] = plate;
            var recipe = new RecipePrototype(cache, "smelt", "smelt", subgroup, "a") { Available = true };
            recipe.InternalOneWayAddIngredient(ore, 1);
            ore.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(plate, 1, 0);
            plate.ProductionRecipesInternal.Add(recipe);
            store.Recipes[recipe.Name] = recipe;
            var assembler = new AssemblerPrototype(cache, "assembler", "assembler", EntityType.Assembler, EnergySource.Electric) { Available = true, Enabled = true };
            recipe.AssemblersInternal.Add(assembler);
            assembler.RecipesInternal.Add(recipe);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = quality;
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 900 };
            window.Show();

            NodeId nodeId = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, quality), Point.Empty);
            Assert.True(control.Viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? nodeVm));
            var element = new RecipeNodeElement(control.Viewer.Context, (IRecipeNodeViewModel)nodeVm!);
            element.PrePaint();
            control.NodeElements.Add(element);

            control.EditNode(element);

            var editPanel = Assert.IsType<EditRecipePanel>(control.FloatingPanelHost.Content);
            Assert.IsType<RecipePanel>(control.FloatingPanelHost.CompanionContent);
            Assert.Empty(editPanel.GetVisualDescendants().OfType<RecipePanel>());
            Assert.True(control.FloatingPanelHost.Bounds.Right <= control.FloatingPanelHost.CompanionBounds.Left);
        }

        //Final-review I2: Viewer/Grid were IDisposable with no caller (dead code). This proves the control's
        //own natural teardown - detaching from the visual tree, which is what happens when its owning window
        //closes - now actually disposes the Viewer it constructed.
        [AvaloniaFact]
        public void OnDetachedFromVisualTree_DisposesOwnedViewer() {
            var control = new GraphCanvasControl();
            var window = new AvaloniaWindow { Content = control, Width = 400, Height = 300 };
            window.Show();

            Assert.False(control.Viewer.IsDisposed);

            window.Content = null;

            Assert.True(control.Viewer.IsDisposed);
        }

        //Final-review I2: the ownership trap this guards against is ImageExportWindow holding the SAME live
        //Viewer as the owning MainWindow's canvas (MainWindow.axaml.cs's `new ImageExportWindow(GraphCanvas.
        //Viewer)`) - proves that while the owning control stays attached (the only state ImageExportWindow's
        //modal ShowDialog can coexist with, since it blocks the owner's own close), the shared Viewer is
        //never disposed out from under it and both can keep painting through it.
        [AvaloniaFact]
        public void ViewerStaysUndisposed_WhileOwningCanvasRemainsAttached() {
            var control = new GraphCanvasControl();
            var window = new AvaloniaWindow { Content = control, Width = 400, Height = 300 };
            window.Show();

            using SKSurface exportSurface = SKSurface.Create(new SKImageInfo(200, 150));
            control.Viewer.Paint(exportSurface.Canvas, fullGraph: true, clearBackground: false);

            Assert.False(control.Viewer.IsDisposed);

            using SKSurface canvasSurface = SKSurface.Create(new SKImageInfo(400, 300));
            control.Render(canvasSurface.Canvas);

            Assert.False(control.Viewer.IsDisposed);
        }
    }
}
