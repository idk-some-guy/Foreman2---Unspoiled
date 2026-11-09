using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Inspector;
using InspectorGroup = Foreman.Mac.Kit.Composites.InspectorGroup;
using SplitDivider = Foreman.Mac.Kit.Composites.SplitDivider;
using StatePanel = Foreman.Mac.Kit.Composites.StatePanel;
using StatePanelShape = Foreman.Mac.Kit.Composites.StatePanelShape;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    [Collection(ScaleTestGroup.Name)]
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
            Assert.Equal("No node selected", window.Inspector.FindControl<StatePanel>("EmptyState")!.Headline);
        }

        [AvaloniaFact]
        public async Task SelectedRecipeNode_SubtitleShowsNodeTypeThenBuilding() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            string expected = $"Recipe node · {recipe.SelectedAssembler.Assembler.FriendlyName}";
            Assert.Equal(expected, window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
        }

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

        [AvaloniaFact]
        public async Task PickAssembler_AlreadySelected_RaisesNoGraphMutated_AndStaysClean() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            InspectorTestSupport.Select(window, recipe);
            var recipeSegmentView = (RecipeSegmentView)window.Inspector.FindControl<ContentControl>("SegmentHost")!.Content!;
            window.ViewModel.IsDirty = false;

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
            window.ViewModel.IsDirty = false;

            PickGridItem selectedItem = recipeSegmentView.ViewModel!.QualityOptions.Single(i => Equals(i.Key, recipe.SelectedAssembler.Quality));
            recipeSegmentView.ViewModel!.PickQuality(selectedItem);

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void QualityMiniSelect_ListsQualitiesAndPickQuality_WritesThroughAndUpdatesSubtitleAndLabel() {
            DataCache cache = new(filterRecipes: true);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            normal.SetIconAndColor(new IconColorPair(new SKBitmap(4, 4), System.Drawing.Color.White));
            var legendary = new QualityPrototype(cache, "legendary", "Legendary", "b") { Level = 1 };
            legendary.SetIconAndColor(new IconColorPair(new SKBitmap(4, 4), System.Drawing.Color.Orange));
            DataCacheStore store = InspectorTestSupport.Store(cache);
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

            PopUp qualityMiniSelect = window.Inspector.FindControl<PopUp>("QualityMiniSelect")!;
            Assert.True(qualityMiniSelect.IsVisible);
            Assert.Equal(normal.FriendlyName, qualityMiniSelect.SelectedItem);
            Assert.Equal(2, qualityMiniSelect.Items.Count);

            object? normalSource = qualityMiniSelect.Icon;
            Assert.NotNull(normalSource);

            qualityMiniSelect.Command!.Execute(legendary.FriendlyName);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(legendary, node.SelectedAssembler.Quality);
            Assert.Equal($"Recipe node · {electric.FriendlyName}", window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
            Assert.Equal(legendary.FriendlyName, qualityMiniSelect.SelectedItem);
            Assert.NotSame(normalSource, qualityMiniSelect.Icon);
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
        public async Task Inspector_Width_DefaultsTo300_ClampsTo260And380() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.Settings = new AppSettings();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(300, window.Inspector.Bounds.Width, 1);

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();

            divider.Value = 1000;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(380, window.Inspector.Bounds.Width, 1);

            divider.Value = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(260, window.Inspector.Bounds.Width, 1);
        }

        [AvaloniaFact]
        public async Task Inspector_DividerDrag_TowardTheCanvasWidens_EscapeRestores() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();
            Point start = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window) ?? default;

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            Point towardCanvas = new(start.X - 40, start.Y);
            window.MouseMove(towardCanvas, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(340, window.Inspector.Bounds.Width, 1);

            CanvasKeys.KeyDownOn(divider, Key.Escape);
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);
            window.MouseUp(towardCanvas, MouseButton.Left, RawInputModifiers.None);
        }

        [AvaloniaFact]
        public async Task Inspector_DividerDrag_TowardTheWindowEdgeNarrows() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();
            Point start = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window) ?? default;

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            Point towardWindowEdge = new(start.X + 40, start.Y);
            window.MouseMove(towardWindowEdge, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(260, window.Inspector.Bounds.Width, 1);
            window.MouseUp(towardWindowEdge, MouseButton.Left, RawInputModifiers.None);
        }

        [AvaloniaFact]
        public async Task Inspector_DividerDrag_SavesSettingsOnceOnPointerRelease_NotPerMouseMove_NorOnBoot() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var settingsService = new SettingsService(NewTempDir());
            window.SettingsService = settingsService;
            window.Settings = new AppSettings();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            window.ApplyLoadedSettings();
            Assert.Equal(0, settingsService.SaveCallCount);

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();
            Point start = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window) ?? default;
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            for (int step = 1; step <= 4; step++) {
                window.MouseMove(new Point(start.X - (step * 10), start.Y), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(0, settingsService.SaveCallCount);

            window.MouseUp(new Point(start.X - 40, start.Y), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, settingsService.SaveCallCount);
        }

        [AvaloniaFact]
        public async Task Inspector_WidthCommitted_UpdatesSettingsInMemory_AndApplyLoadedSettingsSeedsTheViewModel() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.Settings = new AppSettings();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();
            Point start = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window) ?? default;
            Point towardCanvas = new(start.X - 40, start.Y);
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(towardCanvas, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(towardCanvas, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(340, window.Settings.InspectorWidth);

            window.Settings = new AppSettings { InspectorWidth = 340 };
            window.ApplyLoadedSettings();

            Assert.Equal(340, window.ViewModel.Inspector.Width);
        }

        [AvaloniaFact]
        public async Task Inspector_WidthScalesWithDisplayFactor_ButPersistsLogicalWidth() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.Settings = new AppSettings();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);

            window.Settings.UiScale = 1.5;
            window.ApplyLoadedSettings();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(450, window.Inspector.Bounds.Width, 1);

            SplitDivider divider = window.Inspector.GetVisualDescendants().OfType<SplitDivider>().Single();
            Point start = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window) ?? default;
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(start.X - 60, start.Y), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(new Point(start.X - 60, start.Y), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(window.Settings.InspectorWidth, 260, 380);

            window.Settings.UiScale = 1.0;
            window.ApplyLoadedSettings();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(window.Settings.InspectorWidth, window.Inspector.Bounds.Width, 1);
        }

        [AvaloniaFact]
        public async Task Inspector_GroupsAreSeparatedByASingleOnePointRule_NotPerRow() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            var groups = recipeView.GetVisualDescendants().OfType<InspectorGroup>().Where(g => g.IsVisible).ToList();
            var rules = recipeView.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("inspector-group-rule") && b.IsVisible).ToList();

            Assert.Equal(groups.Count - 1, rules.Count);
            Assert.All(rules, r => Assert.Equal(1, r.Bounds.Height));

            InspectorGroup assemblerGroup = groups.Single(g => Equals(g.Header, "Assembler"));
            bool hairlineInsideBody = assemblerGroup.GetVisualDescendants().OfType<Border>()
                .Any(b => b.Bounds.Height == 1 && b.Bounds.Width >= assemblerGroup.Bounds.Width - 1);
            Assert.False(hairlineInsideBody);
        }

        [AvaloniaFact]
        public async Task Inspector_NoSelection_ShowsTheStatePanelEmptyShape_WithItsButton() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            int calls = 0;
            window.Inspector.AddRecipeCommand = new RelayCommand(() => calls++);
            window.ViewModel.Inspector.IsPinned = true;
            Dispatcher.UIThread.RunJobs();

            StatePanel emptyState = window.Inspector.FindControl<StatePanel>("EmptyState")!;
            Assert.True(emptyState.IsVisible);
            Assert.Equal(StatePanelShape.Empty, emptyState.Shape);
            Assert.Equal("No node selected", emptyState.Headline);
            Button addRecipe = Assert.Single(emptyState.Buttons!.Items.OfType<Button>());
            Assert.Equal("Add Recipe…", addRecipe.Content);

            Point center = addRecipe.TranslatePoint(new Point(addRecipe.Bounds.Width / 2, addRecipe.Bounds.Height / 2), window) ?? default;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public async Task RecipeErrorPanel_OpenPresetsButton_ReselectsThePresetsPaneOnAReusedSettingsWindow() {
            (MainWindow window, _) = await OpenWithMissingRecipeAsync();
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            SettingsWindow? captured = null;
            int constructions = 0;
            window.SettingsDialogStub = w => { captured = w; constructions++; };

            window.ShowSettings();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(captured);
            captured!.SimulateTabClick(3);
            Assert.Equal(3, captured.ActiveTabIndex);

            //GraphViewer.SetSelection with the unchanged element is a no-op after ShowSettings, so select through the inspector.
            window.ViewModel.Inspector.SetSelection([window.GraphCanvas.Viewer.NodeElements.First().ViewModel]);
            Dispatcher.UIThread.RunJobs();

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            StatePanel errorPanel = recipeView.FindControl<StatePanel>("RecipeErrorPanel")!;
            Button openPresets = errorPanel.Buttons!.Items.OfType<Button>().First();
            Assert.Equal("Open Presets…", openPresets.Content);

            Point center = openPresets.TranslatePoint(new Point(openPresets.Bounds.Width / 2, openPresets.Bounds.Height / 2), window) ?? default;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, constructions);
            Assert.Equal(0, captured!.ActiveTabIndex);
        }

        [AvaloniaFact]
        public async Task RecipeErrorPanel_PickAnotherRecipeButton_OpensTheBrowserSeededForTheNode() {
            (MainWindow window, IRecipeNodeViewModel node) = await OpenWithMissingRecipeAsync();

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            StatePanel errorPanel = recipeView.FindControl<StatePanel>("RecipeErrorPanel")!;
            Button pickAnotherRecipe = errorPanel.Buttons!.Items.OfType<Button>().Last();
            Assert.Equal("Pick Another Recipe…", pickAnotherRecipe.Content);

            Point center = pickAnotherRecipe.TranslatePoint(new Point(pickAnotherRecipe.Bounds.Width / 2, pickAnotherRecipe.Bounds.Height / 2), window) ?? default;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(window.BrowserWindow);
            Assert.True(window.BrowserWindow!.IsVisible);
            Assert.Equal(AddScope.Recipes, window.BrowserRequest!.Scope);
            Assert.Equal(node.BaseRecipe.FriendlyName, window.BrowserRequest!.SeedText);
        }

        //280pt is the narrowest inspector width at which the error panel leaves its compact layout.
        [AvaloniaFact]
        public async Task RecipeErrorPanel_NearTheInspectorsNarrowEnd_BothButtonsStayInsideTheInspector() {
            (MainWindow window, _) = await OpenWithMissingRecipeAsync();
            window.ViewModel.Inspector.Width = 280;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(280, window.Inspector.Bounds.Width, 1);

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            StatePanel errorPanel = recipeView.FindControl<StatePanel>("RecipeErrorPanel")!;
            Assert.False(errorPanel.IsCompact);

            foreach (Button b in errorPanel.Buttons!.Items.OfType<Button>()) {
                Point topLeft = b.TranslatePoint(new Point(0, 0), window.Inspector) ?? default;
                Assert.True(topLeft.X >= -0.5, $"{b.Content}: left edge clipped at {topLeft.X}");
                Assert.True(topLeft.X + b.Bounds.Width <= window.Inspector.Bounds.Width + 0.5,
                    $"{b.Content}: right edge at {topLeft.X + b.Bounds.Width} overflows the inspector's width {window.Inspector.Bounds.Width}");
            }
        }

        private static async Task<(MainWindow Window, IRecipeNodeViewModel Node)> OpenWithMissingRecipeAsync() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            window.Settings = new AppSettings();
            DataCache cache = window.DataCache!;
            var ironPlate = (RecipePrototype)cache.Recipes["iron-plate"];
            var assembler = (AssemblerPrototype)ironPlate.Assemblers.First();
            var missingRecipe = new RecipePrototype(cache, "inspector-shell-tests-missing-recipe", "Missing Recipe",
                ironPlate.MySubgroupInternal, "a", isMissing: true) { Available = true };
            missingRecipe.AssemblersInternal.Add(assembler);
            assembler.RecipesInternal.Add(missingRecipe);

            DataCacheStore store = InspectorTestSupport.Store(cache);
            store.Recipes[missingRecipe.Name] = missingRecipe;

            var pair = new RecipeQualityPair(missingRecipe, cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            return (window, (IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel);
        }

        [AvaloniaFact]
        public async Task Inspector_GroupPersistedCollapsed_RendersCollapsedOnFirstLayout_BodyNeverMeasuredExpanded() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            InspectorGroup beaconGroup = recipeView.FindControl<InspectorGroup>("BeaconGroup")!;
            Assert.False(beaconGroup.IsExpanded);

            Foreman.Mac.Kit.Composites.PickGrid beaconChoiceGrid = recipeView.FindControl<Foreman.Mac.Kit.Composites.PickGrid>("BeaconChoiceGrid")!;
            Assert.DoesNotContain(beaconChoiceGrid, beaconGroup.GetVisualDescendants());
        }

        [AvaloniaFact]
        public async Task Inspector_IdentitySubtitle_NamesTheNodeTypeThenTheBuilding() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(window, node);

            string subtitle = window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text!;
            Assert.Equal("Passthrough node", subtitle);
            Assert.DoesNotContain("·", subtitle);
        }

        [AvaloniaFact]
        public async Task Inspector_IdentitySubtitle_CarriesTheBuildingLine_AndAppendsQualityWhenNotBase() {
            DataCache cache = await InspectorTestSupport.GetSpaceAgeCacheAsync();
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync(cache);

            var node = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            InspectorTestSupport.Select(window, node);

            TextBlock subtitle = window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!;
            string building = node.SelectedAssembler.Assembler.FriendlyName;
            Assert.Equal($"Recipe node · {building}", subtitle.Text);
            Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, subtitle.TextTrimming);
            subtitle.MaxWidth = 40;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(subtitle.Text, ToolTip.GetTip(subtitle)?.ToString());

            IQuality? higher = cache.DefaultQuality!.NextQuality;
            Assert.NotNull(higher);
            PopUp qualityMiniSelect = window.Inspector.FindControl<PopUp>("QualityMiniSelect")!;
            qualityMiniSelect.Command!.Execute(higher!.FriendlyName);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal($"Recipe node · {building} ({higher!.FriendlyName})", subtitle.Text);
            Assert.Equal(subtitle.Text, ToolTip.GetTip(subtitle)?.ToString());
        }

        [AvaloniaFact]
        public async Task Inspector_TrimmedIdentitySubtitle_IsNotReadTwiceByVoiceOver() {
            DataCache cache = await InspectorTestSupport.GetSpaceAgeCacheAsync();
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync(cache);
            InspectorTestSupport.Select(window, window.GraphCanvas.Viewer.Session.View.Nodes.Single());

            TextBlock subtitle = window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!;
            subtitle.MaxWidth = 40;
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(ToolTip.GetTip(subtitle));
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(subtitle);
            Assert.False(string.IsNullOrEmpty(peer.GetName()));
            Assert.NotEqual(peer.GetName(), peer.GetHelpText());
        }

        [AvaloniaFact]
        public async Task IdentityRow_AutomationPeer_IsAGroupNamedAfterTheNode_WithTheSubtitleAsItsValue() {
            DataCache cache = await InspectorTestSupport.GetSpaceAgeCacheAsync();
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync(cache);

            var node = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            InspectorTestSupport.Select(window, node);

            IdentityRow identityRow = window.Inspector.FindControl<IdentityRow>("IdentityRow")!;
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(identityRow);
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.Equal(node.BaseRecipe.FriendlyName, peer.GetName());
            Assert.Equal($"Recipe node · {node.SelectedAssembler.Assembler.FriendlyName}", peer.GetProvider<IValueProvider>()?.Value);
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
                await window.ExportGraphAsync(path);
                string json = await File.ReadAllTextAsync(path);

                await window.LoadGraphJsonAsync(json, "Saved");
                Dispatcher.UIThread.RunJobs();

                Assert.False(window.Inspector.IsVisible);
                Assert.Null(window.ViewModel.Inspector.Node);
            } finally {
                File.Delete(path);
            }
        }

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

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [AvaloniaFact]
        public async Task Inspector_LabelColumnIs108_AndEveryControlSharesOneEdge_WithNoBorderBetweenRows() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            List<FormRow> rows = [.. recipeView.GetVisualDescendants().OfType<FormRow>().Where(r => r.IsVisible)];
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Inspector, r.LabelColumn));

            Assert.True(Application.Current!.TryGetResource("Foreman.Size.FormLabelColumnInspector", Application.Current.ActualThemeVariant, out object? widthResource));
            Assert.Equal(108d, (double)widthResource!);

            double? sharedControlEdge = null;
            foreach (FormRow row in rows) {
                Control control = Assert.IsAssignableFrom<Control>(row.Content);
                double leading = control.TranslatePoint(new Point(0, 0), window)!.Value.X;
                sharedControlEdge ??= leading;
                Assert.True(Math.Abs(leading - sharedControlEdge.Value) <= 1, $"control leading edge {leading} != shared edge {sharedControlEdge}");
            }

            Assert.All(recipeView.GetVisualDescendants().OfType<FormGroup>(),
                group => Assert.All(group.Items, item => Assert.IsType<FormRow>(item)));
        }

        [AvaloniaFact]
        public async Task Inspector_LowPriorityKeyNodeAndSimplify_AreCheckboxes_WritingThroughOnSpace() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            InspectorTestSupport.Select(window, recipe);
            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            Assert.Empty(recipeView.GetVisualDescendants().OfType<ToggleSwitch>());

            CheckBox lowPriority = recipeView.FindControl<CheckBox>("LowPriority")!;
            Assert.False(lowPriority.IsChecked);
            PressSpace(window, lowPriority);
            Assert.True(lowPriority.IsChecked);
            Assert.True(recipe.LowPriority);

            CheckBox keyNode = recipeView.FindControl<CheckBox>("KeyNode")!;
            PressSpace(window, keyNode);
            Assert.True(keyNode.IsChecked);
            Assert.True(recipe.KeyNode);

            (MainWindow flowWindow, INodeViewModel passthrough) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(flowWindow, passthrough);
            var flowView = flowWindow.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Assert.Empty(flowView.GetVisualDescendants().OfType<ToggleSwitch>());

            CheckBox simplify = flowView.FindControl<CheckBox>("Simplify")!;
            PressSpace(flowWindow, simplify);
            Assert.True(simplify.IsChecked);
            Assert.True(((IPassthroughNodeViewModel)passthrough).SimpleDraw);
        }

        private static void PressSpace(MainWindow window, CheckBox checkBox) {
            checkBox.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            checkBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            Dispatcher.UIThread.RunJobs();
            checkBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public async Task Inspector_QualityAndRateUnit_ArePopUps_ListingEveryValue() {
            //Vanilla ships no qualities, so the Quality pop-up needs the Space Age preset.
            DataCache cache = await InspectorTestSupport.GetSpaceAgeCacheAsync();
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync(cache);

            var node = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            InspectorTestSupport.Select(window, node);
            var recipeView = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;

            PopUp qualityMiniSelect = window.Inspector.FindControl<PopUp>("QualityMiniSelect")!;
            Assert.Equal(recipeView.ViewModel!.QualityOptions.Count, qualityMiniSelect.Items.Count);
            Assert.True(qualityMiniSelect.Items.Count > 0);

            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            PopUp rateUnit = window.GetVisualDescendants().OfType<PopUp>().Single(p => p.Name == "RateOptionsDropDown");
            Assert.Equal(MainWindow.RateUnitOptions, rateUnit.Items);
        }
    }
}
