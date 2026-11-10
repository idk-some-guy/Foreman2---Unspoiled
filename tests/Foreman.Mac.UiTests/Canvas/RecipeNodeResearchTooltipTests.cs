using Avalonia.Headless.XUnit;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Models.Research;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class RecipeNodeResearchTooltipTests {
        private static (CanvasFixture Fx, RecipeNodeElement Element) Open() {
            CanvasFixture fx = CanvasFixture.WithVanilla();
            var pair = new RecipeQualityPair(fx.Cache.Recipes["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(pair, new Point(0, 0));
            var element = (RecipeNodeElement)fx.Viewer.NodeElementDictionary[id];
            element.RequestStateUpdate();
            element.PrePaint();
            return (fx, element);
        }

        private static ResearchState Bonus(double bonus) =>
            new(null, new Dictionary<ResearchField, double>(), new Dictionary<ResearchField, double>(),
                new Dictionary<string, double> { ["iron-gear-wheel"] = bonus }, new Dictionary<string, double>(), []);

        private static string HoverText(RecipeNodeElement element) {
            element.RequestStateUpdate();
            element.PrePaint();
            return element.GetToolTips(element.Location).Single(t => t.Text?.Contains("Left click") == true).Text!;
        }

        [AvaloniaFact]
        public void RecipeTooltip_ResearchLine() {
            (CanvasFixture fx, RecipeNodeElement element) = Open();
            using (fx) {
                Assert.Null(element.ResearchTooltipLine);
                Assert.DoesNotContain("Research Productivity", HoverText(element));

                fx.Viewer.Graph.Research = Bonus(0.3);
                Assert.Equal("Research Productivity: +30%", element.ResearchTooltipLine);
                Assert.EndsWith("\nResearch Productivity: +30%", HoverText(element));

                fx.Viewer.Graph.Research = Bonus(0.125);
                Assert.Equal("Research Productivity: +12.5%", element.ResearchTooltipLine);

                fx.Viewer.Graph.Research = ResearchState.None;
                Assert.Null(element.ResearchTooltipLine);
                Assert.DoesNotContain("Research Productivity", HoverText(element));
            }
        }
    }
}
