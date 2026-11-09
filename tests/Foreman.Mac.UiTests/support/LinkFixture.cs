using Avalonia.Headless;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.support {
    internal sealed class LinkFixture : IDisposable {
        internal const int Half = 450;

        public required DataCache Cache { get; init; }
        public required SubgroupPrototype Subgroup { get; init; }
        public required QualityPrototype Quality { get; init; }
        public required GraphCanvasControl Control { get; init; }
        public AvaloniaWindow? Window { get; init; }

        public GraphViewer Viewer => Control.Viewer;

        public static LinkFixture Create(bool atDefaultScale = true) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();
            return Over(control, window, atDefaultScale);
        }

        public static LinkFixture Over(GraphCanvasControl control, AvaloniaWindow? window = null, bool atDefaultScale = true) {
            if (atDefaultScale)
                ScaleGuard.AssertDefault();
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = quality;
            return new LinkFixture { Cache = cache, Subgroup = subgroup, Quality = quality, Control = control, Window = window };
        }

        public ItemPrototype NewItem(string name, Color? color = null, string? friendlyName = null) {
            var item = new ItemPrototype(Cache, name, friendlyName ?? name, Subgroup, "z", isMissing: false);
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((DataCacheStore)field.GetValue(Cache)!).Items[name] = item;
            if (color is Color value)
                item.SetIconAndColor(new IconColorPair(null, value));
            return item;
        }

        public ItemQualityPair Pair(IItem item) => new(item, Quality);

        public BaseNodeElement Supplier(ItemPrototype item, Point location, NodeDirection direction = NodeDirection.Up) =>
            Place(Viewer.Session.Editor.CreateSupplierNode(Pair(item), location), direction);

        public BaseNodeElement Consumer(ItemPrototype item, Point location, NodeDirection direction = NodeDirection.Up) =>
            Place(Viewer.Session.Editor.CreateConsumerNode(Pair(item), location), direction);

        private BaseNodeElement Place(NodeId id, NodeDirection direction) {
            Viewer.Session.Editor.SetDirection(id, direction);
            BaseNodeElement element = Viewer.NodeElementDictionary[id];
            element.PrePaint();
            return element;
        }

        public LinkElement Link(BaseNodeElement supplier, BaseNodeElement consumer, ItemPrototype item, LinkRouting? routing = null) {
            LinkId id = Viewer.Session.Editor.CreateLink(supplier.ViewModel.Id, consumer.ViewModel.Id, Pair(item), routing);
            supplier.PrePaint();
            consumer.PrePaint();
            return Viewer.LinkElementDictionary[id];
        }

        public LinkElement AddLink(ItemPrototype item, Point supplierAt, NodeDirection supplierDirection, Point consumerAt, NodeDirection consumerDirection, LinkRouting? routing = null) =>
            Link(Supplier(item, supplierAt, supplierDirection), Consumer(item, consumerAt, consumerDirection), item, routing);

        public AvaloniaPoint ToScreen(Point graphPoint) => Control.Viewport.GraphToScreen(graphPoint);

        public static SKSurface RenderLink(LinkElement link) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(Half, Half);
            link.UpdateVisibility(new Rectangle(-Half, -Half, 2 * Half, 2 * Half));
            link.PrePaint();
            link.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            return surface;
        }

        public static SKColor Sample(SKSurface surface, int graphX, int graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + graphX, Half + graphY);
        }

        public void ClickAt(AvaloniaPoint screenPoint, RawInputModifiers modifiers = RawInputModifiers.None) {
            Window!.MouseDown(screenPoint, MouseButton.Left, modifiers);
            Window!.MouseUp(screenPoint, MouseButton.Left, modifiers);
        }

        public void DoubleClick(AvaloniaPoint screenPoint) {
            ClickAt(screenPoint);
            Window!.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            Window!.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        public void Dispose() => Window?.Close();
    }
}
