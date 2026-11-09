using Avalonia.Headless;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.support {
    internal sealed class CanvasFixture : IDisposable {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly Lazy<DataCache> VanillaCache = new(LoadVanilla);

        public required DataCache Cache { get; init; }
        public required GraphCanvasControl Control { get; init; }
        public required AvaloniaWindow Window { get; init; }

        public GraphViewer Viewer => Control.Viewer;

        public static CanvasFixture WithVanilla() {
            DataCache cache = VanillaCache.Value;
            (GraphCanvasControl control, AvaloniaWindow window) = NewControl(cache);
            return new CanvasFixture { Cache = cache, Control = control, Window = window };
        }

        public static CanvasFixture WithVanillaAndOneNode(out AvaloniaPoint nodeScreenPoint) {
            CanvasFixture fx = WithVanilla();
            var pair = new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!);
            NodeId nodeId = fx.Control.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = fx.Control.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            nodeScreenPoint = fx.Control.Viewport.GraphToScreen(new DrawingPoint(0, 0));
            return fx;
        }

        public static CanvasFixture WithVanillaAndOneSupplier(out AvaloniaPoint outputTabScreenPoint) =>
            WithVanillaAndOneSupplierOf("iron-plate", out outputTabScreenPoint);

        public static CanvasFixture WithVanillaAndOneSupplierOf(string itemName, out AvaloniaPoint outputTabScreenPoint) {
            CanvasFixture fx = WithVanilla();
            var pair = new ItemQualityPair(fx.Cache.Items[itemName], fx.Cache.DefaultQuality!);
            NodeId nodeId = fx.Control.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = fx.Control.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            ItemTabElement tab = element.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == LinkType.Output);
            outputTabScreenPoint = fx.Control.Viewport.GraphToScreen(element.LocalToGraph(tab.Location));
            return fx;
        }

        public static CanvasFixture WithVanillaAndOneConsumerOf(string itemName, out AvaloniaPoint inputTabScreenPoint) {
            CanvasFixture fx = WithVanilla();
            var pair = new ItemQualityPair(fx.Cache.Items[itemName], fx.Cache.DefaultQuality!);
            NodeId nodeId = fx.Control.Viewer.Session.Editor.CreateConsumerNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = fx.Control.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            ItemTabElement tab = element.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == LinkType.Input);
            inputTabScreenPoint = fx.Control.Viewport.GraphToScreen(element.LocalToGraph(tab.Location));
            return fx;
        }

        public static CanvasFixture WithCacheAndOneSupplierOf(DataCache cache, ItemQualityPair pair, out AvaloniaPoint outputTabScreenPoint) {
            (GraphCanvasControl control, AvaloniaWindow window) = NewControl(cache);
            var fx = new CanvasFixture { Cache = cache, Control = control, Window = window };
            NodeId nodeId = fx.Control.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = fx.Control.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            ItemTabElement tab = element.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == LinkType.Output);
            outputTabScreenPoint = fx.Control.Viewport.GraphToScreen(element.LocalToGraph(tab.Location));
            return fx;
        }

        public static CanvasFixture WithCacheAndOneConsumerOf(DataCache cache, ItemQualityPair pair, out AvaloniaPoint inputTabScreenPoint) {
            (GraphCanvasControl control, AvaloniaWindow window) = NewControl(cache);
            var fx = new CanvasFixture { Cache = cache, Control = control, Window = window };
            NodeId nodeId = fx.Control.Viewer.Session.Editor.CreateConsumerNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = fx.Control.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            ItemTabElement tab = element.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == LinkType.Input);
            inputTabScreenPoint = fx.Control.Viewport.GraphToScreen(element.LocalToGraph(tab.Location));
            return fx;
        }

        //two small moves are needed to cross the drag threshold
        public LinkDrag DragFrom(AvaloniaPoint tabScreenPoint) {
            AvaloniaPoint armed = tabScreenPoint + new AvaloniaPoint(40, 0);
            Window.MouseDown(tabScreenPoint, MouseButton.Left, RawInputModifiers.None);
            Window.MouseMove(armed, RawInputModifiers.LeftMouseButton);
            Window.MouseMove(armed, RawInputModifiers.LeftMouseButton);
            return new LinkDrag(this, armed);
        }

        public void MoveTo(AvaloniaPoint screenPoint) => Window.MouseMove(screenPoint, RawInputModifiers.None);

        public void ClickAt(AvaloniaPoint screenPoint) {
            Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        //The radial arms on pointer direction, so a click on it is preceded by the move a real pointer makes.
        public void MoveAndClickAt(AvaloniaPoint screenPoint) {
            MoveTo(screenPoint);
            ClickAt(screenPoint);
        }

        public void ReleaseAt(AvaloniaPoint screenPoint) => Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);

        public void DoubleClick(AvaloniaPoint screenPoint) {
            Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
            Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        public void RightClick(AvaloniaPoint screenPoint) {
            Window.MouseDown(screenPoint, MouseButton.Right, RawInputModifiers.None);
            Window.MouseUp(screenPoint, MouseButton.Right, RawInputModifiers.None);
        }

        public void Dispose() => Window.Close();

        private static (GraphCanvasControl, AvaloniaWindow) NewControl(DataCache cache) {
            ScaleGuard.AssertDefault();
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            return (control, window);
        }

        private static DataCache LoadVanilla() {
            var cache = new DataCache(filterRecipes: true);
            cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>()).GetAwaiter().GetResult();
            return cache;
        }
    }

    internal sealed class LinkDrag {
        private readonly CanvasFixture fx;
        private AvaloniaPoint current;

        internal LinkDrag(CanvasFixture fx, AvaloniaPoint start) {
            this.fx = fx;
            current = start;
        }

        public LinkDrag To(AvaloniaPoint screenPoint) {
            fx.Window.MouseMove(screenPoint, RawInputModifiers.LeftMouseButton);
            current = screenPoint;
            return this;
        }

        public void Release(KeyModifiers modifiers = KeyModifiers.None) => fx.Window.MouseUp(current, MouseButton.Left, ToRaw(modifiers));

        private static RawInputModifiers ToRaw(KeyModifiers modifiers) {
            RawInputModifiers raw = RawInputModifiers.None;
            if (modifiers.HasFlag(KeyModifiers.Meta)) raw |= RawInputModifiers.Meta;
            if (modifiers.HasFlag(KeyModifiers.Control)) raw |= RawInputModifiers.Control;
            if (modifiers.HasFlag(KeyModifiers.Shift)) raw |= RawInputModifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Alt)) raw |= RawInputModifiers.Alt;
            return raw;
        }
    }
}
