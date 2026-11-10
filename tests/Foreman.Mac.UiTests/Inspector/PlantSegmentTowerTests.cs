using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.UiTests.Inspector {
    public class PlantSegmentTowerTests {
        private static readonly EffectRange Wide = new(-1000, 1000);
        private static readonly EffectLimits WideLimits = new(Wide, Wide, Wide, Wide, new EffectRange(0, 1000));

        internal sealed class Rig {
            public required DataCache Cache;
            public required IQuality Normal;
            public required IQuality? Rare;
            public required GraphCanvasControl Control;
            public required AvaloniaWindow Host;
            public required PlantProcessPrototype Process;
            public required AssemblerPrototype Tower;
            public required ModulePrototype Productivity;
            public required ModulePrototype Quality;
            public required ModulePrototype Speed;
            public ProductionGraph Graph => Control.Viewer.Graph;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        internal static Rig NewRig(bool rareQuality = true, bool slottedTower = true, string seedName = "seed") {
            var cache = new DataCache(filterRecipes: true);
            DataCacheStore store = Store(cache);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            store.Qualities[normal.Name] = normal;
            store.DefaultQuality = normal;
            QualityPrototype? rare = null;
            if (rareQuality) {
                rare = new QualityPrototype(cache, "rare", "Rare", "b") { Level = 1, PrevQuality = normal };
                normal.NextQuality = rare;
                store.Qualities[rare.Name] = rare;
            }

            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(cache, name, name, subgroup, "a") { Available = true };
                store.Items[name] = item;
                return item;
            }
            ItemPrototype seed = NewItem(seedName);
            ItemPrototype crop = NewItem("crop");
            var process = new PlantProcessPrototype(cache, "plant-seed");
            process.InternalOneWayAddProduct(crop, 50);
            process.Seed = seed;
            seed.PlantResult = process;

            ModulePrototype NewModule(string name, double productivity, double quality, double speed) {
                NewItem(name);
                var module = new ModulePrototype(cache, name, name) { ProductivityBonus = productivity, QualityBonus = quality, SpeedBonus = speed, Category = "test" };
                store.Modules[name] = module;
                return module;
            }
            ModulePrototype productivityModule = NewModule("Productivity module 3", 0.1, 0, 0);
            ModulePrototype qualityModule = NewModule("Quality module 3", 0, 0.1, 0);
            ModulePrototype speedModule = NewModule("Speed module 3", 0, 0, 0.2);

            var tower = new AssemblerPrototype(cache, "test-tower", "Test tower", EntityType.AgriculturalTower, EnergySource.Electric) {
                Available = true,
                EffectLimits = WideLimits,
                ModuleSlots = slottedTower ? 4 : 0,
                AllowModules = true,
            };
            foreach (ModulePrototype module in new[] { productivityModule, qualityModule, speedModule })
                tower.ModulesInternal.Add(module);
            store.Assemblers[tower.Name] = tower;

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            var host = new AvaloniaWindow { Content = control, Width = 900, Height = 900 };
            host.Show();
            return new Rig { Cache = cache, Normal = normal, Rare = rare, Control = control, Host = host, Process = process, Tower = tower, Productivity = productivityModule, Quality = qualityModule, Speed = speedModule };
        }

        internal static (IPlantNodeViewModel Node, PlantNodeController Controller) NewPlantNode(Rig rig, IPlantProcess? process = null, IQuality? quality = null) {
            NodeId id = rig.Control.Viewer.Session.Editor.CreatePlantNode(process ?? rig.Process, quality ?? rig.Normal, new Point(0, 0));
            Assert.True(rig.Control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            var controller = (PlantNodeController)rig.Control.Viewer.Session.Editor.RequestNodeController(id)!;
            return ((IPlantNodeViewModel)node!, controller);
        }

        private static (AvaloniaWindow Host, FlowSegmentView View) Open(Rig rig, INodeViewModel node) {
            var view = new FlowSegmentView();
            var host = new AvaloniaWindow { Content = view, Width = 400, Height = 900 };
            host.Show();
            rig.Graph.UpdateNodeValues();
            view.Bind(node, rig.Control.Viewer);
            Settle(host);
            return (host, view);
        }

        private static void Settle(AvaloniaWindow host) {
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(host);
            host.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        private static T Req<T>(FlowSegmentView view, string name) where T : Control {
            T? control = view.FindControl<T>(name);
            Assert.NotNull(control);
            return control;
        }

        private static string[] VisibleHeaders(FlowSegmentView view) =>
            [.. view.GetVisualDescendants().OfType<InspectorGroup>().Where(g => g.IsEffectivelyVisible).Select(g => (string)g.Header!)];

        private static void ChooseSlot(KitPickGrid grid, int index) {
            grid.FocusedIndex = index;
            CanvasKeys.KeyDownOn(grid, Key.Return);
        }

        private static void ClearSlot(KitPickGrid grid, int index) {
            grid.FocusedIndex = index;
            CanvasKeys.KeyDownOn(grid, Key.Delete);
        }

        [AvaloniaFact]
        public async Task PlantSegment_NoSlottedTower_LooksAsToday() {
            DataCache cache = await InspectorTestSupport.GetSpaceAge21CacheAsync();
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            NodeId id = control.Viewer.Session.Editor.CreatePlantNode(cache.Items["yumako-seed"].PlantResult!, cache.DefaultQuality!, new Point(0, 0));
            control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node);
            var view = new FlowSegmentView();
            var host = new AvaloniaWindow { Content = view, Width = 400, Height = 900 };
            host.Show();
            control.Viewer.Graph.UpdateNodeValues();
            view.Bind(node!, control.Viewer);
            Settle(host);

            Assert.Equal(["Rate", "Node"], VisibleHeaders(view));
            Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("inspector-group-rule") && b.IsEffectivelyVisible);
            Assert.False(Req<InspectorGroup>(view, "TowerGroup").IsEffectivelyVisible);
            Assert.False(Req<InspectorGroup>(view, "TowerModulesGroup").IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void PlantSegment_SlottedTower_NoneSelected() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, _) = NewPlantNode(rig);
            (_, FlowSegmentView view) = Open(rig, node);

            KitPickGrid grid = Req<KitPickGrid>(view, "TowerChoiceGrid");
            Assert.True(grid.Slots[0].IsNone);
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Equal("No tower", Req<FormRow>(view, "TowerRow").Note);
            Assert.False(Req<FormRow>(view, "TowerQualityRow").IsEffectivelyVisible);
            Assert.False(Req<InspectorGroup>(view, "TowerModulesGroup").IsEffectivelyVisible);
            Assert.Equal(["Rate", "Tower", "Node"], VisibleHeaders(view));
        }

        [AvaloniaFact]
        public void PlantSegment_PickTower_SetsTowerAndShowsModules() {
            Rig rig = NewRig();
            rig.Graph.DefaultAssemblerQuality = rig.Rare;
            (IPlantNodeViewModel node, _) = NewPlantNode(rig);
            (_, FlowSegmentView view) = Open(rig, node);

            ChooseSlot(Req<KitPickGrid>(view, "TowerChoiceGrid"), 1);
            Settle(rig.Host);

            Assert.Equal(rig.Tower, node.Tower!.Value.Assembler);
            Assert.Equal(rig.Rare, node.Tower.Value.Quality);
            Assert.Equal("Modules (0/4)", Req<InspectorGroup>(view, "TowerModulesGroup").Header);
            Assert.Equal(4, Req<KitPickGrid>(view, "EquippedTowerGrid").Slots.Count);
            FormRow row = Req<FormRow>(view, "TowerRow");
            Assert.Equal("Test tower", row.Note);
            Assert.Equal("4 module slots", row.NoteSuffix);
        }

        [AvaloniaFact]
        public void PlantSegment_ModuleOptions_OnlyProductivity() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            KitPickGrid options = Req<KitPickGrid>(view, "TowerModuleOptionsGrid");
            Assert.Equal(["Productivity module 3"], options.Slots.Select(s => s.Name));
            Assert.Equal("Only productivity changes the harvest.", Req<FormRow>(view, "TowerModuleOptionsRow").Note);
        }

        [AvaloniaFact]
        public void PlantSegment_NoProductivityModule_HidesAddAndSaysSo() {
            Rig rig = NewRig();
            rig.Productivity.Enabled = false;
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Assert.False(Req<FormRow>(view, "TowerModuleOptionsRow").IsEffectivelyVisible);
            Assert.Equal("No enabled productivity module fits this tower.", Req<FormRow>(view, "TowerModulesEquippedRow").Note);
        }

        [AvaloniaFact]
        public void PlantSegment_AddModule_UsesTowerQuality() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Rare!));
            (_, FlowSegmentView view) = Open(rig, node);

            ChooseSlot(Req<KitPickGrid>(view, "TowerModuleOptionsGrid"), 0);
            Settle(rig.Host);

            Assert.Equal(new ModuleQualityPair(rig.Productivity, rig.Rare!), Assert.Single(node.TowerModules));
            Assert.Equal("Modules (1/4)", Req<InspectorGroup>(view, "TowerModulesGroup").Header);
        }

        [AvaloniaFact]
        public void PlantSegment_RemoveEquippedModule_UpdatesHeader() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);
            Assert.Equal("Modules (2/4)", Req<InspectorGroup>(view, "TowerModulesGroup").Header);

            ClearSlot(Req<KitPickGrid>(view, "EquippedTowerGrid"), 0);
            Settle(rig.Host);

            Assert.Single(node.TowerModules);
            Assert.Equal("Modules (1/4)", Req<InspectorGroup>(view, "TowerModulesGroup").Header);
        }

        [AvaloniaFact]
        public void PlantSegment_ClickingAnOptionWithAFilledSlotSelected_ReplacesThatSlot() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Req<KitPickGrid>(view, "EquippedTowerGrid").SelectedIndex = 1;
            ChooseSlot(Req<KitPickGrid>(view, "TowerModuleOptionsGrid"), 0);
            Settle(rig.Host);

            Assert.Equal([rig.Speed, rig.Productivity], node.TowerModules.Select(m => m.Module));
        }

        [AvaloniaFact]
        public void PlantSegment_PickNone_ClearsTowerAndModules() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            ChooseSlot(Req<KitPickGrid>(view, "TowerChoiceGrid"), 0);
            Settle(rig.Host);

            Assert.Null(node.Tower);
            Assert.Empty(node.TowerModules);
            Assert.False(Req<InspectorGroup>(view, "TowerModulesGroup").IsEffectivelyVisible);
            Assert.Equal(0, Req<KitPickGrid>(view, "TowerChoiceGrid").SelectedIndex);
        }

        [AvaloniaFact]
        public void PlantSegment_TowerQuality_DoesNotChangeGraphDefault() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            PopUp popUp = Req<PopUp>(view, "TowerQualityPopUp");
            Assert.True(Req<FormRow>(view, "TowerQualityRow").IsEffectivelyVisible);
            popUp.Command!.Execute("Rare");
            Settle(rig.Host);

            Assert.Equal(rig.Rare, node.Tower!.Value.Quality);
            Assert.Equal(rig.Normal, rig.Graph.DefaultAssemblerQuality);
            Assert.Equal(rig.Normal, node.TowerModules[0].Quality);
        }

        [AvaloniaFact]
        public void PlantSegment_TowerQualityRow_HiddenWithOneQuality() {
            Rig rig = NewRig(rareQuality: false);
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Assert.False(Req<FormRow>(view, "TowerQualityRow").IsEffectivelyVisible);
            Assert.True(Req<InspectorGroup>(view, "TowerModulesGroup").IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void PlantSegment_MissingTower_ShowsOrphanEvenWithoutOptions() {
            Rig rig = NewRig(slottedTower: false);
            var missing = new AssemblerPrototype(rig.Cache, "gone-tower", "Gone tower", EntityType.AgriculturalTower, EnergySource.Electric, isMissing: true) { ModuleSlots = 4 };
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(missing, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Assert.True(Req<InspectorGroup>(view, "TowerGroup").IsEffectivelyVisible);
            KitPickGrid grid = Req<KitPickGrid>(view, "TowerChoiceGrid");
            Assert.True(grid.Slots[^1].IsMissing);
            Assert.Equal(grid.Slots.Count - 1, grid.SelectedIndex);
            FormRow row = Req<FormRow>(view, "TowerRow");
            Assert.True(row.NoteIsWarning);
            Assert.Equal("Gone tower", row.Note);
            Assert.False(Req<FormRow>(view, "TowerQualityRow").IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void PlantSegment_ModulesOverSlots_HeaderAndUnavailableCells() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            for (int i = 0; i < 5; i++)
                controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Assert.Equal("Modules (5/4)", Req<InspectorGroup>(view, "TowerModulesGroup").Header);
            KitPickGrid equipped = Req<KitPickGrid>(view, "EquippedTowerGrid");
            Assert.Equal(5, equipped.Slots.Count);
            Assert.False(equipped.Slots[3].IsMissing);
            Assert.True(equipped.Slots[4].IsMissing);
        }

        [AvaloniaFact]
        public void PlantSegment_Accessibility() {
            Rig rig = NewRig();
            (IPlantNodeViewModel node, PlantNodeController controller) = NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            (_, FlowSegmentView view) = Open(rig, node);

            Assert.Equal("Tower", Req<KitPickGrid>(view, "TowerChoiceGrid").GroupLabel);
            Assert.Equal("No tower", Req<KitPickGrid>(view, "TowerChoiceGrid").Slots[0].Name);
            PopUp quality = Req<PopUp>(view, "TowerQualityPopUp");
            Assert.Equal("Tower quality", AutomationProperties.GetName(quality));
            Assert.Equal("New modules use this quality.", AutomationProperties.GetHelpText(quality));
            Assert.Equal("Tower module slots", Req<KitPickGrid>(view, "EquippedTowerGrid").GroupLabel);
            Assert.Equal("Available tower modules", Req<KitPickGrid>(view, "TowerModuleOptionsGrid").GroupLabel);
        }

        [AvaloniaFact]
        public void Identity_PlantWithTower_SubtitleNamesTower() {
            Rig rig = NewRig();
            var window = new MainWindow();
            window.Show();
            window.DataCache = rig.Cache;
            window.GraphCanvas.Viewer.Context.DCache = rig.Cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = rig.Normal;
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreatePlantNode(rig.Process, rig.Normal, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node);
            var controller = (PlantNodeController)window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, node!);
            string Subtitle() => window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text!;

            Assert.Equal("Plant node", Subtitle());

            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Plant node · Test tower", Subtitle());

            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Rare!));
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Plant node · Test tower (Rare)", Subtitle());
        }
    }
}
