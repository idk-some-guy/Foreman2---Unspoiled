using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
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
using InputPointer = Avalonia.Input.Pointer;

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

        //Drives the real RecipeChooserPanel (task 3, docs/panels-reference.md §2/§8) rather than the deleted
        //placeholder's stub: Add Recipe opens the panel directly with an empty KeyItem, so a plain click on
        //the named recipe's icon is the whole flow (no footer buttons, no item-choosing stage first). Unlike
        //the placeholder's flat list, the real panel only shows the currently selected group's recipes -
        //switches to the target recipe's own group first when it isn't already showing.
        private static Task<RecipeNodeElement> AddRecipeViaChooser(GraphCanvasControl control, DataCache cache, string recipeName, Point location) {
            IRecipe recipe = cache.Recipes[recipeName];
            int before = control.NodeElements.Count;
            control.AddRecipeAsync(location);
            var panel = Assert.IsType<RecipeChooserPanel>(control.FloatingPanelHost.Content);

            IconButton? cell = panel.GetVisualDescendants().OfType<IconButton>().FirstOrDefault(b => Equals(b.DataObject, recipe));
            if (cell is null && recipe.MySubgroup.MyGroup is IGroup targetGroup) {
                IconButton groupButton = panel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, targetGroup));
                Click(groupButton);
                cell = panel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, recipe));
            }
            Click(cell!);
            Assert.Equal(before + 1, control.NodeElements.Count);
            return Task.FromResult((RecipeNodeElement)control.NodeElements[^1]);
        }

        private static void Click(Avalonia.Controls.Control control) {
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            var args = new PointerReleasedEventArgs(control, pointer, control, default, 0, properties, KeyModifiers.None, MouseButton.Left);
            control.RaiseEvent(args);
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

        //Task 6's phase gate: the real left-click-to-edit dispatch (docs/panels-reference.md §8), opening a
        //real EditRecipePanel through the same click routing BuildSmallFactory's chooser-only flow above never
        //exercises, then closing it via a plain canvas click elsewhere (Task 1's click-outside-closes,
        //reference §7) before proving the edit itself survives a save/reload round-trip.
        [AvaloniaFact]
        public async Task ClickToEdit_RecipeNode_ChangeAssembler_PersistsThroughSaveAndReload() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewControl(cache);

            RecipeNodeElement smelter = await AddRecipeViaChooser(control, cache, "iron-plate", new Point(0, 0));
            var recipeViewModel = (IRecipeNodeViewModel)smelter.ViewModel;
            IAssembler originalAssembler = recipeViewModel.SelectedAssembler.Assembler;

            ClickNodeBody(control, smelter);
            var panel = Assert.IsType<EditRecipePanel>(control.FloatingPanelHost.Content);

            IconButton alternateAssemblerButton = panel.AssemblerOptions.First(b => !Equals(b.DataObject, originalAssembler));
            var alternateAssembler = (IAssembler)alternateAssemblerButton.DataObject!;
            Click(alternateAssemblerButton);
            Assert.Equal(alternateAssembler, recipeViewModel.SelectedAssembler.Assembler);

            //Same click that closes the panel still lands on the canvas underneath it (Task 1 semantics) -
            //here it's just empty space, so nothing else happens beyond the close itself.
            ClickCanvasPoint(control, new Point(2 * Half - 20, 2 * Half - 20));
            Assert.False(control.FloatingPanelHost.IsOpen);

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(control.Viewer.Graph),
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            GraphCanvasControl reloaded = NewControl(cache);
            GraphLoadResult result = reloaded.Viewer.LoadDocument(cache, json);
            Assert.True(result.Success, result.ErrorMessage);

            var reloadedRecipe = (IRecipeNodeViewModel)reloaded.Viewer.Session.View.Nodes.Single();
            Assert.Equal(alternateAssembler, reloadedRecipe.SelectedAssembler.Assembler);
        }

        //Same phase gate for the other dispatch branch (RecipeNodeElement -> EditRecipePanel above,
        //everything else -> EditFlowPanel here) - a supplier node's fixed rate, set through the real click
        //path rather than direct panel construction (EditFlowPanelTests' own job).
        [AvaloniaFact]
        public async Task ClickToEdit_SupplierNode_SetFixedRate_PersistsThroughSaveAndReload() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewControl(cache);

            IItem ironPlate = cache.Items["iron-plate"];
            IQuality quality = cache.DefaultQuality!;
            NodeId supplierId = control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ironPlate, quality), new Point(0, 0));
            BaseNodeElement supplier = control.NodeElements.Single(n => n.ViewModel.Id == supplierId);
            supplier.RequestStateUpdate();
            supplier.PrePaint(); //stands in for the first real paint pass that would otherwise position its item tab (see ClickToEditTests)

            ClickNodeBody(control, supplier);
            var panel = Assert.IsType<EditFlowPanel>(control.FloatingPanelHost.Content);

            panel.FixedOption.IsChecked = true;
            panel.FixedFlowInput.Value = 12.5m;
            Assert.Equal(RateType.Manual, supplier.ViewModel.RateType);
            Assert.Equal(12.5, supplier.ViewModel.DesiredSetValue, 3);

            ClickCanvasPoint(control, new Point(2 * Half - 20, 2 * Half - 20));
            Assert.False(control.FloatingPanelHost.IsOpen);

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(control.Viewer.Graph),
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
            AvaloniaWindow window = (AvaloniaWindow)Avalonia.Controls.TopLevel.GetTopLevel(control)!;
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);
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

        //---- Task 6 deliverable: a node with its edit panel open, opened through the real click dispatch
        //rather than direct panel construction (EditRecipePanelTests' own render already covers the panel in
        //isolation) ----

        [AvaloniaFact]
        public async Task VisualGate_ClickToEdit_RecipeNodeWithEditPanelOpen_RendersToSddWorkspace() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewControl(cache);

            RecipeNodeElement smelter = await AddRecipeViaChooser(control, cache, "iron-plate", new Point(0, 0));
            control.Viewer.Graph.UpdateNodeValues();

            ClickNodeBody(control, smelter);
            var panel = Assert.IsType<EditRecipePanel>(control.FloatingPanelHost.Content);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            control.Render(surface.Canvas);

            Rectangle panelBounds = control.FloatingPanelHost.Bounds;
            surface.Canvas.Save();
            surface.Canvas.Translate(panelBounds.X, panelBounds.Y);
            panel.RenderOffscreen(surface.Canvas, panelBounds.Width, panelBounds.Height);
            surface.Canvas.Restore();

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(workspaceDir);
            string outPath = Path.Combine(workspaceDir, "task-6-click-to-edit-render.png");
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }
    }
}
