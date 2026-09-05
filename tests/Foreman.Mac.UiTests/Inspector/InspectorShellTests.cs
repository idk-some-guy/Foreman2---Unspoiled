using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    public class InspectorShellTests : IDisposable {
        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public async Task SelectingOneNode_ShowsSidebarAt300WithIdentity() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            Assert.False(window.Inspector.IsVisible);

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsVisible);
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            Assert.Equal(recipe.BaseRecipe.FriendlyName, window.Inspector.FindControl<TextBlock>("IdentityName")!.Text);
        }

        //Task 9 (contract §03, Jozef's live-gate ruling): the sidebar overlays the canvas instead of
        //pushing it - showing or hiding it must never resize the canvas underneath.
        [AvaloniaFact]
        public async Task SelectingAndClearingSelection_CanvasWidthNeverChanges() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            double fullWidth = window.GraphCanvas.Bounds.Width;

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsVisible);
            Assert.Equal(fullWidth, window.GraphCanvas.Bounds.Width, 1);

            window.GraphCanvas.Viewer.SetSelection([]);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.Inspector.IsVisible);
            Assert.Equal(fullWidth, window.GraphCanvas.Bounds.Width, 1);
        }

        //The sidebar sits in the same cell as the canvas, right-aligned above it - its right edge lands
        //exactly at the canvas's own right edge (both fill the same full-width row).
        [AvaloniaFact]
        public async Task SelectingOneNode_SidebarRightEdgeMatchesCanvasRightEdge() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(300, window.Inspector.Bounds.Width, 1);
            Assert.Equal(window.GraphCanvas.Bounds.Right, window.Inspector.Bounds.Right, 1);
        }

        [AvaloniaFact]
        public async Task Pinned_EmptySelection_ShowsNoSelectionText() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.ViewModel.Inspector.IsPinned = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsVisible);
            Assert.Equal("No selection", window.Inspector.FindControl<TextBlock>("EmptyState")!.Text);
        }

        [AvaloniaFact]
        public async Task SelectedRecipeNode_SubtitleShowsAssemblerAndQuality() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            string expected = $"{recipe.SelectedAssembler.Assembler.FriendlyName} · {recipe.SelectedAssembler.Quality.FriendlyName}";
            Assert.Equal(expected, window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
        }

        //Task 7: the paired floating recipe editor and its standalone recipe card are retired - a real click
        //on a recipe node now only selects it, and the inspector's own Recipe segment takes over.
        [AvaloniaFact]
        public async Task ClickingRecipeNode_SelectsAndShowsRecipeSegment_NoFloatingPanel() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var node = window.GraphCanvas.Viewer.NodeElements.First();
            node.RequestStateUpdate();
            node.PrePaint();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(node.Location);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(window.GraphCanvas, canvasPoint, window) ?? canvasPoint;

            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
            Assert.True(window.Inspector.IsVisible);
            Assert.IsType<RecipeSegmentView>(window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content);
        }

        //Clicking straight from one node to another used to flash the sidebar shut and back open for a frame.
        [AvaloniaFact]
        public async Task SwitchingSelectionBetweenNodes_InspectorStaysVisible_NoFalseTransition() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            DataCache cache = window.DataCache!;
            var secondPair = new RecipeQualityPair(cache.Recipes["copper-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(secondPair, new System.Drawing.Point(400, 0));
            Dispatcher.UIThread.RunJobs();

            List<BaseNodeElement> elements = [.. window.GraphCanvas.Viewer.NodeElements];
            foreach (BaseNodeElement element in elements) {
                element.RequestStateUpdate();
                element.PrePaint();
            }
            Dispatcher.UIThread.RunJobs();

            Avalonia.Point ScreenPointOf(BaseNodeElement element) {
                Avalonia.Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(element.Location);
                return Avalonia.VisualExtensions.TranslatePoint(window.GraphCanvas, canvasPoint, window) ?? canvasPoint;
            }

            var visibilityTransitions = new List<bool>();
            window.Inspector.PropertyChanged += (_, e) => {
                if (e.Property == Visual.IsVisibleProperty)
                    visibilityTransitions.Add((bool)e.NewValue!);
            };

            Avalonia.Point screenA = ScreenPointOf(elements[0]);
            window.MouseDown(screenA, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenA, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            object? segmentAfterFirst = window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content;

            Avalonia.Point screenB = ScreenPointOf(elements[1]);
            window.MouseDown(screenB, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenB, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsVisible);
            Assert.Same(segmentAfterFirst, window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content);
            Assert.Single(visibilityTransitions);
            Assert.True(visibilityTransitions[0]);
        }

        //Gate fix 3: MouseDown's own SetSelection and MouseUp's EditNode->SetSelection both fired for a
        //plain click, doubling every Refresh (segment rebind + tint recompute) behind it.
        [AvaloniaFact]
        public async Task ClickingBetweenNodes_RaisesSelectionChangedExactlyOncePerClick() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            DataCache cache = window.DataCache!;
            var secondPair = new RecipeQualityPair(cache.Recipes["copper-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(secondPair, new System.Drawing.Point(400, 0));
            Dispatcher.UIThread.RunJobs();

            List<BaseNodeElement> elements = [.. window.GraphCanvas.Viewer.NodeElements];
            foreach (BaseNodeElement element in elements) {
                element.RequestStateUpdate();
                element.PrePaint();
            }
            Dispatcher.UIThread.RunJobs();

            Avalonia.Point ScreenPointOf(BaseNodeElement element) {
                Avalonia.Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(element.Location);
                return Avalonia.VisualExtensions.TranslatePoint(window.GraphCanvas, canvasPoint, window) ?? canvasPoint;
            }

            int raiseCount = 0;
            window.GraphCanvas.Viewer.SelectionChanged += (_, _) => raiseCount++;

            Avalonia.Point screenA = ScreenPointOf(elements[0]);
            window.MouseDown(screenA, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenA, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raiseCount);

            Avalonia.Point screenB = ScreenPointOf(elements[1]);
            window.MouseDown(screenB, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenB, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, raiseCount);
        }

        //Fix round: picking the already-selected assembler/quality cell used to dirty the document even
        //though nothing changed - RecipeNodeController.SetAssembler always ends with Mutated().
        [AvaloniaFact]
        public async Task PickAssembler_AlreadySelected_RaisesNoGraphMutated_AndStaysClean() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            InspectorTestSupport.Select(window, recipe);
            var recipeSegmentView = (RecipeSegmentView)window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content!;
            window.ViewModel.IsDirty = false; //selecting the node above didn't mutate the graph, but reset to isolate the pick

            PickGridItem selectedItem = recipeSegmentView.ViewModel!.AssemblerOptions.Single(i => Equals(i.Key, recipe.SelectedAssembler.Assembler));
            recipeSegmentView.ViewModel!.PickAssembler(selectedItem);

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task PickQuality_AlreadySelected_RaisesNoGraphMutated_AndStaysClean() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            InspectorTestSupport.Select(window, recipe);
            var recipeSegmentView = (RecipeSegmentView)window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content!;
            window.ViewModel.IsDirty = false; //selecting the node above didn't mutate the graph, but reset to isolate the pick

            PickGridItem selectedItem = recipeSegmentView.ViewModel!.QualityOptions.Single(i => Equals(i.Key, recipe.SelectedAssembler.Quality));
            recipeSegmentView.ViewModel!.PickQuality(selectedItem);

            Assert.False(window.ViewModel.IsDirty);
        }

        //The identity row's own quality mini-select: a pick writes straight through to the node's current
        //assembler (RecipeSegmentViewModel.PickQuality), so the subtitle and the mini-select's own label
        //both update immediately.
        [AvaloniaFact]
        public void QualityMiniSelect_ListsQualitiesAndPickQuality_WritesThroughAndUpdatesSubtitleAndLabel() {
            DataCache cache = new(filterRecipes: true);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            var legendary = new QualityPrototype(cache, "legendary", "Legendary", "b") { Level = 1 };
            FieldInfo storeField = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)storeField.GetValue(cache)!;
            store.Qualities[normal.Name] = normal;
            store.Qualities[legendary.Name] = legendary;
            store.DefaultQuality = normal;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var ore = new ItemPrototype(cache, "ore", "ore", subgroup, "a") { Available = true };
            store.Items[ore.Name] = ore;
            var plate = new ItemPrototype(cache, "plate", "plate", subgroup, "a") { Available = true };
            store.Items[plate.Name] = plate;
            var recipe = new RecipePrototype(cache, "smelt", "smelt", subgroup, "a") { Available = true, Time = 1 };
            recipe.InternalOneWayAddIngredient(ore, 1);
            ore.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(plate, 1, 0);
            plate.ProductionRecipesInternal.Add(recipe);
            store.Recipes[recipe.Name] = recipe;

            var electric = new AssemblerPrototype(cache, "electric-assembler", "Electric Furnace", EntityType.Assembler, EnergySource.Electric) { Available = true, Enabled = true };
            recipe.AssemblersInternal.Add(electric);
            electric.RecipesInternal.Add(recipe);

            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = normal;
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();

            var node = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(node.Id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            controller.SetAssembler(new AssemblerQualityPair(electric, normal));
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            InspectorTestSupport.Select(window, node);

            Button qualityMiniSelect = window.Inspector.FindControl<Button>("QualityMiniSelect")!;
            Assert.True(qualityMiniSelect.IsVisible);
            Assert.Equal(normal.FriendlyName, window.Inspector.FindControl<TextBlock>("QualityMiniSelectLabel")!.Text);
            PickGrid qualityGrid = window.Inspector.FindControl<PickGrid>("QualityGrid")!;
            Assert.Equal(2, qualityGrid.Items.Count);

            qualityMiniSelect.Flyout!.ShowAt(qualityMiniSelect);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Button legendaryCell = qualityGrid.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals((b.DataContext as PickGridItem)?.Key, legendary));
            Point center = new(legendaryCell.Bounds.Width / 2, legendaryCell.Bounds.Height / 2);
            Point screen = legendaryCell.TranslatePoint(center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(legendary, node.SelectedAssembler.Quality);
            Assert.Equal($"{electric.FriendlyName} · {legendary.FriendlyName}", window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
            Assert.Equal(legendary.FriendlyName, window.Inspector.FindControl<TextBlock>("QualityMiniSelectLabel")!.Text);
        }

        [AvaloniaFact]
        public async Task SelectedSupplierNode_ShowsItemNameAndSupplierSubtitle() {
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(cache.Items["iron-plate"].FriendlyName, window.Inspector.FindControl<TextBlock>("IdentityName")!.Text);
            Assert.Equal("Supplier node", window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
        }

        [AvaloniaFact]
        public void InspectorTokens_ResolveInBothThemes() {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark }) {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (string key in new[] {
                    "Foreman.Size.InspectorWidth", "Foreman.Color.InspectorBackground",
                    "Foreman.Type.InspectorTitle.FontSize", "Foreman.Type.InspectorTitle.FontWeight",
                    "Foreman.Type.InspectorSubtitle.FontSize",
                    "Foreman.Color.SidebarBackground", "Foreman.Color.WellBackground",
                    "Foreman.Type.SmallBody.FontSize",
                    "Foreman.Shadow.Sidebar", "Foreman.Shadow.PickSelectedRing",
                })
                    Assert.True(Application.Current.TryGetResource(key, variant, out _), key);
            }
        }

        [AvaloniaFact]
        public async Task Scale1_5_InspectorTransformedWidthIs450() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(450, TransformedWidth(window.Inspector, window), 1);
        }

        private static double TransformedWidth(Control control, Window window) {
            Point topLeft = control.TranslatePoint(new Point(0, 0), window)!.Value;
            Point topRight = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value;
            return topRight.X - topLeft.X;
        }

        [AvaloniaFact]
        public async Task LoadingAGraphDocument_ClearsStaleInspectorSelection() {
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = cache.PresetName! };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();

            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsVisible);

            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");
            try {
                await window.SaveGraphAsync(path);
                string json = await File.ReadAllTextAsync(path);

                await window.LoadGraphJsonAsync(json, path);
                Dispatcher.UIThread.RunJobs();

                Assert.False(window.Inspector.IsVisible);
                Assert.Null(window.ViewModel.Inspector.Node);
            } finally {
                File.Delete(path);
            }
        }

        //Fix round: deleting the inspected node used to leave the inspector on a dead node - neither
        //OnNodeViewModelRemoved nor TryDeleteSelection raised SelectionChanged, so nothing told the
        //inspector its selection was gone.
        [AvaloniaFact]
        public async Task DeletingInspectedNode_ViaDeleteKey_ClearsInspectorAndHidesSidebar() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsVisible);

            CanvasKeys.PressOnCanvas(window.GraphCanvas, Key.Delete);

            Assert.Null(window.ViewModel.Inspector.Node);
            Assert.False(window.Inspector.IsVisible);
        }

        [AvaloniaFact]
        public async Task DeletingInspectedNode_ViaTryDeleteSelection_ClearsInspectorAndHidesSidebar() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsVisible);

            window.GraphCanvas.Viewer.TryDeleteSelection();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.ViewModel.Inspector.Node);
            Assert.False(window.Inspector.IsVisible);
        }

        //Fix round: bakedIconCache kept every baked identity-icon Bitmap alive for the life of the process -
        //nothing ever disposed them, even once the window (and the icons with it) was gone.
        [AvaloniaFact]
        public async Task WindowClose_DisposesBakedIdentityIcons() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            FieldInfo field = typeof(InspectorView).GetField("bakedIconCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cache = (Dictionary<SKBitmap, Bitmap>)field.GetValue(window.Inspector)!;
            Assert.NotEmpty(cache);

            window.Content = null; //detaches the visual tree, same as the owning window closing for real

            Assert.Empty(cache);
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
