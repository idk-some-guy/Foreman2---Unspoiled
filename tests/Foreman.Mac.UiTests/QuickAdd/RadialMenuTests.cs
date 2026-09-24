using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Task 4, then gate fix 2 (Jozef, live 2026-09-06): the link-drag release chooser is a radial whose
    //slice set now depends on drag direction and the dragged item's spoil/plant data - builds on
    //CanvasFixture's drag helpers (DragFrom/MoveTo/ClickAt/ReleaseAt) and its own custom-cache overloads
    //for the spoil/plant fixtures below.
    public sealed class RadialMenuTests {
        [AvaloniaFact]
        public void ReleasingALinkDragOnEmptyCanvasOpensTheRadialCentredOnTheReleasePoint() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            var release = new AvaloniaPoint(400, 300);
            fx.DragFrom(outputTab).To(release).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            Assert.Equal(release.X, radial.Bounds.Center.X, 1);
            Assert.Equal(release.Y, radial.Bounds.Center.Y, 1);
            Assert.Empty(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
        }

        [AvaloniaFact]
        public void HoveringAndClickingAddOpensTheLinkScopedPalette() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.MoveTo(radial.Bounds.Center + new AvaloniaVector(0, -64)); //top slice: Add is always centred here
            Assert.Equal(RadialChoice.Add, radial.HoveredSlice);

            fx.ClickAt(radial.Bounds.Center + new AvaloniaVector(0, -64));
            AddRequest request = fx.Control.ActiveAddRequest!;
            Assert.NotNull(request.Link);
            Assert.Equal(NewNodeType.Consumer, request.Link!.NodeType);
            Assert.NotNull(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void ClickingPassthroughDropsALinkedPassthroughNode() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Passthrough, radial.Slices));

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void CancelSlice_OutsideClick_AndEscape_AllDisposeTheDragWithoutANode() {
            foreach (string how in new[] { "cancel", "outside", "escape" }) {
                using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
                fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
                RadialMenu radial = fx.Control.OpenRadial!;
                AvaloniaPoint center = radial.Bounds.Center;

                if (how == "cancel")
                    fx.ClickAt(center + OffsetFor(RadialChoice.Cancel, radial.Slices));
                else if (how == "outside")
                    fx.ClickAt(center + new AvaloniaVector(150, 150));
                else
                    CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);

                Assert.False(fx.Control.FloatingPanelHost.IsOpen);
                Assert.Single(fx.Viewer.Graph.Nodes);
                Assert.Null(fx.Control.DraggedLink);
            }
        }

        //Gate fix 3, item 4: a press inside the 260x260 square but past the outer ring (radius 92) used to
        //land inside FloatingPanelHost.Bounds and get silently swallowed instead of cancelling.
        [AvaloniaFact]
        public void PressInsideTheSquareButOutsideTheRing_Cancels() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            fx.ClickAt(radial.Bounds.Center + new AvaloniaVector(100, 100));

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Single(fx.Viewer.Graph.Nodes);
            Assert.Null(fx.Control.DraggedLink);
        }

        [AvaloniaFact]
        public void PressOnTheHub_IsANoOp() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            fx.ClickAt(radial.Bounds.Center);

            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Same(radial, fx.Control.OpenRadial);
        }

        [AvaloniaFact]
        public void CmdHeldAtReleaseStillDropsAPassthroughWithoutTheRadial() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release(KeyModifiers.Meta);

            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
        }

        [AvaloniaFact]
        public void EscapeDuringALinkDragCancelsIt() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)); //no release
            CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);
            Assert.Null(fx.Control.DraggedLink);

            fx.ReleaseAt(new AvaloniaPoint(400, 300));
            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes);
        }

        [AvaloniaFact]
        public void PaletteFromTheRadialPlacesALinkedNode_AndTheNextOneToTheRight() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64));

            var box = fx.Control.OpenPalette!.Search;
            CanvasKeys.KeyDownOn(box, Key.Enter); //top row: the top-ranked compatible recipe (the item row is gone - Source/Output own that role now)
            CanvasKeys.KeyDownOn(box, Key.Enter);

            Assert.Equal(2, fx.Viewer.Graph.NodeLinks.Count());
            var placed = fx.Viewer.Graph.Nodes.Where(n => n is not SupplierNode || n.Location.Y != 0).OrderBy(n => n.Location.X).ToList();
            Assert.Equal(120, placed[1].Location.X - placed[0].Location.X, 0.5);
        }

        //---- gate fix 2: direction/spoil-dependent slice sets ----

        [AvaloniaFact]
        public void InputTabDragOnAnItemWithSpoilOrigins_SlicesAreAddSourcePassthroughUnspoilCancel() {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out _);
            var pair = new ItemQualityPair(rottenFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Source, RadialChoice.Passthrough, RadialChoice.Unspoil, RadialChoice.Cancel],
                fx.Control.OpenRadial!.Slices);
        }

        [AvaloniaFact]
        public void OutputTabDragOnASpoilableItem_SlicesAreAddOutputPassthroughSpoilCancel() {
            DataCache cache = SpoilCache(out _, out ItemPrototype freshFood);
            var pair = new ItemQualityPair(freshFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneSupplierOf(cache, pair, out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Output, RadialChoice.Passthrough, RadialChoice.Spoil, RadialChoice.Cancel],
                fx.Control.OpenRadial!.Slices);
        }

        [AvaloniaFact]
        public void InputTabDragOnAPlantOriginItemAtDefaultQuality_SlicesIncludeUnplant() {
            DataCache cache = PlantCache(out _, out ItemPrototype crop, out _);
            var pair = new ItemQualityPair(crop, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Source, RadialChoice.Passthrough, RadialChoice.Unplant, RadialChoice.Cancel],
                fx.Control.OpenRadial!.Slices);
        }

        [AvaloniaFact]
        public void InputTabDragOnAPlantOriginItemAtNonDefaultQuality_HasNoUnplantSlice() {
            DataCache cache = PlantCache(out _, out ItemPrototype crop, out IQuality nonDefaultQuality);
            var pair = new ItemQualityPair(crop, nonDefaultQuality);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Source, RadialChoice.Passthrough, RadialChoice.Cancel],
                fx.Control.OpenRadial!.Slices);
        }

        [AvaloniaFact]
        public void VanillaIronPlate_HasFourSlicesEachWay() {
            using var supplierRequest = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out AvaloniaPoint inputTab);
            supplierRequest.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();
            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Source, RadialChoice.Passthrough, RadialChoice.Cancel],
                supplierRequest.Control.OpenRadial!.Slices);

            using var consumerRequest = CanvasFixture.WithVanillaAndOneSupplierOf("iron-plate", out AvaloniaPoint outputTab);
            consumerRequest.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            Assert.Equal(
                [RadialChoice.Add, RadialChoice.Output, RadialChoice.Passthrough, RadialChoice.Cancel],
                consumerRequest.Control.OpenRadial!.Slices);
        }

        //---- gate fix 2: Source/Output/Spoil/UnSpoil outcomes ----

        [AvaloniaFact]
        public void ClickingSourcePlacesALinkedSupplierNode() {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out _);
            var pair = new ItemQualityPair(rottenFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Source, radial.Slices));

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<SupplierNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
            Assert.Null(fx.Control.PlacementFlashNode);
        }

        [AvaloniaFact]
        public void ClickingOutputPlacesALinkedConsumerNode() {
            DataCache cache = SpoilCache(out _, out ItemPrototype freshFood);
            var pair = new ItemQualityPair(freshFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneSupplierOf(cache, pair, out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Output, radial.Slices));

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<ConsumerNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void ClickingSpoilPlacesASpoilNode() {
            DataCache cache = SpoilCache(out _, out ItemPrototype freshFood);
            var pair = new ItemQualityPair(freshFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneSupplierOf(cache, pair, out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Spoil, radial.Slices));

            SpoilNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<SpoilNode>());
            Assert.Equal("fresh-food", node.InputItem.Item!.Name);
            Assert.Equal("rotten-food", node.OutputItem.Item!.Name);
        }

        [AvaloniaFact]
        public void ClickingUnspoilWithOneOriginPlacesASpoilNodeDirectly() {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out _);
            var pair = new ItemQualityPair(rottenFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Unspoil, radial.Slices));

            SpoilNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<SpoilNode>());
            Assert.Equal("fresh-food", node.InputItem.Item!.Name);
            Assert.Equal("rotten-food", node.OutputItem.Item!.Name);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void ClickingUnspoilWithSeveralOriginsRaisesTheBrowseOpenSeamWithTheOriginsRole() {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out _, originCount: 2);
            var pair = new ItemQualityPair(rottenFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            AddRequest? browseRequest = null;
            fx.Control.BrowseRequested += (_, r) => browseRequest = r;
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Unspoil, radial.Slices));

            Assert.NotNull(browseRequest);
            Assert.Equal(NodeType.Spoil, browseRequest!.OriginsRole);
            Assert.NotNull(browseRequest.Link);
            Assert.Equal("rotten-food", browseRequest.Link!.Item.Item!.Name);
            Assert.Empty(fx.Viewer.Graph.Nodes.OfType<SpoilNode>());
        }

        [AvaloniaFact]
        public void ClickingPlantPlacesAPlantNode() {
            DataCache cache = PlantCache(out ItemPrototype seed, out ItemPrototype crop, out _);
            var pair = new ItemQualityPair(seed, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneSupplierOf(cache, pair, out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Plant, radial.Slices));

            PlantNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<PlantNode>());
            Assert.Equal("seed", node.Seed.Item!.Name);
            Assert.Equal("crop", node.BasePlantProcess.ProductList[0].Name);
        }

        [AvaloniaFact]
        public void ClickingUnplantWithOneOriginPlacesAPlantNodeDirectly() {
            DataCache cache = PlantCache(out ItemPrototype seed, out ItemPrototype crop, out _);
            var pair = new ItemQualityPair(crop, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.ClickAt(radial.Bounds.Center + OffsetFor(RadialChoice.Unplant, radial.Slices));

            PlantNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<PlantNode>());
            Assert.Equal("seed", node.Seed.Item!.Name);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //---- gate fix 2: label overflow (260x260 control, MaxWidth-wrapped labels) ----

        [AvaloniaFact]
        public void EveryLabelStaysInsideTheControlBounds_ForSixAndThreeSlices() {
            IReadOnlyList<RadialChoice>[] cases = [
                [RadialChoice.Add, RadialChoice.Source, RadialChoice.Passthrough, RadialChoice.Unspoil, RadialChoice.Unplant, RadialChoice.Cancel],
                [RadialChoice.Add, RadialChoice.Passthrough, RadialChoice.Cancel],
            ];

            foreach (IReadOnlyList<RadialChoice> choices in cases) {
                var radial = new RadialMenu(choices);
                radial.Measure(new Size(260, 260));
                radial.Arrange(new Rect(0, 0, 260, 260));

                var controlBounds = new Rect(0, 0, 260, 260);
                foreach (TextBlock label in radial.Children.OfType<TextBlock>()) {
                    Point origin = label.TranslatePoint(new Point(0, 0), radial)!.Value;
                    var labelBounds = new Rect(origin, label.Bounds.Size);
                    Assert.True(controlBounds.Contains(labelBounds), $"'{label.Text}' bounds {labelBounds} escape the {controlBounds} control");
                }
            }
        }

        [Fact]
        public void ResolveChoice_IsNullInTheHubAndBeyondTheRim_AndPicksBySliceAngleInBetween() {
            RadialChoice[] choices = [RadialChoice.Add, RadialChoice.Passthrough, RadialChoice.Cancel];

            for (int radius = 0; radius <= 35; radius++)
                foreach (double angle in SampleAngles)
                    Assert.Null(RadialMenu.ResolveChoice(PointAt(radius, angle), choices));

            for (int radius = 37; radius <= 91; radius++)
                foreach (double angle in SampleAngles)
                    Assert.Equal(ExpectedSlice(angle), RadialMenu.ResolveChoice(PointAt(radius, angle), choices));

            for (int radius = 93; radius <= 150; radius += 10)
                foreach (double angle in SampleAngles)
                    Assert.Null(RadialMenu.ResolveChoice(PointAt(radius, angle), choices));
        }

        [AvaloniaFact]
        public void AddSlicePathBoundsMatchThe92RadiusExtents() {
            var radial = new RadialMenu([RadialChoice.Add, RadialChoice.Passthrough, RadialChoice.Cancel]);
            Path addSlice = radial.Children.OfType<Path>().First(p => p.Classes.Contains("foreman-radial-slice"));
            Rect bounds = addSlice.Data!.Bounds;

            Assert.Equal(50.3, bounds.Left, 1);
            Assert.Equal(209.7, bounds.Right, 1);
            Assert.Equal(38, bounds.Top, 1);
        }

        private static readonly double[] SampleAngles = [.. Enumerable.Range(0, 24).Select(i => 7.5 + (i * 15.0))]; //offset off the slice boundaries (multiples of 30)

        private static AvaloniaPoint PointAt(double radius, double angleDeg) {
            double angleRad = angleDeg * Math.PI / 180;
            return new AvaloniaPoint(130 + (radius * Math.Cos(angleRad)), 130 + (radius * Math.Sin(angleRad)));
        }

        private static RadialChoice ExpectedSlice(double angleDeg) {
            double normalized = ((angleDeg % 360) + 360) % 360;
            if (normalized is >= 210 and < 330)
                return RadialChoice.Add;
            if (normalized is >= 90 and < 210)
                return RadialChoice.Cancel;
            return RadialChoice.Passthrough;
        }

        //Mirrors RadialMenu's own SliceAngles formula (index 0 = Add, centred at the top, clockwise from
        //there) so click tests can hit a slice by name regardless of how many slices the set has.
        private static AvaloniaVector OffsetFor(RadialChoice choice, IReadOnlyList<RadialChoice> slices, double radius = 60) {
            int index = slices.ToList().IndexOf(choice);
            double span = 360.0 / slices.Count;
            double angleDeg = 270 + (index * span);
            double angleRad = angleDeg * Math.PI / 180;
            return new AvaloniaVector(radius * Math.Cos(angleRad), radius * Math.Sin(angleRad));
        }

        //A minimal fresh-food/rotten-food pair (mirrors LinkDragTests' own spoil fixture): rottenFood.SpoilOrigins
        //defaults to one origin (freshFood); originCount adds more freshFood-* items spoiling into the same
        //rottenFood for the multi-origin browse-handoff case.
        private static DataCache SpoilCache(out ItemPrototype rottenFood, out ItemPrototype freshFood, int originCount = 1) {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            rottenFood = new ItemPrototype(cache, "rotten-food", "rotten-food", subgroup, "z");
            store.Items[rottenFood.Name] = rottenFood;
            freshFood = new ItemPrototype(cache, "fresh-food", "fresh-food", subgroup, "a");
            store.Items[freshFood.Name] = freshFood;
            freshFood.SpoilResult = rottenFood;
            rottenFood.SpoilOriginsInternal.Add(freshFood);

            for (int i = 1; i < originCount; i++) {
                var extra = new ItemPrototype(cache, $"fresh-food-{i}", $"fresh-food-{i}", subgroup, "a");
                store.Items[extra.Name] = extra;
                extra.SpoilResult = rottenFood;
                rottenFood.SpoilOriginsInternal.Add(extra);
            }

            return cache;
        }

        //A seed/crop pair (mirrors SpoilCache): crop.PlantOrigins defaults to one origin (seed), plus a
        //second, non-default quality for the UnPlant default-quality gate tests.
        private static DataCache PlantCache(out ItemPrototype seed, out ItemPrototype crop, out IQuality nonDefaultQuality, int originCount = 1) {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            var legendary = new QualityPrototype(cache, "legendary", "Legendary", "b");
            DataCacheStore store = Store(cache);
            store.Qualities[normal.Name] = normal;
            store.Qualities[legendary.Name] = legendary;
            store.DefaultQuality = normal;
            nonDefaultQuality = legendary;

            seed = new ItemPrototype(cache, "seed", "seed", subgroup, "a");
            store.Items[seed.Name] = seed;
            crop = new ItemPrototype(cache, "crop", "crop", subgroup, "z");
            store.Items[crop.Name] = crop;

            var process = new PlantProcessPrototype(cache, "plant-seed");
            process.InternalOneWayAddProduct(crop, 1);
            process.Seed = seed;
            seed.PlantResult = process;
            crop.PlantOriginsInternal.Add(seed);

            for (int i = 1; i < originCount; i++) {
                var extraSeed = new ItemPrototype(cache, $"seed-{i}", $"seed-{i}", subgroup, "a");
                store.Items[extraSeed.Name] = extraSeed;
                var extraProcess = new PlantProcessPrototype(cache, $"plant-seed-{i}");
                extraProcess.InternalOneWayAddProduct(crop, 1);
                extraProcess.Seed = extraSeed;
                extraSeed.PlantResult = extraProcess;
                crop.PlantOriginsInternal.Add(extraSeed);
            }

            return cache;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }
    }
}
