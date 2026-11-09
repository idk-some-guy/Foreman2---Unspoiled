using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.UiTests.support;
using SkiaSharp;
using System.Drawing;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class ImageExportTests {
        private const int Size = 900;
        private static readonly Color Orange = Color.FromArgb(255, 210, 90, 30);

        private static byte[] Paint(LinkFixture fx, bool export, bool draggedNodeActive = false) {
            using var bitmap = new SKBitmap(Size, Size);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Magenta);
            fx.Viewer.Viewport.SetSize(Size, Size);
            if (export)
                fx.Viewer.Paint(canvas, fullGraph: true, draggedNodeActive: draggedNodeActive, clearBackground: false, exportTransform: new GraphViewer.ExportTransform(Size, Size, 1f, new Point(0, 0)));
            else
                fx.Viewer.Paint(canvas, draggedNodeActive: draggedNodeActive, clearBackground: false);
            return bitmap.Bytes;
        }

        private static byte[] Render(LinkFixture fx) {
            using var bitmap = new SKBitmap(Size, Size);
            using var canvas = new SKCanvas(bitmap);
            fx.Viewer.Viewport.SetSize(Size, Size);
            fx.Control.Render(canvas);
            return bitmap.Bytes;
        }

        [AvaloniaFact]
        public void Export_DrawsNoTransientMark_WhileTheOnScreenPaintDrawsEachOne() {
            using LinkFixture fx = LinkFixture.Create();
            ItemPrototype item = fx.NewItem("iron-plate", Orange);
            LinkElement link = fx.AddLink(item, new Point(-150, 100), NodeDirection.Up, new Point(150, -100), NodeDirection.Up);
            fx.Viewer.SetWaypoints(link, [new Point(0, 0)]);
            var supplier = (BaseNodeElement)link.SupplierElement!;
            var consumer = (BaseNodeElement)link.ConsumerElement!;
            var annotation = new ShapeAnnotationElement(new Point(-60, 250));
            fx.Viewer.AddAnnotationElement(annotation);
            byte[] cleanExport = Paint(fx, export: true);
            byte[] cleanScreen = Paint(fx, export: false);

            ItemTabElement supplierTab = supplier.SubElements.OfType<ItemTabElement>().First();
            ItemTabElement consumerTab = consumer.SubElements.OfType<ItemTabElement>().First();
            var marks = new (string Name, System.Action Set, System.Action Clear)[] {
                ("node selection", () => fx.Viewer.SetSelection([supplier]), () => fx.Viewer.SetSelection([])),
                ("find ring", () => consumer.IsFindMatch = true, () => consumer.IsFindMatch = false),
                ("keyboard focus", () => consumer.ShowsKeyboardFocus = true, () => consumer.ShowsKeyboardFocus = false),
                ("placement flash", () => { supplier.IsPlacementFlashing = true; supplier.PlacementFlashOpacity = 1; }, () => supplier.IsPlacementFlashing = false),
                ("link hover", () => link.IsHovered = true, () => link.IsHovered = false),
                ("link selection", () => link.IsSelected = true, () => link.IsSelected = false),
                ("link focus", () => link.IsFocused = true, () => link.IsFocused = false),
                ("selected waypoint", () => { link.IsSelected = true; link.SelectedWaypointIndex = 0; }, () => { link.SelectedWaypointIndex = -1; link.IsSelected = false; }),
                ("hovered tab", () => fx.Viewer.Context.HoveredTab = supplierTab, () => fx.Viewer.Context.HoveredTab = null),
                ("armed tab", () => fx.Viewer.Context.ArmedTab = consumerTab, () => fx.Viewer.Context.ArmedTab = null),
                ("annotation selection", () => annotation.IsSelected = true, () => annotation.IsSelected = false),
                ("axis guide", () => { fx.Viewer.Grid.LockDragToAxis = true; fx.Viewer.Grid.DragOrigin = new Point(0, 0); }, () => fx.Viewer.Grid.LockDragToAxis = false),
                ("selection zone", () => fx.Viewer.SelectionZone = new Rectangle(-300, -300, 600, 600), () => fx.Viewer.SelectionZone = Rectangle.Empty),
            };

            foreach ((string name, System.Action set, System.Action clear) in marks) {
                bool dragging = name == "axis guide";
                set();
                Assert.False(cleanScreen.SequenceEqual(Paint(fx, export: false, dragging)), name + " draws on screen");
                Assert.True(cleanExport.SequenceEqual(Paint(fx, export: true, dragging)), name + " stays out of the export");
                clear();
                Assert.True(cleanScreen.SequenceEqual(Paint(fx, export: false, dragging)), name + " clears");
            }

            byte[] cleanRender = Render(fx);
            fx.Viewer.IsPaused = true;
            Assert.False(cleanRender.SequenceEqual(Render(fx)), "paused frame draws on screen");
            Assert.True(cleanExport.SequenceEqual(Paint(fx, export: true)), "paused frame stays out of the export");
            fx.Viewer.IsPaused = false;
            Assert.True(cleanRender.SequenceEqual(Render(fx)), "paused frame clears");
        }

        [AvaloniaFact]
        public void Export_OfASelectedUndersuppliedNode_KeepsTheErrorBorderAndDropsTheSelection() {
            using LinkFixture fx = LinkFixture.Create();
            ItemPrototype item = fx.NewItem("iron-plate", Orange);
            LinkElement link = fx.AddLink(item, new Point(-150, 100), NodeDirection.Up, new Point(150, -100), NodeDirection.Up);
            var consumer = (BaseNodeElement)link.ConsumerElement!;
            var controller = fx.Viewer.Session.Editor.RequestNodeController(consumer.ViewModel.Id)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(500);
            consumer.RequestStateUpdate();
            consumer.PrePaint();
            fx.Viewer.SetSelection([consumer]);
            CanvasTokens tokens = TokenBridge.Current;
            SKRect card = consumer.CardGeometry().Rect;

            using var bitmap = new SKBitmap(Size, Size);
            using (var canvas = new SKCanvas(bitmap)) {
                canvas.Clear(SKColors.Magenta);
                fx.Viewer.Paint(canvas, fullGraph: true, clearBackground: false, exportTransform: new GraphViewer.ExportTransform(Size, Size, 1f, new Point(0, 0)));
            }

            Assert.True(consumer.FlowUndersupplied);
            bool sawError = false, sawSelected = false;
            for (int y = (int)card.MidY - 5; y < (int)card.MidY + 5; y++)
                for (int x = (int)card.Left - 4; x < (int)card.Left + 4; x++) {
                    SKColor pixel = bitmap.GetPixel((Size / 2) + x, (Size / 2) + y);
                    sawError |= pixel == tokens.Error;
                    sawSelected |= pixel == tokens.NodeSelected;
                }
            Assert.True(sawError);
            Assert.False(sawSelected);
        }
    }
}
