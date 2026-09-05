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
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.support {
    //Shared real-vanilla-preset canvas fixture for tests driving a GraphCanvasControl end to end (real
    //pointer events, real DataCache) - the shape ItemRecipeChooserFlowTests.Fixture used before Task 7
    //retires that file, plus the double/right-click helpers QuickAddCanvasTests needs.
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
}
