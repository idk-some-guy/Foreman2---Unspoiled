using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Drawing;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public sealed class CanvasTypeFloorTests : IDisposable {
        public void Dispose() => WindowScaler.SetScale(1.0);

        private static RecipeNodeElement Recipe(CanvasFixture fx, string name, DrawingPoint at) {
            var pair = new RecipeQualityPair(fx.Cache.Recipes[name], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(pair, at);
            RecipeNodeElement node = (RecipeNodeElement)fx.Viewer.NodeElementDictionary[id];
            node.RequestStateUpdate();
            node.PrePaint();
            return node;
        }

        private static SupplierNodeElement Supplier(CanvasFixture fx, string item, DrawingPoint at) {
            NodeId id = fx.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(fx.Cache.Items[item], fx.Cache.DefaultQuality!), at);
            SupplierNodeElement node = (SupplierNodeElement)fx.Viewer.NodeElementDictionary[id];
            node.RequestStateUpdate();
            node.PrePaint();
            return node;
        }

        private static SupplierNodeElement ErrorSupplier(CanvasFixture fx, DrawingPoint at) {
            var ghost = new ItemPrototype(fx.Cache, "ghost-item", "ghost-item", new SubgroupPrototype(fx.Cache, "§§test:subgroup", "z"), "z", true);
            NodeId id = fx.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ghost, fx.Cache.DefaultQuality!), at);
            SupplierNodeElement node = (SupplierNodeElement)fx.Viewer.NodeElementDictionary[id];
            node.RequestStateUpdate();
            node.PrePaint();
            return node;
        }

        private static void Show(CanvasFixture fx, double step, double zoom, params BaseNodeElement[] nodes) {
            WindowScaler.SetScale(step);
            fx.Viewer.Viewport.ViewScale = (float)zoom;
            foreach (BaseNodeElement node in nodes)
                node.PrePaint();
        }

        private static IEnumerable<ItemTabElement> Tabs(BaseNodeElement node) => node.SubElements.OfType<ItemTabElement>();

        private static NodeText Name(BaseNodeElement node) => node.Texts.Single(text => text.Role == NodeTextRole.Name);

        [AvaloniaFact]
        public void Step1_Zoom0Point69_DropsTabRate_KeepsName_0Point58_DropsName_0Point71_DrawsBoth() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = Recipe(fx, "iron-gear-wheel", DrawingPoint.Empty);

            Show(fx, 1.0, 0.69, recipe);
            Assert.All(Tabs(recipe), tab => Assert.False(tab.DrawsRate));
            Assert.Contains(recipe.DrawnTexts, text => text.Role == NodeTextRole.Name);
            Assert.DoesNotContain(recipe.DrawnTexts, text => text.Role is NodeTextRole.Detail or NodeTextRole.Chip or NodeTextRole.TabRate);
            Assert.Contains(recipe.DrawnTexts, text => text.Role == NodeTextRole.Count);

            Show(fx, 1.0, 0.58, recipe);
            Assert.DoesNotContain(recipe.DrawnTexts, text => text.Role == NodeTextRole.Name);
            Assert.Contains(recipe.DrawnTexts, text => text.Role == NodeTextRole.Count);

            Show(fx, 1.0, 0.70, recipe);
            Assert.All(Tabs(recipe), tab => Assert.True(tab.DrawsRate));
            Assert.Contains(recipe.DrawnTexts, text => text.Role == NodeTextRole.Detail);

            Show(fx, 1.0, 0.71, recipe);
            Assert.Contains(recipe.DrawnTexts, text => text.Role == NodeTextRole.Name);
            Assert.All(Tabs(recipe), tab => Assert.True(tab.DrawsRate));

            Show(fx, 1.0, 0.35, recipe);
            Assert.DoesNotContain(recipe.DrawnTexts, text => text.Role is NodeTextRole.Name or NodeTextRole.Count);

            Show(fx, 1.0, 1, recipe);
            Rectangle bounds = recipe.Bounds;
            int[] tabWidths = [.. Tabs(recipe).Select(tab => tab.Width)];
            Point[] anchors = [.. Tabs(recipe).Select(tab => tab.GetConnectionPoint())];
            Show(fx, 1.0, 0.5, recipe);
            Assert.Equal(bounds, recipe.Bounds);
            Assert.Equal(tabWidths, Tabs(recipe).Select(tab => tab.Width));
            Assert.Equal(anchors, Tabs(recipe).Select(tab => tab.GetConnectionPoint()));

            fx.Viewer.Viewport.ViewScale = 0.3f;
            using (SKBitmap export = ExportAtOne(fx)) {
                Assert.All(Tabs(recipe), tab => Assert.True(tab.DrawsRate));
                Assert.Equal(bounds, recipe.Bounds);
                Assert.Equal(tabWidths, Tabs(recipe).Select(tab => tab.Width));
                Assert.True(Differs(export, recipe.TitleTextRect(), recipe.CardGeometry().Fill), "export lost the name");
                foreach (ItemTabElement tab in Tabs(recipe)) {
                    Point centre = recipe.LocalToGraph(tab.Location);
                    SKRect rate = tab.RateText.Rect;
                    rate.Offset(centre.X, centre.Y);
                    Assert.True(Differs(export, rate, tab.TabGeometry().Fill), "export lost a tab rate");
                }
            }
            Show(fx, 1.0, 0.3, recipe);
            Assert.All(Tabs(recipe), tab => Assert.False(tab.DrawsRate));
            Assert.DoesNotContain(recipe.DrawnTexts, text => text.Role == NodeTextRole.Name);
            Assert.Equal(bounds, recipe.Bounds);

            using (var failing = new SKBitmap(2 * Half, 2 * Half))
            using (var canvas = new SKCanvas(failing)) {
                Assert.Throws<InvalidOperationException>(() => fx.Viewer.Paint(canvas, fullGraph: true, draggedLink: new ThrowingElement(), exportTransform: new GraphViewer.ExportTransform(2 * Half, 2 * Half, 1f, new Point(0, 0))));
            }
            Assert.Null(fx.Viewer.Context.ExportViewScale);
            recipe.PrePaint();
            Assert.All(Tabs(recipe), tab => Assert.False(tab.DrawsRate));

            SupplierNodeElement error = ErrorSupplier(fx, DrawingPoint.Empty);
            Show(fx, 1.0, 1, error);
            SKRect word = error.BadgeRect();
            int footprint = error.Bounds.Width;
            Assert.True(word.Width > 16);
            Assert.True(error.BadgeDrawsText);

            Show(fx, 1.0, 0.69, error);
            SKRect circle = error.BadgeRect();
            Assert.Equal((16f, 16f), (circle.Width, circle.Height));
            Assert.False(error.BadgeDrawsText);
            float pad = TokenBridge.Current.Space05;
            Assert.Equal((SKRect?)SKRect.Inflate(circle, pad, pad), error.BadgeHitRect());
            Assert.Equal(footprint, error.Bounds.Width);
            using (SKBitmap bitmap = Draw(error, NodeDrawingStyle.Regular)) {
                Assert.True(Differs(bitmap, SKRect.Create(circle.MidX - 4, circle.MidY - 4, 8, 8), TokenBridge.Current.Error), "glyph missing at the circle centre");
                Assert.False(Differs(bitmap, new SKRect(circle.Left - (word.Width - 16), circle.Top, circle.Left, circle.Bottom), error.CardGeometry().Fill), "text drawn where the word used to run");
            }

            Show(fx, 1.0, 0.71, error);
            Assert.Equal(word, error.BadgeRect());
            Assert.True(error.BadgeDrawsText);
        }

        [AvaloniaFact]
        public void Step1Point5_TabCutoffIs0Point467() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = Recipe(fx, "iron-gear-wheel", DrawingPoint.Empty);

            Show(fx, 1.5, 0.467, recipe);
            Assert.All(Tabs(recipe), tab => Assert.True(tab.DrawsRate));

            Show(fx, 1.5, 0.466, recipe);
            Assert.All(Tabs(recipe), tab => {
                Assert.False(tab.DrawsRate);
                Assert.Equal((33f, 33f), (tab.TabGeometry().Rect.Width, tab.TabGeometry().Rect.Height));
            });

            Show(fx, 1.0, 0.5, recipe);
            Assert.All(Tabs(recipe), tab => {
                Assert.False(tab.DrawsRate);
                Assert.Equal((22f, 22f), (tab.TabGeometry().Rect.Width, tab.TabGeometry().Rect.Height));
            });

            SupplierNodeElement error = ErrorSupplier(fx, new DrawingPoint(500, 0));
            Show(fx, 1.5, 0.467, error);
            Assert.True(error.BadgeRect().Width > 24);
            Show(fx, 1.5, 0.466, error);
            Assert.Equal((24f, 24f), (error.BadgeRect().Width, error.BadgeRect().Height));
        }

        [AvaloniaFact]
        public void Step2_TabStroke1Point5_CardRadius10_Border1() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = Recipe(fx, "iron-gear-wheel", DrawingPoint.Empty);

            Show(fx, 2.0, 1, recipe);

            Assert.All(Tabs(recipe), tab => Assert.Equal(1.5f, tab.TabGeometry().StrokeWidth));
            NodeCardGeometry card = recipe.CardGeometry();
            Assert.Equal(10f, card.Radius);
            Assert.Equal(1f, card.StrokeWidth);
            Assert.Equal(1f, recipe.BorderRing().StrokeWidth);
        }

        [AvaloniaFact]
        public void SimpleStyle_BelowZoom0Point2OrOver200Nodes_DrawsCardStripIconBorderOnly() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            GraphViewer viewer = fx.Viewer;

            Assert.Equal(NodeDrawingStyle.Simple, viewer.StyleFor(201, 1f, false));
            Assert.Equal(NodeDrawingStyle.Regular, viewer.StyleFor(200, 1f, false));
            Assert.Equal(NodeDrawingStyle.Simple, viewer.StyleFor(1, 0.19f, false));
            Assert.Equal(NodeDrawingStyle.Regular, viewer.StyleFor(1, 0.2f, false));

            SupplierNodeElement node = Supplier(fx, "iron-plate", DrawingPoint.Empty);
            Show(fx, 1.0, 1, node);
            SKColor card = node.CardGeometry().Fill;
            SKRect text = node.TitleTextRect();
            SKRect icon = node.TitleIconRect();

            using SKBitmap simple = Draw(node, NodeDrawingStyle.Simple);
            using SKBitmap regular = Draw(node, NodeDrawingStyle.Regular);

            SKRect strip = node.TypeStripRect();
            Assert.Equal(node.TypeStripColor, Pixel(simple, strip.MidX, strip.MidY));
            Assert.True(Differs(simple, icon, card), "icon missing in simple style");
            Assert.False(Differs(simple, text, card), "text drawn in simple style");
            Assert.True(Differs(regular, text, card));

            foreach (ItemTabElement tab in Tabs(node)) {
                TabGeometry geometry = tab.TabGeometry();
                SKRect outside = OutsideCard(geometry.Rect, node.CardGeometry().Rect);
                Assert.False(Has(simple, outside, geometry.Fill), "tab drawn in simple style");
                Assert.True(Has(regular, outside, geometry.Fill));
            }
        }

        private const int Half = 300;

        private sealed class ThrowingElement : GraphElement {
            public override void PrePaint() => throw new InvalidOperationException();
            protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) { }
        }

        private static SKRect OutsideCard(SKRect tab, SKRect card) =>
            tab.Top < card.Top ? new SKRect(tab.Left + 4, tab.Top + 1, tab.Right - 4, card.Top - 1) : new SKRect(tab.Left + 4, card.Bottom + 1, tab.Right - 4, tab.Bottom - 1);

        private static SKBitmap Draw(BaseNodeElement node, NodeDrawingStyle style) {
            var bitmap = new SKBitmap(2 * Half, 2 * Half);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Magenta);
            canvas.Translate(Half, Half);
            node.PrePaint();
            node.Paint(canvas, style);
            return bitmap;
        }

        private static SKBitmap ExportAtOne(CanvasFixture fx) {
            var bitmap = new SKBitmap(2 * Half, 2 * Half);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Magenta);
            fx.Viewer.Paint(canvas, fullGraph: true, clearBackground: false, exportTransform: new GraphViewer.ExportTransform(2 * Half, 2 * Half, 1f, new Point(0, 0)));
            return bitmap;
        }

        private static SKColor Pixel(SKBitmap bitmap, float x, float y) => bitmap.GetPixel(Half + (int)x, Half + (int)y);

        private static bool Has(SKBitmap bitmap, SKRect rect, SKColor color) {
            for (int y = (int)rect.Top; y < (int)rect.Bottom; y++)
                for (int x = (int)rect.Left; x < (int)rect.Right; x++)
                    if (bitmap.GetPixel(Half + x, Half + y) == color)
                        return true;
            return false;
        }

        private static bool Differs(SKBitmap bitmap, SKRect rect, SKColor background) {
            for (int y = (int)rect.Top; y < (int)rect.Bottom; y++)
                for (int x = (int)rect.Left; x < (int)rect.Right; x++)
                    if (bitmap.GetPixel(Half + x, Half + y) != background)
                        return true;
            return false;
        }
    }
}
