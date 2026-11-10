using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.UiTests.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    public sealed class PlantNodeTowerTests {
        private static PlantSegmentTowerTests.Rig NewRig(string seedName = "seed") => PlantSegmentTowerTests.NewRig(seedName: seedName);

        private static PlantNodeElement Card(PlantSegmentTowerTests.Rig rig, PlantNodeController controller, IPlantNodeViewModel node) {
            rig.Graph.UpdateNodeValues();
            var element = new PlantNodeElement(rig.Control.Viewer.Context, node);
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        private static (PlantNodeElement Element, PlantNodeController Controller, PlantSegmentTowerTests.Rig Rig) Plant(int productivityModules, bool tower = true, string seedName = "seed") {
            PlantSegmentTowerTests.Rig rig = NewRig(seedName);
            (IPlantNodeViewModel node, PlantNodeController controller) = PlantSegmentTowerTests.NewPlantNode(rig);
            if (tower)
                controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            for (int i = 0; i < productivityModules; i++)
                controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            return (Card(rig, controller, node), controller, rig);
        }

        private static NodeText Detail(PlantNodeElement element) => element.Texts.Single(t => t.Role == NodeTextRole.Detail);

        private static float TextRight(NodeText text) => text.Rect.Left + NodeCardPainter.Measure(text.Text, text.Size, text.Weight);

        [AvaloniaFact]
        public void PlantCard_NoTower_Unchanged() {
            (PlantNodeElement element, _, _) = Plant(0, tower: false);

            Assert.Equal(96, element.Height);
            Assert.Equal(144, element.Width);
            Assert.StartsWith("Plant · ×", Detail(element).Text);
            Assert.True(element.TowerElement.IconRect.IsEmpty);
            Assert.DoesNotContain(element.Texts, t => t.Role == NodeTextRole.ExtraProductivity);
        }

        [AvaloniaFact]
        public void PlantCard_TowerWithProductivity_IconAndBonus() {
            (PlantNodeElement element, _, _) = Plant(4);
            CanvasTokens tokens = TokenBridge.Current;
            SKRect icon = element.TowerElement.IconRect;
            NodeText bonus = element.Texts.Single(t => t.Role == NodeTextRole.ExtraProductivity);

            Assert.Equal(96, element.Height);
            Assert.Equal(16, icon.Width);
            Assert.Equal(16, icon.Height);
            Assert.Equal(element.Bounds.Top + 52, icon.Top, 0.01);
            Assert.Equal(TextRight(Detail(element)) + 8, icon.Left, 0.01);
            Assert.Equal("+40%", bonus.Text);
            Assert.Equal(icon.Right + 4, bonus.Rect.Left, 0.01);
            Assert.Equal(tokens.CanvasEmphasizedWeight, bonus.Weight);
            Assert.Equal(tokens.TextSecondary, bonus.Color);
        }

        [AvaloniaFact]
        public void PlantCard_TowerWithoutProductivity_IconOnly() {
            (PlantNodeElement element, _, _) = Plant(0);

            Assert.Equal(16, element.TowerElement.IconRect.Width);
            Assert.DoesNotContain(element.Texts, t => t.Role == NodeTextRole.ExtraProductivity);
        }

        [AvaloniaFact]
        public void PlantCard_LowDetail_HidesTowerSegment() {
            PlantSegmentTowerTests.Rig rig = NewRig();
            rig.Control.Viewer.Context.LevelOfDetail = LevelOfDetail.Low;
            (IPlantNodeViewModel node, PlantNodeController controller) = PlantSegmentTowerTests.NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            PlantNodeElement element = Card(rig, controller, node);

            Assert.True(element.TowerElement.IconRect.IsEmpty);
            Assert.False(element.TowerElement.Visible);
            Assert.DoesNotContain(element.Texts, t => t.Role == NodeTextRole.ExtraProductivity);
            Assert.Equal(144, element.Width);
        }

        [AvaloniaFact]
        public void PlantCard_LongNameWithBadge_NoOverlap() {
            (PlantNodeElement element, PlantNodeController controller, PlantSegmentTowerTests.Rig rig) = Plant(0, seedName: "Extraordinarily long seed name for an overlap check");
            for (int i = 0; i < 5; i++)
                controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            rig.Graph.UpdateNodeValues();
            element.RequestStateUpdate();
            element.PrePaint();
            NodeText detail = Detail(element);

            SKRect badge = element.BadgeRect();
            Assert.False(badge.IsEmpty);
            Assert.StartsWith("Plant · ×", detail.Text);
            Assert.DoesNotContain('…', detail.Text);
            float segmentRight = element.Texts.Where(t => t.Role == NodeTextRole.ExtraProductivity).Select(t => t.Rect.Right).Append(element.TowerElement.IconRect.Right).Max();
            Assert.True(segmentRight <= badge.Left - 6 + 0.01f, $"segment right {segmentRight}, badge left {badge.Left}");
        }

        [AvaloniaFact]
        public void PlantCard_MissingTower_IconDimmedNoBonus() {
            PlantSegmentTowerTests.Rig rig = NewRig();
            var missing = new AssemblerPrototype(rig.Cache, "gone-tower", "Gone tower", EntityType.AgriculturalTower, EnergySource.Electric, isMissing: true) { ModuleSlots = 4 };
            (IPlantNodeViewModel node, PlantNodeController controller) = PlantSegmentTowerTests.NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(missing, rig.Normal));
            PlantNodeElement element = Card(rig, controller, node);

            Assert.False(element.TowerElement.IconRect.IsEmpty);
            Assert.Equal(0.62f, element.TowerElement.IconOpacity, 0.001);
            Assert.DoesNotContain(element.Texts, t => t.Role == NodeTextRole.ExtraProductivity);
        }

        [AvaloniaFact]
        public void PlantCard_TowerTooltip_ListsModules() {
            (PlantNodeElement element, _, _) = Plant(4);
            SKRect icon = element.TowerElement.IconRect;
            var point = new DrawingPoint((int)icon.MidX, (int)icon.MidY);

            TooltipInfo tip = Assert.Single(element.GetToolTips(point));

            Assert.Equal("Test tower\nProductivity +40%\nTower Modules:\n   4 :Productivity module 3", tip.Text);
        }

        [AvaloniaFact]
        public void PlantCard_TowerTooltip_OmitsEmptyLines() {
            (PlantNodeElement element, _, _) = Plant(0);
            SKRect icon = element.TowerElement.IconRect;

            TooltipInfo tip = Assert.Single(element.GetToolTips(new DrawingPoint((int)icon.MidX, (int)icon.MidY)));

            Assert.Equal("Test tower", tip.Text);
        }

        [AvaloniaFact]
        public void PlantCard_AutomationName_IncludesTower() {
            (PlantNodeElement element, _, _) = Plant(4);

            Assert.Contains("plant, Test tower, productivity +40%", element.AutomationName);
        }

        [AvaloniaFact]
        public void PlantCard_AutomationName_WithoutProductivityNamesOnlyTheTower() {
            (PlantNodeElement element, _, _) = Plant(0);

            Assert.Contains("plant, Test tower,", element.AutomationName);
            Assert.DoesNotContain("productivity", element.AutomationName);
        }

        [AvaloniaFact]
        public void Notice_TowerModulesOverSlots_OffersFixTowerModules() {
            (PlantNodeElement element, PlantNodeController controller, PlantSegmentTowerTests.Rig rig) = Plant(0);
            for (int i = 0; i < 5; i++)
                controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Normal));
            rig.Graph.UpdateNodeValues();
            element.RequestStateUpdate();
            element.PrePaint();

            ErrorNoticeElement notice = element.SubElements.OfType<ErrorNoticeElement>().Single();

            Assert.Contains(notice.BuildRightClickMenu(), e => e.Caption == "Fix tower modules");
        }

        [AvaloniaFact]
        public void Notice_MissingTower_OffersRemoveTower() {
            PlantSegmentTowerTests.Rig rig = NewRig();
            var missing = new AssemblerPrototype(rig.Cache, "gone-tower", "Gone tower", EntityType.AgriculturalTower, EnergySource.Electric, isMissing: true) { ModuleSlots = 4 };
            (IPlantNodeViewModel node, PlantNodeController controller) = PlantSegmentTowerTests.NewPlantNode(rig);
            controller.SetTower(new AssemblerQualityPair(missing, rig.Normal));
            PlantNodeElement element = Card(rig, controller, node);

            ErrorNoticeElement notice = element.SubElements.OfType<ErrorNoticeElement>().Single();

            Assert.Contains(notice.BuildRightClickMenu(), e => e.Caption == "Remove tower");
        }
    }
}
