using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas.Panels;
using SkiaSharp;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Live bug (found driving the real app): opening "Add Item"/"Add Recipe" turned the whole window gray,
    //toolbar included, with no crash and no logged exception. Root cause, confirmed by bisecting the real
    //app down to a single populated IconButton: it painted itself through its own ICustomDrawOperation (a
    //raw ISkiaSharpApiLeaseFeature GPU lease), and on macOS's Metal-backed Avalonia.Skia renderer any such
    //lease taken from a nested composited control - as opposed to GraphCanvasControl's own single top-level
    //one - corrupts the whole window's next composited frame the moment it draws a bitmap. Consolidating
    //every button's paint into one shared lease (tried first) still broke it live: still a second, nested
    //lease. The GPU corruption itself only reproduces live and can't be exercised under Avalonia's headless
    //renderer, so these tests instead pin the actual fix: IconButton no longer takes a GPU lease of its own
    //at all - it bakes its pixels into a real Avalonia Bitmap and draws that through DrawingContext, the same
    //ordinary image-compositing route every other bitmap in an Avalonia app already renders through.
    public class IconButtonLiveRenderTests {
        [Fact]
        public void IconButton_NoLongerOwnsAPerInstanceCustomDrawOperation() {
            Assert.DoesNotContain(typeof(IconButton).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public),
                t => typeof(ICustomDrawOperation).IsAssignableFrom(t));
        }

        //Bake wraps the exact PaintOnto call RenderOffscreen already trusts, so this proves it actually runs
        //(no lease, no exception) for a populated cell - the real DrawBitmap path that broke live - and an
        //empty one. That's the part a headless run can verify: Avalonia's headless platform has no real
        //bitmap codec (PixelSize and Bitmap.Save both come back as stubs under it, for any Bitmap
        //constructor - confirmed true of the raw-pixel constructor too, not just the PNG-decode one), so
        //pixel/size fidelity for the resulting Avalonia Bitmap rests on RenderOffscreen's own PNG output
        //(PaintOnto's pixels, proven correct there) plus round 2's live screenshots (Bake's real DrawImage
        //output, live).
        [AvaloniaFact]
        public void Bake_RunsPaintOntoWithoutThrowing_ForPopulatedAndEmptyCells() {
            var cache = new DataCache(filterRecipes: true);
            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            var item = new ItemPrototype(cache, "widget", "Widget", subgroup, "a") { Available = true };

            using var redBitmap = new SKBitmap(4, 4);
            using (var iconCanvas = new SKCanvas(redBitmap))
                iconCanvas.Clear(new SKColor(200, 40, 40, 255));
            item.SetIconAndColor(new IconColorPair(redBitmap, System.Drawing.Color.Red));

            var populated = new IconButton();
            populated.SetPopulated(item, Avalonia.Media.Color.FromRgb(60, 120, 60));
            using (Bitmap baked = populated.Bake(16, 12))
                Assert.NotNull(baked);

            var empty = new IconButton();
            empty.SetEmpty();
            using (Bitmap baked = empty.Bake(10, 10))
                Assert.NotNull(baked);
        }
    }
}
