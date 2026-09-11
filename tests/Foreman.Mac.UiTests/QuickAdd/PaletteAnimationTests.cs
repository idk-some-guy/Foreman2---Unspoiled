using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.Kit;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using System;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Gate fix 8, item 2a / gate fix 11: palette rows fade+slide in on entry, on Motion's shared timings.
    public sealed class PaletteAnimationTests {
        [AvaloniaFact]
        public void EnteringRow_StartsAtOpacityZero_TranslatedUp() {
            var row = NewStyledRow("entering");

            Assert.Equal(0, row.Opacity);
            Assert.NotNull(row.RenderTransform);
        }

        [AvaloniaFact]
        public void SettledRow_IsFullyOpaque_WithNoTransform_AndDeclaresTheSpecifiedTransitions() {
            var row = NewStyledRow();

            Assert.Equal(1, row.Opacity);

            var opacity = Assert.Single(row.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.EntranceDuration.TotalMilliseconds, opacity.Duration.TotalMilliseconds);
            Assert.IsType(Motion.EntranceEasing.GetType(), opacity.Easing);

            var slide = Assert.Single(row.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.EntranceDuration.TotalMilliseconds, slide.Duration.TotalMilliseconds);
            Assert.IsType(Motion.EntranceEasing.GetType(), slide.Easing);
        }

        //End to end: a row realized by the real palette sheds .entering and settles at full opacity, no
        //matter exactly how many dispatcher passes that takes.
        [AvaloniaFact]
        public void ARowRealizedByThePalette_EventuallySettlesAtFullOpacity_WithEnteringShed() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron";
            var results = palette.FindControl<ItemsControl>("Results")!;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Control row = results.ContainerFromIndex(0)!;
            Assert.DoesNotContain("entering", row.Classes);
            Assert.Equal(1, row.Opacity, 2);
        }

        //Gate fix 11, item 2b: rows cascade top-down instead of popping together - each row's own
        //stagger delay grows with its index, capped, and traces back to Motion.
        [AvaloniaFact]
        public void RowsRealizedByThePalette_HaveAStaggerDelayThatGrowsWithIndex() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron";
            var results = palette.FindControl<ItemsControl>("Results")!;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            TimeSpan DelayOf(int index) => results.ContainerFromIndex(index)!.Transitions!
                .OfType<DoubleTransition>().First(t => t.Property == Visual.OpacityProperty).Delay;

            Assert.Equal(Motion.StaggerDelay(0), DelayOf(0));
            Assert.Equal(Motion.StaggerDelay(1), DelayOf(1));
            Assert.Equal(Motion.StaggerDelay(2), DelayOf(2));
            Assert.True(DelayOf(1) > DelayOf(0));
            Assert.True(DelayOf(2) > DelayOf(1));
        }

        //Gate fix 11, item 1: a row that's still in the results after a query change keeps its own
        //container - only rows that are genuinely new get the .entering treatment.
        [AvaloniaFact]
        public void ARowThatSurvivesAQueryChange_KeepsItsOwnContainer() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;
            vm.Query = "iron";
            var results = palette.FindControl<ItemsControl>("Results")!;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            AddCandidate survivor = vm.Rows.First(r => r.Name == "Iron plate" && r.KindLabel == "item").Candidate;
            int indexBefore = IndexOf(vm, survivor);
            Control containerBefore = results.ContainerFromIndex(indexBefore)!;

            vm.Query = "iron pl";
            Dispatcher.UIThread.RunJobs();

            int indexAfter = IndexOf(vm, survivor);
            Control containerAfter = results.ContainerFromIndex(indexAfter)!;

            Assert.Same(containerBefore, containerAfter);
            Assert.DoesNotContain("entering", containerAfter.Classes);
        }

        private static int IndexOf(QuickAddViewModel vm, AddCandidate candidate) {
            var rows = vm.Rows.ToList();
            return rows.FindIndex(r => r.Candidate.Equals(candidate));
        }

        private static Button NewStyledRow(params string[] extraClasses) {
            var row = new Button { Classes = { "foreman-qa-row" } };
            foreach (string extra in extraClasses)
                row.Classes.Add(extra);
            var window = new AvaloniaWindow { Content = row, Width = 200, Height = 60 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return row;
        }
    }
}
