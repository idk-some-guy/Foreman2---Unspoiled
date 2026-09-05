using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Task 7's phase gate: builds a small factory purely through synthesized interactions - the chooser path,
    //link drag, clipboard, annotations, and the Delete key all in one flow - then proves it round-trips
    //through a real save/reload (reference §11 step 12's "P4 demoable end-to-end" goal).
    public class EditingEndToEndTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const int Half = 400;

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private static GraphCanvasControl NewControl(DataCache cache) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();
            return control;
        }

        //C2b: Add Recipe now opens the Quick Add palette scoped to recipes - searching the recipe's own
        //friendly name and pressing Enter picks the top (and only) match. Closes the palette afterward (it
        //stays open by design for a second pick) so callers get back a clean canvas.
        private static Task<RecipeNodeElement> AddRecipeViaChooser(GraphCanvasControl control, DataCache cache, string recipeName, Point location) {
            IRecipe recipe = cache.Recipes[recipeName];
            int before = control.NodeElements.Count;
            control.AddRecipeAsync(location);
            var palette = Assert.IsType<QuickAddPalette>(control.FloatingPanelHost.Content);
            ((QuickAddViewModel)palette.DataContext!).Query = recipe.FriendlyName;
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            control.FloatingPanelHost.Close();
            Assert.Equal(before + 1, control.NodeElements.Count);
            return Task.FromResult((RecipeNodeElement)control.NodeElements[^1]);
        }

        private static void DragLinkBetween(GraphCanvasControl control, RecipeNodeElement producer, RecipeNodeElement consumer, ItemQualityPair item) {
            ItemTabElement outputTab = producer.GetOutputLineItemTab(item);
            AvaloniaPoint outputScreen = control.Viewport.GraphToScreen(producer.LocalToGraph(outputTab.Location));
            AvaloniaWindow window = (AvaloniaWindow)Avalonia.Controls.TopLevel.GetTopLevel(control)!;

            window.MouseDown(outputScreen, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(outputScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(outputScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);

            AvaloniaPoint consumerScreen = control.Viewport.GraphToScreen(consumer.Location);
            window.MouseMove(consumerScreen, RawInputModifiers.LeftMouseButton);
            window.MouseUp(consumerScreen, MouseButton.Left, RawInputModifiers.None);
        }

        //Plain click (MouseDown+MouseUp at the same screen point, no drag) - drives the real click-to-edit
        //path (Task 6) rather than Click(Control)'s direct-to-control synth above, which is for panel-internal
        //buttons once a panel is already open.
        private static void ClickCanvasPoint(GraphCanvasControl control, Point screenPoint) {
            var window = (AvaloniaWindow)Avalonia.Controls.TopLevel.GetTopLevel(control)!;
            var avaloniaPoint = new AvaloniaPoint(screenPoint.X, screenPoint.Y);
            window.MouseDown(avaloniaPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(avaloniaPoint, MouseButton.Left, RawInputModifiers.None);
        }

        private static void ClickNodeBody(GraphCanvasControl control, BaseNodeElement node) {
            AvaloniaPoint screen = control.Viewport.GraphToScreen(node.Location);
            ClickCanvasPoint(control, new Point((int)screen.X, (int)screen.Y));
        }

        //MainWindow nests GraphCanvas under a toolbar/menu bar, unlike NewControl's bare
        //window-is-the-canvas fixture above, so a real click needs the canvas point translated into the
        //window's own coordinate space first.
        private static void ClickNodeBodyOnMainWindow(MainWindow window, BaseNodeElement node) {
            AvaloniaPoint canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(node.Location);
            AvaloniaPoint screen = Avalonia.VisualExtensions.TranslatePoint(window.GraphCanvas, canvasPoint, window) ?? canvasPoint;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
        }

        //A real MouseDown/Up through the window, translated from the target control's own bounds - unlike
        //Click(Control)'s direct-to-control synth above, this is what a PickGrid cell (a real Avalonia
        //Button) needs to register its own Click.
        private static void ClickThroughWindow(AvaloniaWindow window, Avalonia.Controls.Control control) {
            var center = new AvaloniaPoint(control.Bounds.Width / 2, control.Bounds.Height / 2);
            AvaloniaPoint screen = Avalonia.VisualExtensions.TranslatePoint(control, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //Task 7's phase gate: the real left-click-to-edit dispatch (docs/panels-reference.md §8) selects a
        //recipe node, then the assembler is changed through the inspector's own Recipe segment (the paired
        //floating editor this test used to open is retired), before proving the edit survives a save/reload
        //round-trip.
        [AvaloniaFact]
        public async Task ClickToEdit_RecipeNode_ChangeAssembler_PersistsThroughSaveAndReload() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.GraphCanvas.Viewport.SetSize(2 * Half, 2 * Half);
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;

            RecipeNodeElement smelter = await AddRecipeViaChooser(window.GraphCanvas, cache, "iron-plate", new Point(0, 0));
            var recipeViewModel = (IRecipeNodeViewModel)smelter.ViewModel;
            IAssembler originalAssembler = recipeViewModel.SelectedAssembler.Assembler;
            Dispatcher.UIThread.RunJobs();

            ClickNodeBodyOnMainWindow(window, smelter);
            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
            Assert.Contains(smelter, window.GraphCanvas.Viewer.SelectedNodes);

            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var assemblerGrid = view.FindControl<PickGrid>("AssemblerGrid")!;
            Button alternateAssemblerButton = assemblerGrid.GetVisualDescendants().OfType<Button>()
                .First(b => !Equals((b.DataContext as PickGridItem)?.Key, originalAssembler));
            var alternateAssembler = (IAssembler)((PickGridItem)alternateAssemblerButton.DataContext!).Key!;
            ClickThroughWindow(window, alternateAssemblerButton);
            Assert.Equal(alternateAssembler, recipeViewModel.SelectedAssembler.Assembler);

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(window.GraphCanvas.Viewer.Graph),
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            GraphCanvasControl reloaded = NewControl(cache);
            GraphLoadResult result = reloaded.Viewer.LoadDocument(cache, json);
            Assert.True(result.Success, result.ErrorMessage);

            var reloadedRecipe = (IRecipeNodeViewModel)reloaded.Viewer.Session.View.Nodes.Single();
            Assert.Equal(alternateAssembler, reloadedRecipe.SelectedAssembler.Assembler);
        }

        //Same phase gate for the other dispatch branch - a supplier node's fixed rate, set through the real
        //click-then-sidebar path: a real click selects the node (GraphCanvasControl.EditNode), then the
        //value is set through the inspector's own Flow segment controls (RateFixed/RateValue), not a direct
        //controller call, so this still proves "edit through the real sidebar controls, save, reload" end
        //to end.
        [AvaloniaFact]
        public async Task ClickToEdit_SupplierNode_SetFixedRate_PersistsThroughSaveAndReload() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.GraphCanvas.Viewport.SetSize(2 * Half, 2 * Half);
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;

            IItem ironPlate = cache.Items["iron-plate"];
            IQuality quality = cache.DefaultQuality!;
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ironPlate, quality), new Point(0, 0));
            BaseNodeElement supplier = window.GraphCanvas.NodeElements.Single(n => n.ViewModel.Id == supplierId);
            supplier.RequestStateUpdate();
            supplier.PrePaint(); //stands in for the first real paint pass that would otherwise position its item tab (see ClickToEditTests)
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            ClickNodeBodyOnMainWindow(window, supplier);
            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
            Assert.Contains(supplier, window.GraphCanvas.Viewer.SelectedNodes);

            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            view.FindControl<RadioButton>("RateFixed")!.IsChecked = true;
            view.FindControl<CompactStepper>("RateValue")!.Value = 12.5;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(RateType.Manual, supplier.ViewModel.RateType);
            Assert.Equal(12.5, supplier.ViewModel.DesiredSetValue, 3);

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(window.GraphCanvas.Viewer.Graph),
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            GraphCanvasControl reloaded = NewControl(cache);
            GraphLoadResult result = reloaded.Viewer.LoadDocument(cache, json);
            Assert.True(result.Success, result.ErrorMessage);

            INodeViewModel reloadedSupplier = reloaded.Viewer.Session.View.Nodes.Single();
            Assert.Equal(RateType.Manual, reloadedSupplier.RateType);
            Assert.Equal(12.5, reloadedSupplier.DesiredSetValue, 3);
        }

        [AvaloniaFact]
        public async Task BuildSmallFactory_ThroughSynthesizedInteractions_SurvivesSaveAndReload() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewControl(cache);

            //---- add two nodes via the chooser path (programmatic selection) ----
            RecipeNodeElement smelter = await AddRecipeViaChooser(control, cache, "iron-plate", new Point(-250, 0));
            RecipeNodeElement crafter = await AddRecipeViaChooser(control, cache, "iron-gear-wheel", new Point(250, 0));
            Assert.Equal(2, control.NodeElements.Count);

            //---- connect via link drag ----
            ItemQualityPair ironPlate = ((IRecipeNodeViewModel)smelter.ViewModel).Outputs.Single(o => o.Item?.Name == "iron-plate");
            DragLinkBetween(control, smelter, crafter, ironPlate);
            Assert.Single(control.Viewer.Session.View.Links);
            INodeLinkViewModel link = control.Viewer.Session.View.Links.Single();
            Assert.Equal(smelter.ViewModel.Id, link.SupplierId);
            Assert.Equal(crafter.ViewModel.Id, link.ConsumerId);

            //---- copy/paste the pair ----
            control.Viewer.SetSelection([smelter, crafter]);
            string fragment = NodeClipboard.Copy(control.Viewer);
            NodeClipboard.Paste(control.Viewer, cache, fragment, new Point(0, 400));
            Assert.Equal(4, control.NodeElements.Count);
            Assert.Equal(2, control.Viewer.SelectedNodes.Count); //paste replaced the selection with just the pasted pair

            //---- annotate ----
            var annotation = new ShapeAnnotationElement(new Point(0, -250));
            control.Viewer.AddAnnotationElement(annotation);
            Assert.Single(control.Viewer.Annotations);

            //---- Delete one node via keyboard (reference §7: no annotation selected -> node-only delete) ----
            BaseNodeElement toDelete = control.Viewer.SelectedNodes.First(); //one of the just-pasted pair
            control.Viewer.SetSelection([toDelete]);
            control.Focus();
            CanvasKeys.PressOnCanvas(control, Key.Delete);
            Assert.Equal(3, control.NodeElements.Count);

            //---- save to string, reload ----
            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(control.Viewer.Graph),
                Annotations = [.. control.Viewer.Annotations.Select(a => a.ToSaveData())],
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            GraphCanvasControl reloaded = NewControl(cache);
            GraphLoadResult result = reloaded.Viewer.LoadDocument(cache, json);

            //---- assert counts/positions ----
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(control.NodeElements.Count, reloaded.NodeElements.Count);
            Assert.Equal(control.Viewer.Session.View.Links.Count, reloaded.Viewer.Session.View.Links.Count);
            Assert.Equal(control.Viewer.Annotations.Count, reloaded.Viewer.Annotations.Count);

            List<Point> originalLocations = [.. control.NodeElements.Select(n => n.Location).OrderBy(p => p.X).ThenBy(p => p.Y)];
            List<Point> reloadedLocations = [.. reloaded.NodeElements.Select(n => n.Location).OrderBy(p => p.X).ThenBy(p => p.Y)];
            Assert.Equal(originalLocations, reloadedLocations);
        }

        //---- demo render for the SDD workspace (task 7's own visual-gate substitute, same offscreen
        //technique GraphViewerIntegrationTests' phase-3 gate already established) ----

        [AvaloniaFact]
        public async Task VisualGate_BuiltFactory_OverviewRendersToSddWorkspace() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewControl(cache);

            RecipeNodeElement smelter = await AddRecipeViaChooser(control, cache, "iron-plate", new Point(-250, 0));
            RecipeNodeElement crafter = await AddRecipeViaChooser(control, cache, "iron-gear-wheel", new Point(250, 0));
            ItemQualityPair ironPlate = ((IRecipeNodeViewModel)smelter.ViewModel).Outputs.Single(o => o.Item?.Name == "iron-plate");
            DragLinkBetween(control, smelter, crafter, ironPlate);
            control.Viewer.AddAnnotationElement(new ShapeAnnotationElement(new Point(0, -220)));
            control.Viewer.Graph.UpdateNodeValues();

            Rectangle bounds = control.Viewer.Graph.Bounds;
            float scale = Math.Min((float)(2 * Half / (double)bounds.Width), (float)(2 * Half / (double)bounds.Height)) * 0.9f;
            control.Viewport.ViewScale = Math.Clamp(scale, Viewport.MinViewScale, Viewport.MaxViewScale);
            var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            control.Viewport.ViewOffset = new Point(-center.X, -center.Y);
            control.Viewport.UpdateGraphBounds();

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            control.Render(surface.Canvas);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase4-editing");
            Directory.CreateDirectory(workspaceDir);
            File.WriteAllBytes(Path.Combine(workspaceDir, "task-7-overview.png"), data.ToArray());
            File.WriteAllText(Path.Combine(workspaceDir, "task-7-render-metrics.txt"),
                $"Synthesized factory: {control.NodeElements.Count} nodes, {control.LinkElements.Count} links, {control.Viewer.Annotations.Count} annotations{Environment.NewLine}");

            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-7-overview.png")));
        }
    }
}
