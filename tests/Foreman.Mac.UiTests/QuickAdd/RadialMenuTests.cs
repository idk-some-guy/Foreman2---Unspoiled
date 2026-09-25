using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
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
    //The link-drag release chooser's slice set depends on drag direction and the dragged item's spoil/plant data.
    public sealed class RadialMenuTests : IDisposable {
        public void Dispose() => WindowScaler.SetScale(1.0);

        private static readonly string[] SixOutputOutcomes = ["Add…", "Output", "Passthrough", "Spoil", "Plant", "Cancel"];

        private static RadialMenu OpenSixOutcomeRadial(out CanvasFixture fx, AvaloniaPoint release) {
            DataCache cache = SixOutcomeCache(out ItemPrototype seed);
            var pair = new ItemQualityPair(seed, Store(cache).DefaultQuality!);
            fx = CanvasFixture.WithCacheAndOneSupplierOf(cache, pair, out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(release).Release();
            return fx.Control.OpenRadial!;
        }

        [AvaloniaFact]
        public void Radial_AnyOtherKey_DismissesTheRing_AndOpensTheSameSixOutcomesAsAContextMenuAtTheReleasePoint() {
            var release = new AvaloniaPoint(400, 300);
            RadialMenu radial = OpenSixOutcomeRadial(out CanvasFixture fx, release);
            using CanvasFixture _ = fx;

            CanvasKeys.KeyDownOn(radial.Ring, Key.A);

            Assert.Null(fx.Control.OpenRadial);
            MenuPanel menu = fx.Control.OpenRadialFallback!;
            Assert.Equal(SixOutputOutcomes, menu.Items.Where(r => !r.IsSeparator).Select(r => r.Text));
            Assert.Equal(2, menu.Items.Count(r => r.IsSeparator));
            Assert.Equal(release, fx.Control.RadialFallbackPopup.PlacementRect!.Value.TopLeft);
            Assert.True(menu.IsKeyboardFocusWithin);

            CanvasKeys.KeyDownOn(menu, Key.Down);
            CanvasKeys.KeyDownOn(menu, Key.Down);
            CanvasKeys.KeyDownOn(menu, Key.Down);
            CanvasKeys.KeyDownOn(menu, Key.Enter);

            Assert.Null(fx.Control.OpenRadialFallback);
            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void Radial_FallbackMenuDropsEmptyGroups_AndEscapeThereCancelsWithoutANode() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            CanvasKeys.KeyDownOn(fx.Control.OpenRadial!.Ring, Key.A);
            MenuPanel menu = fx.Control.OpenRadialFallback!;
            Assert.Equal(["Add…", "Output", "Passthrough", "", "Cancel"], menu.Items.Select(r => r.Text));

            CanvasKeys.KeyDownOn(menu, Key.Escape);
            Assert.Null(fx.Control.OpenRadialFallback);
            Assert.Single(fx.Viewer.Graph.Nodes);
        }

        [AvaloniaFact]
        public void Radial_ArrowKeysArmByDirection_AndReturnCommitsTheArmedSlice() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            CanvasKeys.KeyDownOn(radial.Ring, Key.Up);
            Assert.Equal(RadialMenu.SlotOf(RadialChoice.Add), radial.Ring.ArmedIndex);
            CanvasKeys.KeyDownOn(radial.Ring, Key.Right);
            Assert.Equal(RadialMenu.SlotOf(RadialChoice.Passthrough), radial.Ring.ArmedIndex);
            CanvasKeys.KeyDownOn(radial.Ring, Key.Return);

            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void Radial_TabOnTheFocusedRingDoesNothing_AndEscapeCancelsInsteadOfOpeningTheFallbackMenu() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            CanvasKeys.KeyDownOn(fx.Control.OpenRadial!.Ring, Key.Tab);
            Assert.NotNull(fx.Control.OpenRadial);
            Assert.True(fx.Control.OpenRadial!.Ring.IsFocused);
            Assert.Null(fx.Control.OpenRadialFallback);

            CanvasKeys.KeyDownOn(fx.Control.OpenRadial!.Ring, Key.A, KeyModifiers.Meta);
            Assert.NotNull(fx.Control.OpenRadial);
            Assert.Null(fx.Control.OpenRadialFallback);
            Assert.Empty(fx.Viewer.SelectedNodes);

            CanvasKeys.KeyDownOn(fx.Control.OpenRadial!.Ring, Key.Escape);

            Assert.Null(fx.Control.OpenRadial);
            Assert.Null(fx.Control.OpenRadialFallback);
            Assert.Single(fx.Viewer.Graph.Nodes);
        }

        [AvaloniaFact]
        public void Radial_MovingPast140ptFromTheCentre_Cancels() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            AvaloniaPoint center = fx.Control.OpenRadial!.Ring.Bounds.Center;

            fx.MoveTo(center + new AvaloniaVector(135, 0));
            Assert.NotNull(fx.Control.OpenRadial);

            fx.MoveTo(center + new AvaloniaVector(145, 0));
            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes);
        }

        [AvaloniaFact]
        public void Radial_ExposesItselfAsAMenu_WithSixNamedItems() {
            RadialMenu radial = OpenSixOutcomeRadial(out CanvasFixture fx, new AvaloniaPoint(400, 300));
            using CanvasFixture _ = fx;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(radial.Ring);
            Assert.Equal(AutomationControlType.Menu, peer.GetAutomationControlType());
            Assert.Equal("Link options", peer.GetName());
            Assert.Equal(SixOutputOutcomes, peer.GetChildren().Select(c => c.GetName()));
        }

        [AvaloniaFact]
        public void Radial_RingIs220pt_AtEveryScaleStep() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialRing ring = fx.Control.OpenRadial!.Ring;

            foreach (double step in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 }) {
                WindowScaler.SetScale(step);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(220, ring.Bounds.Width);
                Assert.Equal(220, ring.Bounds.Height);
            }
        }

        [AvaloniaFact]
        public void ReleasingALinkDragOnEmptyCanvasOpensTheRadialCentredOnTheReleasePoint() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            var release = new AvaloniaPoint(400, 300);
            fx.DragFrom(outputTab).To(release).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            Assert.Equal(release.X, radial.Ring.Bounds.Center.X, 1);
            Assert.Equal(release.Y, radial.Ring.Bounds.Center.Y, 1);
            Assert.Empty(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
        }

        [AvaloniaFact]
        public void HoveringAndClickingAddOpensTheLinkScopedPalette() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.MoveTo(radial.Ring.Bounds.Center + new AvaloniaVector(0, -64)); //top slice: Add is always centred here
            Assert.Equal(RadialMenu.SlotOf(RadialChoice.Add), radial.Ring.ArmedIndex);

            fx.ClickAt(radial.Ring.Bounds.Center + new AvaloniaVector(0, -64));
            AddRequest request = fx.Control.ActiveAddRequest!;
            Assert.NotNull(request.Link);
            Assert.Equal(NewNodeType.Consumer, request.Link!.NodeType);
            Assert.NotNull(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void ClickingPassthrough_OnItsSliceOrPastTheRingWithin140pt_DropsALinkedPassthroughNode() {
            foreach (bool pastTheRing in new[] { false, true }) {
                using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
                fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

                RadialMenu radial = fx.Control.OpenRadial!;
                if (pastTheRing) {
                    AvaloniaPoint center = radial.Ring.Bounds.Center;
                    fx.MoveTo(center + Polar(60, 30));
                    Assert.Equal(RadialMenu.SlotOf(RadialChoice.Passthrough), radial.Ring.ArmedIndex);
                    fx.MoveAndClickAt(center + Polar(125, 10));
                } else {
                    ClickSlice(fx, radial, RadialChoice.Passthrough);
                }

                Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
                Assert.Single(fx.Viewer.Graph.NodeLinks);
                Assert.Null(fx.Control.OpenRadial);
            }

            using var hub = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint tab);
            hub.DragFrom(tab).To(new AvaloniaPoint(400, 300)).Release();
            AvaloniaPoint hubCenter = hub.Control.OpenRadial!.Ring.Bounds.Center;
            hub.MoveTo(hubCenter + Polar(60, 30));
            hub.MoveTo(hubCenter + Polar(10, 30));
            hub.MoveAndClickAt(hubCenter + Polar(125, 10));
            Assert.Null(hub.Control.OpenRadial);
            Assert.Single(hub.Viewer.Graph.Nodes);

            //A tip drop clears the source by 24 along the flow axis; a side drop stays put and snaps outward on the grid.
            foreach (double step in new[] { 1.0, 1.5 }) {
                foreach (RadialChoice choice in new[] { RadialChoice.Passthrough, RadialChoice.Output }) {
                    float s = (float)step;
                    using var near = CanvasFixture.WithVanillaAndOneSupplier(out _);
                    (BaseNodeElement source, BaseNodeElement placed, System.Drawing.Point dropped) = DropBeside(near, step, choice, 0, -10 * s, 0);
                    Assert.Equal(dropped.X, placed.Location.X);
                    Assert.InRange(FlowGap(source, placed, s), (24 * s) - 0.001f, (24 * s) + 1);
                    int cleared = placed.Location.Y;

                    using var side = CanvasFixture.WithVanillaAndOneSupplier(out _);
                    (_, BaseNodeElement aside, System.Drawing.Point sideDrop) = DropBeside(side, step, choice, 200 * s, 0, 0);
                    Assert.Equal(new System.Drawing.Point(sideDrop.X, sideDrop.Y - (choice == RadialChoice.Output ? aside.Height / 2 : 0)), aside.Location);

                    int unit = Enumerable.Range(5, 200).First(u => Nearest(cleared, u) > cleared);
                    using var gridded = CanvasFixture.WithVanillaAndOneSupplier(out _);
                    (BaseNodeElement gridSource, BaseNodeElement snapped, _) = DropBeside(gridded, step, choice, 0, -10 * s, unit);
                    Assert.Equal(0, snapped.Location.Y % unit);
                    Assert.True(snapped.Location.Y < cleared);
                    Assert.True(FlowGap(gridSource, snapped, s) >= 24 * s);
                }
            }
        }

        private static int Nearest(int value, int unit) {
            value += Math.Sign(value) * unit / 2;
            return value - (value % unit);
        }

        private static float FlowGap(BaseNodeElement source, BaseNodeElement placed, float s) =>
            (source.Location.Y - (source.Height / 2f) - (13 * s)) - (placed.Location.Y + (placed.Height / 2f) + (13 * s));

        private static (BaseNodeElement Source, BaseNodeElement Placed, System.Drawing.Point Dropped) DropBeside(CanvasFixture fx, double step, RadialChoice choice, float dx, float dy, int gridUnit) {
            WindowScaler.SetScale(step);
            fx.Control.Grid.ShowGrid = gridUnit > 0;
            fx.Control.Grid.CurrentGridUnit = gridUnit;
            BaseNodeElement source = fx.Viewer.NodeElementDictionary.Values.Single();
            source.RequestStateUpdate();
            source.PrePaint();
            ItemTabElement sourceTab = source.SubElements.OfType<ItemTabElement>().Single();
            AvaloniaPoint tab = fx.Control.Viewport.GraphToScreen(source.LocalToGraph(sourceTab.Location));
            SkiaSharp.SKRect tip = sourceTab.TabGeometry().Rect;
            float x = dx == 0 ? tip.MidX : tip.Right + dx;
            float y = dx == 0 ? tip.Top + dy : tip.MidY;
            fx.DragFrom(tab).To(fx.Control.Viewport.GraphToScreen(new System.Drawing.Point((int)Math.Round(x), (int)Math.Round(y)))).Release();
            ClickSlice(fx, fx.Control.OpenRadial!, choice);

            System.Drawing.Point dropped = fx.Control.LastNewNodeLinkDragRequest!.Value.EndpointLocation;
            BaseNodeElement placed = fx.Viewer.NodeElementDictionary.Values.Single(node => node != source);
            placed.RequestStateUpdate();
            placed.PrePaint();
            WindowScaler.SetScale(1.0);
            return (source, placed, dropped);
        }

        private static AvaloniaVector Polar(double radius, double angleDeg) =>
            new(radius * Math.Cos(angleDeg * Math.PI / 180), radius * Math.Sin(angleDeg * Math.PI / 180));

        [AvaloniaFact]
        public void EveryWayOut_ClosesTheRadial_FadesTheRingOutFirst_ThenTakesItOutOfTheTabOrderAndTheTree() {
            foreach (string how in new[] { "commit", "cancel", "outside", "escape", "past140" }) {
                using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
                fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
                RadialMenu radial = fx.Control.OpenRadial!;
                RadialRing ring = radial.Ring;
                AvaloniaPoint center = ring.Bounds.Center;

                if (how == "commit")
                    ClickSlice(fx, radial, RadialChoice.Passthrough);
                else if (how == "cancel")
                    ClickSlice(fx, radial, RadialChoice.Cancel);
                else if (how == "outside")
                    fx.ClickAt(center + new AvaloniaVector(150, 150));
                else if (how == "escape")
                    CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);
                else
                    fx.MoveTo(center + new AvaloniaVector(145, 0));

                Assert.Null(fx.Control.OpenRadial);
                Assert.Equal(how == "commit" ? 2 : 1, fx.Viewer.Graph.Nodes.Count());
                if (how != "commit")
                    Assert.Null(fx.Control.DraggedLink);
                Assert.True(ring.IsVisible, how);
                TabTwelveTimesFromTheCanvas(fx, ring);
                Assert.True(ring.IsVisible, how);

                AnimationClock.Settle((int)Foreman.Mac.Kit.Motion.RadialCloseDuration.TotalMilliseconds + 150);
                Assert.False(ring.IsVisible, how);
                TabTwelveTimesFromTheCanvas(fx, ring);
                Assert.False(HasLinkOptionsMenu(ControlAutomationPeer.CreatePeerForElement(fx.Control)), how);

                fx.DragFrom(outputTab).To(new AvaloniaPoint(300, 500)).Release();
                RadialRing reopened = fx.Control.OpenRadial!.Ring;
                Assert.True(reopened.IsVisible, how);
                Assert.True(reopened.IsFocused, how);
            }
        }

        //Tab raised on the focused element bubbles to the TopLevel's keyboard navigation handler, which moves focus.
        private static void TabTwelveTimesFromTheCanvas(CanvasFixture fx, RadialRing ring) {
            fx.Control.Focus();
            for (int i = 0; i < 12; i++) {
                CanvasKeys.KeyDownOn((Control)fx.Window.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.NotSame(ring, fx.Window.FocusManager.GetFocusedElement());
            }
        }

        private static bool HasLinkOptionsMenu(AutomationPeer peer) =>
            (peer.GetAutomationControlType() == AutomationControlType.Menu && peer.GetName() == "Link options")
            || peer.GetChildren().Any(HasLinkOptionsMenu);

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
            ClickSlice(fx, fx.Control.OpenRadial!, RadialChoice.Add);

            var box = fx.Control.OpenPalette!.Search;
            CanvasKeys.KeyDownOn(box, Key.Enter); //top row: the top-ranked compatible recipe (the item row is gone - Source/Output own that role now)
            CanvasKeys.KeyDownOn(box, Key.Enter);

            Assert.Equal(2, fx.Viewer.Graph.NodeLinks.Count());
            var placed = fx.Viewer.Graph.Nodes.Where(n => n is not SupplierNode || n.Location.Y != 0).OrderBy(n => n.Location.X).ToList();
            Assert.Equal(120, placed[1].Location.X - placed[0].Location.X, 0.5);
        }

        //---- direction/spoil-dependent slice sets ----

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

        //---- Source/Output/Spoil/UnSpoil outcomes ----

        [AvaloniaFact]
        public void ClickingSourcePlacesALinkedSupplierNode() {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out _);
            var pair = new ItemQualityPair(rottenFood, Store(cache).DefaultQuality!);
            using var fx = CanvasFixture.WithCacheAndOneConsumerOf(cache, pair, out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            ClickSlice(fx, radial, RadialChoice.Source);

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
            ClickSlice(fx, radial, RadialChoice.Output);

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
            ClickSlice(fx, radial, RadialChoice.Spoil);

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
            ClickSlice(fx, radial, RadialChoice.Unspoil);

            SpoilNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<SpoilNode>());
            Assert.Equal("fresh-food", node.InputItem.Item!.Name);
            Assert.Equal("rotten-food", node.OutputItem.Item!.Name);
            Assert.Null(fx.Control.OpenRadial);
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
            ClickSlice(fx, radial, RadialChoice.Unspoil);

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
            ClickSlice(fx, radial, RadialChoice.Plant);

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
            ClickSlice(fx, radial, RadialChoice.Unplant);

            PlantNode node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<PlantNode>());
            Assert.Equal("seed", node.Seed.Item!.Name);
            Assert.Null(fx.Control.OpenRadial);
        }

        //Six fixed 60° slots, slot 0 centred at the top and clockwise from there, whatever the outcome count.
        private static AvaloniaVector OffsetFor(RadialChoice choice, double radius = 60) {
            double angleDeg = 270 + (RadialMenu.SlotOf(choice) * 60);
            double angleRad = angleDeg * Math.PI / 180;
            return new AvaloniaVector(radius * Math.Cos(angleRad), radius * Math.Sin(angleRad));
        }

        private static void ClickSlice(CanvasFixture fx, RadialMenu radial, RadialChoice choice) {
            fx.MoveAndClickAt(radial.Ring.Bounds.Center + OffsetFor(choice));
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

        //One item that both spoils and plants, so an output-tab drag fills all six slots.
        private static DataCache SixOutcomeCache(out ItemPrototype seed) {
            DataCache cache = SpoilCache(out ItemPrototype rottenFood, out seed);
            DataCacheStore store = Store(cache);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup-crop", "z");
            var crop = new ItemPrototype(cache, "crop", "crop", subgroup, "z");
            store.Items[crop.Name] = crop;
            var process = new PlantProcessPrototype(cache, "plant-fresh-food");
            process.InternalOneWayAddProduct(crop, 1);
            process.Seed = seed;
            seed.PlantResult = process;
            crop.PlantOriginsInternal.Add(seed);
            return cache;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }
    }
}
