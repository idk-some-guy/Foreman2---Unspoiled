using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.UiTests.support {
    internal sealed class KeyboardRig : System.IDisposable {
        public required CanvasFixture Fixture { get; init; }
        public required BaseNodeElement First { get; init; }
        public required BaseNodeElement Second { get; init; }
        public required BaseNodeElement Third { get; init; }

        public GraphCanvasControl Canvas => Fixture.Control;

        //Reading order is (0,0), (200,0), (0,200).
        public static KeyboardRig ThreeSuppliers() {
            CanvasFixture fx = CanvasFixture.WithVanilla();
            return new KeyboardRig {
                Fixture = fx,
                First = AddSupplier(fx, "iron-plate", new Point(0, 0)),
                Second = AddSupplier(fx, "copper-plate", new Point(200, 0)),
                Third = AddSupplier(fx, "iron-ore", new Point(0, 200)),
            };
        }

        public static BaseNodeElement AddSupplier(CanvasFixture fx, string item, Point at) {
            var pair = new ItemQualityPair(fx.Cache.Items[item], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateSupplierNode(pair, at);
            return Prime(fx.Viewer, id);
        }

        public static BaseNodeElement AddConsumer(CanvasFixture fx, string item, Point at) {
            var pair = new ItemQualityPair(fx.Cache.Items[item], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateConsumerNode(pair, at);
            return Prime(fx.Viewer, id);
        }

        public static BaseNodeElement AddRecipe(CanvasFixture fx, string recipe, Point at) {
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(fx.Cache.Recipes[recipe], fx.Cache.DefaultQuality!), at);
            return Prime(fx.Viewer, id);
        }

        public static BaseNodeElement Prime(GraphViewer viewer, NodeId id) {
            BaseNodeElement element = viewer.NodeElementDictionary[id];
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        public bool Press(Key key, KeyModifiers modifiers = KeyModifiers.None) => Press(Canvas, key, modifiers);

        public static bool Press(GraphCanvasControl canvas, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            return Press(canvas, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        }

        //Whether the canvas itself consumed the key, before a parent such as the window's focus loop sees it.
        public static bool Press(GraphCanvasControl canvas, KeyEventArgs args) {
            bool consumed = false;
            void Observe(object? sender, KeyEventArgs e) => consumed = e.Handled;
            canvas.AddHandler(InputElement.KeyDownEvent, Observe, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            canvas.RaiseEvent(args);
            canvas.RemoveHandler(InputElement.KeyDownEvent, Observe);
            Dispatcher.UIThread.RunJobs();
            return consumed;
        }

        public System.Collections.Generic.IReadOnlyList<BaseNodeElement> Selected => [.. Canvas.Viewer.SelectedNodes.OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X)];

        public void Dispose() => Fixture.Dispose();
    }
}
