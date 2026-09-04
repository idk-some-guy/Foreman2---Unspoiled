using Foreman.Mac.ViewModels;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    public class InspectorViewModelTests {
        [Fact]
        public void EmptySelection_HidesAndShowsNoSelectionText() {
            var vm = new InspectorViewModel();
            vm.SetSelection([]);
            Assert.False(vm.HasNode);
            Assert.False(vm.IsVisible);
            Assert.Equal("No selection", vm.EmptyStateText);
        }

        [Fact]
        public void SingleRecipeNode_SelectsRecipeSegmentAndShows() {
            var vm = new InspectorViewModel();
            vm.SetSelection([FakeNodes.Recipe()]);
            Assert.True(vm.HasNode);
            Assert.Equal(InspectorSegment.Recipe, vm.Segment);
            Assert.True(vm.IsVisible);
        }

        [Fact]
        public void SinglePassthroughNode_SelectsFlowSegment() {
            var vm = new InspectorViewModel();
            vm.SetSelection([FakeNodes.Passthrough()]);
            Assert.Equal(InspectorSegment.Flow, vm.Segment);
        }

        [Fact]
        public void MultiSelection_ShowsCountAndEditsNothing() {
            var vm = new InspectorViewModel();
            vm.SetSelection([FakeNodes.Recipe(), FakeNodes.Passthrough(), FakeNodes.Recipe()]);
            Assert.Null(vm.Node);
            Assert.Equal("3 nodes selected", vm.EmptyStateText);
            Assert.False(vm.IsVisible);
        }

        [Fact]
        public void Pinned_StaysVisibleWithEmptySelection() {
            var vm = new InspectorViewModel { IsPinned = true };
            vm.SetSelection([]);
            Assert.True(vm.IsVisible);
        }

        [Fact]
        public void ShowOverride_MakesVisibleAndClearsWhenSelectionClears() {
            var vm = new InspectorViewModel();
            vm.ShowOverride = true;
            Assert.True(vm.IsVisible);
            vm.ShowOverride = false;
            vm.SetSelection([]);
            Assert.False(vm.IsVisible);
        }

        [Fact]
        public void IsVisible_RaisesPropertyChangedOnEveryTransition() {
            var vm = new InspectorViewModel();
            int raised = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(InspectorViewModel.IsVisible)) raised++; };
            vm.SetSelection([FakeNodes.Recipe()]);
            vm.SetSelection([]);
            vm.IsPinned = true;
            Assert.Equal(3, raised);
        }
    }
}
