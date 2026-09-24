using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitFormRowTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitFormRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static FormRow Themed(string label, object? content = null) =>
            new() { Theme = KitFormRowTheme(), Label = label, Content = content ?? new TextBlock { Text = "value" } };

        private static Grid FrameGrid(FormRow row) =>
            row.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");

        private static TextBlock LabelText(FormRow row) =>
            row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

        private static Border ControlBorder(FormRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_ControlBorder");

        private static Panel ControlHost(FormRow row) =>
            row.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_ControlHost");

        [AvaloniaFact]
        public void KitFormRow_IsAControlThemeTargetingFormRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(FormRow), KitFormRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void ColumnWidths_WindowShape_AreLabel132Gutter12Star() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(132, grid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(12, grid.ColumnDefinitions[1].Width.Value);
            Assert.True(grid.ColumnDefinitions[2].Width.IsStar);
        }

        [AvaloniaFact]
        public void ColumnWidths_InspectorShape_SwapsLabelTo108() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Quality");
            row.LabelColumn = FormRowLabelColumn.Inspector;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(108, grid.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void ColumnWidths_PopoverShape_SwapsLabelTo86() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Show");
            row.LabelColumn = FormRowLabelColumn.Popover;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(86, grid.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void Label_IsRightAlignedWrappingNoTrim() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Default node direction");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            Assert.Equal(HorizontalAlignment.Right, label.HorizontalAlignment);
            Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
            Assert.Equal(TextTrimming.None, label.TextTrimming);
            Assert.Equal("Default node direction", label.Text);
        }

        [AvaloniaFact]
        public void Label_BaselineAlignsToTheControlsFirstLine_ForASingleLineControl() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("Node width", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            Border controlBorder = ControlBorder(row);

            TextLine labelLine = label.TextLayout!.TextLines[0];
            double expectedLocal = (controlBorder.Bounds.Height - labelLine.Height) / 2 + labelLine.Baseline;
            double expectedY = controlBorder.TranslatePoint(new Point(0, expectedLocal), fixture.Window)!.Value.Y;
            double labelBaselineY = label.TranslatePoint(new Point(0, labelLine.Baseline), fixture.Window)!.Value.Y;

            Assert.Equal(expectedY, labelBaselineY, 2);
        }

        [AvaloniaFact]
        public void Label_BaselineTracksTheContentsOwnArrangedHeight_NotTheFixedRowMinHeight() {
            using var fixture = new KitFixture();
            var shortContent = new Border { Width = 80, Height = 22 };
            var tallContent = new Border { Width = 80, Height = 26 };
            FormRow shortRow = Themed("Node width", shortContent);
            FormRow tallRow = Themed("Node width", tallContent);
            var stack = new StackPanel { Children = { shortRow, tallRow } };
            fixture.Window.Content = stack;
            Dispatcher.UIThread.RunJobs();

            double shortOffset = Assert.IsType<TranslateTransform>(LabelText(shortRow).RenderTransform).Y;
            double tallOffset = Assert.IsType<TranslateTransform>(LabelText(tallRow).RenderTransform).Y;

            Assert.Equal((tallContent.Height - shortContent.Height) / 2, tallOffset - shortOffset, 2);
        }

        [AvaloniaFact]
        public void Label_BaselineAlignsToTheControlsFirstLine_ForAMultilineTextBox() {
            using var fixture = new KitFixture();
            var content = new TextBox { AcceptsReturn = true, Height = 80, Text = "line one\nline two\nline three" };
            FormRow row = Themed("Node width", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            TextPresenter presenter = content.GetVisualDescendants().OfType<TextPresenter>().Single();

            double labelBaselineY = label.TranslatePoint(new Point(0, label.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;
            double presenterFirstLineBaselineY = presenter.TranslatePoint(new Point(0, presenter.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;
            Assert.Equal(presenterFirstLineBaselineY, labelBaselineY, 2);

            double controlCenterY = content.TranslatePoint(new Point(0, content.Bounds.Height / 2), fixture.Window)!.Value.Y;
            Assert.True(labelBaselineY < controlCenterY - 5,
                $"label baseline {labelBaselineY} should sit near the control's first line, well above its center {controlCenterY}");
        }

        [AvaloniaFact]
        public void Label_BaselineAlignsToASlidersValueLabel() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Slider", null, out object? sliderTheme));
            var slider = new Foreman.Mac.Kit.Controls.Slider {
                Theme = Assert.IsType<ControlTheme>(sliderTheme), Width = 124,
                Minimum = 0, Maximum = 100, Value = 72, Unit = "%", ShowValue = true,
            };
            FormRow row = Themed("Link opacity", slider);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            TextBlock valueLabel = slider.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");

            double labelBaselineY = label.TranslatePoint(new Point(0, label.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;
            double valueBaselineY = valueLabel.TranslatePoint(new Point(0, valueLabel.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;

            Assert.Equal(valueBaselineY, labelBaselineY, 2);
        }

        //A control whose visual tree happens to carry a hidden text part earlier than its real, visible one (Kit.Slider's
        //own PART_ValueLabel when ShowValue is off) must not win the baseline search over that visible text.
        [AvaloniaFact]
        public void Label_SkipsAnInvisibleTextDescendant_AligningToTheVisibleOneInstead() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Slider", null, out object? sliderTheme));
            var slider = new Foreman.Mac.Kit.Controls.Slider { Theme = Assert.IsType<ControlTheme>(sliderTheme), Width = 124 };
            //PART_ValueLabel resolves Foreman.Type.Callout.FontSize (12): a visible sibling at a different
            //size proves the search actually skips the hidden part rather than landing on it by coincidence.
            var valueText = new TextBlock { Text = "72 %", FontSize = 20 };
            var opacityRow = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { slider, valueText },
            };
            FormRow row = Themed("Link opacity", opacityRow);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            double labelBaselineY = label.TranslatePoint(new Point(0, label.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;
            double valueBaselineY = valueText.TranslatePoint(new Point(0, valueText.TextLayout!.TextLines[0].Baseline), fixture.Window)!.Value.Y;

            Assert.Equal(valueBaselineY, labelBaselineY, 2);
        }

        [AvaloniaFact]
        public void Label_WhenItWrapsToTwoLines_AlignsItsFirstLineToTheControl_NotClampedToTheRowTop() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("A label long enough to wrap across two full lines at the window label column width", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            Assert.True(label.TextLayout!.TextLines.Count >= 2, "the label needs to actually wrap for this test to be meaningful");
            Assert.IsType<TranslateTransform>(label.RenderTransform);

            Border controlBorder = ControlBorder(row);
            TextLine labelLine = label.TextLayout!.TextLines[0];
            double expectedLocal = (controlBorder.Bounds.Height - labelLine.Height) / 2 + labelLine.Baseline;
            double expectedY = controlBorder.TranslatePoint(new Point(0, expectedLocal), fixture.Window)!.Value.Y;
            double labelBaselineY = label.TranslatePoint(new Point(0, labelLine.Baseline), fixture.Window)!.Value.Y;

            Assert.Equal(expectedY, labelBaselineY, 2);
        }

        [AvaloniaTheory]
        [InlineData(22)]
        [InlineData(26)]
        public void Label_WhenItWraps_ItsTranslatedBottomStaysInsideTheRowsBounds(double contentHeight) {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = contentHeight };
            FormRow row = Themed("A label long enough to wrap across two full lines at the window label column width", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            Assert.True(label.TextLayout!.TextLines.Count >= 2, "the label needs to actually wrap for this test to be meaningful");

            double offset = Assert.IsType<TranslateTransform>(label.RenderTransform).Y;
            double labelBottom = label.Bounds.Y + offset + label.Bounds.Height;

            Assert.True(labelBottom <= row.Bounds.Height + 0.5,
                $"label's translated bottom {labelBottom} escaped the row's bounds {row.Bounds.Height}");
        }

        [AvaloniaFact]
        public void DefaultState_ControlBorderIsTransparentAndNoOtherBorderHasABrush() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border controlBorder = ControlBorder(row);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(controlBorder.BorderBrush).Color);

            Assert.All(row.GetVisualDescendants().OfType<Border>().Where(b => !ReferenceEquals(b, controlBorder)),
                b => Assert.Null(b.BorderBrush));
        }

        [AvaloniaFact]
        public void ControlBorder_SurroundsTheContentByOnePointOnEveryEdge() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("Node width", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border controlBorder = ControlBorder(row);
            Assert.Equal(content.Bounds.Width + 2, controlBorder.Bounds.Width);
            Assert.Equal(content.Bounds.Height + 2, controlBorder.Bounds.Height);

            double contentLeft = content.TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            double borderLeft = controlBorder.TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            Assert.Equal(1, contentLeft - borderLeft, 2);
        }

        [AvaloniaFact]
        public void EnteringErrorState_DoesNotShiftTheControlBordersBounds() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("Beacon count", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Rect before = ControlBorder(row).Bounds;

            row.Error = "Must be 0 or more.";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, ControlBorder(row).Bounds);
        }

        [AvaloniaFact]
        public void EnteringErrorState_DoesNotShiftTheContentLabelOrUnitBounds() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("Beacon count", content);
            row.Unit = "pt";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = LabelText(row);
            TextBlock unit = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");

            Rect contentBefore = content.Bounds;
            Rect labelBefore = label.Bounds;
            Rect unitBefore = unit.Bounds;

            row.Error = "Must be 0 or more.";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(contentBefore, content.Bounds);
            Assert.Equal(labelBefore, label.Bounds);
            Assert.Equal(unitBefore, unit.Bounds);
        }

        [AvaloniaFact]
        public void RowMinHeight_IsControlHeightRegular() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Grid grid = FrameGrid(row);
            Assert.Equal(Assert.IsType<double>(height), grid.RowDefinitions[0].MinHeight);
        }

        [AvaloniaFact]
        public void RowGrowsWithATallerControl() {
            using var fixture = new KitFixture();
            var tall = new Border { Height = 60 };
            FormRow row = Themed("Beacon layout", tall);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Assert.True(row.Bounds.Height >= 60);
        }

        [AvaloniaFact]
        public void Note_UsesCaptionAndTextTertiary_StartsOnTheControlEdge() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Assembler");
            row.Note = "Used for every new node until you change it.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.True(note.IsVisible);
            Assert.Equal("Used for every new node until you change it.", note.Text);
            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal(Assert.IsType<double>(size), note.FontSize);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), note.Foreground?.ToImmutable());

            Panel errorRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_ErrorRow");
            Assert.False(errorRow.IsVisible);
        }

        [AvaloniaFact]
        public void Error_ReplacesTheNote_AndAddsAnErrorBorderToTheControl() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Beacon count");
            row.Note = "This note must disappear once Error is set.";
            row.Error = "Must be 0 or more.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Panel noteRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_NoteRow");
            Panel errorRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_ErrorRow");
            Assert.False(noteRow.IsVisible);
            Assert.True(errorRow.IsVisible);

            TextBlock errorText = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ErrorText");
            Assert.Equal("Must be 0 or more.", errorText.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), errorText.Foreground?.ToImmutable());

            Path errorGlyph = row.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_ErrorGlyph");
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Alert.12"), errorGlyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), errorGlyph.Stroke?.ToImmutable());

            Border controlBorder = ControlBorder(row);
            Assert.Equal(new Thickness(1), controlBorder.BorderThickness);
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), controlBorder.BorderBrush?.ToImmutable());
        }

        //PART_ControlBorder sits on a -1 margin to outline the control from just outside its bounds; a row that
        //self-clips to its own bounds cuts the ring's top edge, since row 0 starts flush at the row's own top.
        [AvaloniaFact]
        public void ErrorBorder_AllFourEdgesRenderInsideTheRow_NoneClippedByTheRowItself() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 80, Height = 22 };
            FormRow row = Themed("Beacon count", content);
            row.Error = "Must be 0 or more.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border ring = ControlBorder(row);
            Point topLeft = ring.TranslatePoint(new Point(0, 0), row) ?? default;
            Point bottomRight = ring.TranslatePoint(new Point(ring.Bounds.Width, ring.Bounds.Height), row) ?? default;

            Assert.False(row.ClipToBounds && topLeft.Y < 0, "the row clips the error ring's top edge.");
            Assert.False(row.ClipToBounds && topLeft.X < 0, "the row clips the error ring's left edge.");
            Assert.False(row.ClipToBounds && bottomRight.X > row.Bounds.Width, "the row clips the error ring's right edge.");
            Assert.False(row.ClipToBounds && bottomRight.Y > row.Bounds.Height, "the row clips the error ring's bottom edge.");
        }

        [AvaloniaFact]
        public void NoteIsValue_RendersTextSecondary_NotTertiary() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Building");
            row.NoteIsValue = true;
            row.Note = "Assembling machine 3";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), note.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void NoteSuffix_RendersAsASecondTextTertiaryRunAfterAMiddleDot() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Building");
            row.NoteIsValue = true;
            row.Note = "Assembling machine 3";
            row.NoteSuffix = "Speed 125 % · Prod 10 % · Pollution 2.4/m";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            TextBlock suffix = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NoteSuffix");
            Assert.Equal("Assembling machine 3", note.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), note.Foreground?.ToImmutable());
            Assert.True(suffix.IsVisible);
            Assert.Equal(" · Speed 125 % · Prod 10 % · Pollution 2.4/m", suffix.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), suffix.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void NoErrorIsWarning_ErrorRendersInErrorColour_Unchanged() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Beacon count");
            row.Error = "Must be 0 or more.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock errorText = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ErrorText");
            Path errorGlyph = row.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_ErrorGlyph");
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), errorText.Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), errorGlyph.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void ErrorIsWarning_RendersInWarningColour_NotError() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Building");
            row.ErrorIsWarning = true;
            row.Error = "No enabled building crafts this recipe.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock errorText = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ErrorText");
            Path errorGlyph = row.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_ErrorGlyph");
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), errorText.Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), errorGlyph.Stroke?.ToImmutable());
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Alert.12"), errorGlyph.Data);
        }

        [AvaloniaFact]
        public void NoteIsWarning_OverridesValue_RendersInWarningColour() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Building");
            row.NoteIsValue = true;
            row.NoteIsWarning = true;
            row.Note = "Assembling machine 2";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), note.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void TwoRowsInTheSameShape_ResolveTheSameControlColumnX() {
            using var fixture = new KitFixture();
            FormRow first = Themed("Node width");
            FormRow second = Themed("Link opacity");
            var stack = new StackPanel { Children = { first, second } };
            fixture.Window.Content = stack;
            Dispatcher.UIThread.RunJobs();

            double firstX = ControlHost(first).TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            double secondX = ControlHost(second).TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            Assert.Equal(firstX, secondX);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;

            row.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), row.Opacity);
        }

        [AvaloniaFact]
        public void NoteAndErrorRows_CrossFadeAtTheTabLabelCrossFadeDuration() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Assembler");
            row.Note = "value";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Panel noteRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_NoteRow");
            Panel errorRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_ErrorRow");

            var noteTransition = Assert.Single(noteRow.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.TabLabelCrossFadeDuration, noteTransition.Duration);
            var errorTransition = Assert.Single(errorRow.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.TabLabelCrossFadeDuration, errorTransition.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_NoteAndErrorRows_StayOpacityOnlyWithCrossFadeEasing() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            FormRow row = Themed("Assembler");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Panel noteRow = row.GetVisualDescendants().OfType<Panel>().Single(s => s.Name == "PART_NoteRow");
            Assert.DoesNotContain(noteRow.Transitions!, t => t is not DoubleTransition);
            var opacity = Assert.Single(noteRow.Transitions!.OfType<DoubleTransition>());
            Assert.IsType<CubicEaseInOut>(opacity.Easing);
        }

        [AvaloniaFact]
        public void Transitions_AnimateHeightAndOpacityAtTheInspectorReflowDuration() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            var height = Assert.Single(row.Transitions!.OfType<DoubleTransition>(), t => t.Property == Layoutable.HeightProperty);
            var opacity = Assert.Single(row.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.InspectorReflowDuration, height.Duration);
            Assert.Equal(Motion.InspectorReflowDuration, opacity.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_RowReflow_CollapsesToOpacityOnly() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(row.Transitions!, t => t is not DoubleTransition);
            var opacity = Assert.Single(row.Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
            Assert.Equal(Motion.InspectorReflowDuration, opacity.Duration);
        }

        [AvaloniaFact]
        public void AddingANote_AnimatesHeightFromTheOldValueToTheNewMeasuredValue_ThenClearsTheExplicitHeight() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Assembler");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            double before = row.Bounds.Height;
            Assert.True(double.IsNaN(row.Height));

            int layouts = 0;
            row.LayoutUpdated += (_, _) => layouts++;

            row.Note = "Used for every new node until you change it.";
            Dispatcher.UIThread.RunJobs();

            Assert.False(double.IsNaN(row.Height));
            Assert.True(row.Height >= before);

            AnimationClock.Settle((int)Motion.InspectorReflowDuration.TotalMilliseconds + 100);

            Assert.True(double.IsNaN(row.Height));
            Assert.True(row.Bounds.Height > before);
            Assert.True(layouts <= 40, $"FormRow laid out {layouts} times settling one note reflow.");
        }

        //Direct unit coverage of the shared helper (FormRow.cs and PalettePanel.cs both call it), independent
        //of either composite's own measure logic.
        [AvaloniaFact]
        public void ReflowHeight_SharedHelper_ClearsTheExplicitHeightOnCompletion() {
            using var fixture = new KitFixture();
            var host = new Border { MinHeight = 40 };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Assert.True(double.IsNaN(host.Height));
            double before = host.Bounds.Height;

            CancellationTokenSource? cts = null;
            CompositeMotion.ReflowHeight(host, () => 80, () => CompositeMotion.ClearExplicitHeight(host, ref cts), ref cts);
            Dispatcher.UIThread.RunJobs();

            Assert.False(double.IsNaN(host.Height));

            AnimationClock.Settle((int)Motion.InspectorReflowDuration.TotalMilliseconds + 100);

            Assert.True(double.IsNaN(host.Height));
            Assert.True(host.Bounds.Height >= before);
        }

        //The page names Motion.EntranceEasing for the grid<->pop-up reflow. Sampling the running animation
        //off the render clock cannot tell one curve from another under headless jitter, so the curve itself
        //is sampled: a cubic ease-out stands at 0.578125 a quarter in and 0.984375 three quarters in, where
        //a linear tween stands at 0.25 and 0.75.
        [AvaloniaFact]
        public void ReflowAnimation_RunsTheEntranceCurve_NotALinearTween() {
            using var fixture = new KitFixture();

            Animation reflow = CompositeMotion.BuildReflowAnimation(40, 80);

            Assert.Equal(Motion.InspectorReflowDuration, reflow.Duration);
            Assert.Equal(0.578125, reflow.Easing.Ease(0.25), 6);
            Assert.Equal(0.984375, reflow.Easing.Ease(0.75), 6);
            Assert.Equal(Motion.EntranceEasing.Ease(0.25), reflow.Easing.Ease(0.25), 6);
            Assert.Equal(Motion.EntranceEasing.Ease(0.75), reflow.Easing.Ease(0.75), 6);
        }

        //FormRow and PalettePanel both inlined this exact block in their detach handlers; the reflow
        //continuation must go through the same helper, not just the detach path.
        [AvaloniaFact]
        public void ClearExplicitHeight_SharedHelper_RestoresAutoHeight_AndDropsTheCancellation() {
            using var fixture = new KitFixture();
            var host = new Border { MinHeight = 40 };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            CancellationTokenSource? cts = null;
            CompositeMotion.ReflowHeight(host, () => 80, () => CompositeMotion.ClearExplicitHeight(host, ref cts), ref cts);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(cts);

            AnimationClock.Settle((int)Motion.InspectorReflowDuration.TotalMilliseconds + 100);

            Assert.True(double.IsNaN(host.Height));
            Assert.Null(cts);
        }

        [AvaloniaFact]
        public void ReduceMotion_AddingANote_NeverDrivesAnExplicitHeight() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            FormRow row = Themed("Assembler");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            row.Note = "Used for every new node until you change it.";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.True(double.IsNaN(row.Height));
        }

        [AvaloniaFact]
        public void Unit_RendersAfterTheControlInCalloutTextSecondary() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            row.Unit = "pt";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock unit = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");
            Assert.True(unit.IsVisible);
            Assert.Equal("pt", unit.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), unit.Foreground?.ToImmutable());

            Panel controlHost = ControlHost(row);
            double controlRight = controlHost.TranslatePoint(new Point(controlHost.Bounds.Width, 0), fixture.Window)!.Value.X;
            double unitLeft = unit.TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            Application.Current!.TryGetResource("Foreman.Space.Space2", Application.Current.ActualThemeVariant, out object? space2);
            Assert.Equal(Assert.IsType<double>(space2), unitLeft - controlRight, 2);

            Grid grid = FrameGrid(row);
            double controlColumnOriginX = grid.TranslatePoint(
                new Point(grid.ColumnDefinitions[0].Width.Value + grid.ColumnDefinitions[1].Width.Value, 0), fixture.Window)!.Value.X;
            Assert.True(unitLeft > controlColumnOriginX);
        }

        [AvaloniaFact]
        public void Unit_IsNotVisibleWhenUnset() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Node width");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock unit = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");
            Assert.False(unit.IsVisible);
        }

        [AvaloniaFact]
        public void ClickingTheLabel_FocusesTheContentControl() {
            using var fixture = new KitFixture();
            var content = new TextBox();
            FormRow row = Themed("Beacon count", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(LabelText(row)).Click();

            Assert.True(content.IsFocused);
        }

        [AvaloniaFact]
        public void VoiceOver_LabelNamesTheContentControl() {
            using var fixture = new KitFixture();
            var content = new TextBox();
            FormRow row = Themed("Beacon count", content);
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(content);
            Assert.Equal("Beacon count", peer.GetName());
        }

        [AvaloniaFact]
        public void VoiceOver_NoteBecomesHelpTextUnlessThereIsAnError() {
            using var fixture = new KitFixture();
            var content = new TextBox();
            FormRow row = Themed("Beacon count", content);
            row.Note = "Must be 0 or more.";
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(content);
            Assert.Equal("Must be 0 or more.", peer.GetHelpText());

            row.Error = "Invalid value.";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Invalid value.", peer.GetHelpText());
        }

        [AvaloniaFact]
        public void ReflowsSettled_StaysPendingWhileANoteReflowRuns_AndCompletesOnceTheRowIsBackToAutoHeight() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Assembler");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            row.Note = "Used for every new node until you change it.";
            Assert.False(double.IsNaN(row.Height));
            Assert.False(Motion.ReflowsSettled(fixture.Window).IsCompleted);

            AnimationClock.UntilReflowsSettled(fixture.Window);

            Assert.True(Motion.ReflowsSettled(fixture.Window).IsCompleted);
            Assert.True(double.IsNaN(row.Height));
        }

        [AvaloniaFact]
        public void ReflowsSettled_Completes_WhenTheReflowingRowDetachesMidReflow() {
            using var fixture = new KitFixture();
            FormRow row = Themed("Assembler");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            row.Note = "Used for every new node until you change it.";
            Assert.False(Motion.ReflowsSettled(row).IsCompleted);

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Motion.ReflowsSettled(row).IsCompleted);
            Assert.True(double.IsNaN(row.Height));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitFormRowScaleTests {
        private static Grid FrameGrid(FormRow row) =>
            row.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");

        private static Panel ControlHost(FormRow row) =>
            row.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_ControlHost");

        [AvaloniaFact]
        public void LabelColumnAndRowMinHeight_TrackScaleContract_GutterStaysFixed() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            var row = new FormRow { Theme = (ControlTheme)theme!, Label = "Node width", Content = new TextBlock { Text = "value" } };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(132, grid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(12, grid.ColumnDefinitions[1].Width.Value);
            Assert.Equal(22, grid.RowDefinitions[0].MinHeight);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(198, grid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(12, grid.ColumnDefinitions[1].Width.Value);
            Assert.Equal(33, grid.RowDefinitions[0].MinHeight);
        }

        [AvaloniaFact]
        public void Unit_GapTracksSpace2AtOneAndAHalfTimesScale() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            var row = new FormRow { Theme = (ControlTheme)theme!, Label = "Node width", Unit = "pt", Content = new TextBlock { Text = "value" } };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Panel controlHost = ControlHost(row);
            TextBlock unit = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");

            Application.Current!.TryGetResource("Foreman.Space.Space2", Application.Current.ActualThemeVariant, out object? space2);
            double controlRight = controlHost.TranslatePoint(new Point(controlHost.Bounds.Width, 0), fixture.Window)!.Value.X;
            double unitLeft = unit.TranslatePoint(new Point(0, 0), fixture.Window)!.Value.X;
            Assert.Equal(Assert.IsType<double>(space2), unitLeft - controlRight, 2);
        }

        [AvaloniaFact]
        public void InspectorLabelColumn_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            var row = new FormRow { Theme = (ControlTheme)theme!, Label = "Quality", LabelColumn = FormRowLabelColumn.Inspector, Content = new TextBlock { Text = "value" } };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(108, grid.ColumnDefinitions[0].Width.Value);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(162, grid.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void PopoverLabelColumn_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            var row = new FormRow { Theme = (ControlTheme)theme!, Label = "Show", LabelColumn = FormRowLabelColumn.Popover, Content = new TextBlock { Text = "value" } };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Grid grid = FrameGrid(row);
            Assert.Equal(86, grid.ColumnDefinitions[0].Width.Value);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(129, grid.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void NoteLineHeight_Tracks135TimesCaptionFontSize_AtEveryScaleStep() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormRow", null, out object? theme));
            var row = new FormRow { Theme = (ControlTheme)theme!, Label = "Assembler", Note = "value", Content = new TextBlock { Text = "value" } };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? baseSize);
            Assert.Equal((double)baseSize! * 1.35, note.LineHeight);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? scaledSize);
            Assert.Equal((double)scaledSize! * 1.35, note.LineHeight);
        }

        private static ControlTheme KitTheme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static FormRow Themed(string label, object content) =>
            new() { Theme = KitTheme("Kit.FormRow"), Label = label, Content = content };

        private static double Resolved(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<double>(value);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void ControlColumn_AStretchingFieldFillsToTheGroupsInnerTrailingEdge_NaturalWidthControlsKeepTheirs(double scale) {
            using var fixture = new KitFixture();
            ScaleContract.Apply(scale);
            fixture.Window.Width = 900;
            var field = new TextBox { Theme = KitTheme("Kit.TextField"), HorizontalAlignment = HorizontalAlignment.Stretch, Text = "/Users/me/Documents" };
            var popUp = new Foreman.Mac.Kit.Controls.PopUp { Theme = KitTheme("Kit.PopUp") };
            popUp.Items.Add("1");
            popUp.Items.Add("1/20");
            popUp.SelectedItem = "1";
            var stepper = new Foreman.Mac.Kit.Controls.Stepper { Theme = KitTheme("Kit.Stepper"), Value = 3 };
            var segmented = new Foreman.Mac.Kit.Controls.Segmented { Theme = KitTheme("Kit.Segmented") };
            segmented.Items.Add("Auto");
            segmented.Items.Add("Fixed");
            var group = new FormGroup { Theme = KitTheme("Kit.FormGroup"), IsBoxed = true, Width = 360 * scale };
            group.Items.Add(Themed("Save to", field));
            group.Items.Add(Themed("Scale", popUp));
            group.Items.Add(Themed("Width", stepper));
            group.Items.Add(Themed("Mode", segmented));
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border frame = group.GetVisualChildren().OfType<Border>().Single(b => b.Name == "PART_Frame");
            double innerRight = frame.Bounds.Width - frame.BorderThickness.Right - frame.Padding.Right;
            double fieldRight = field.TranslatePoint(new Point(field.Bounds.Width, 0), frame)!.Value.X;
            Assert.Equal(innerRight, fieldRight, 0.5);

            double fieldLeft = field.TranslatePoint(new Point(0, 0), frame)!.Value.X;
            Assert.Equal(fieldLeft, popUp.TranslatePoint(new Point(0, 0), frame)!.Value.X, 0.5);
            Assert.Equal(popUp.DesiredSize.Width, popUp.Bounds.Width, 0.5);
            Assert.True(popUp.Bounds.Width < fieldRight - fieldLeft);
            Assert.Equal(fieldLeft, stepper.TranslatePoint(new Point(0, 0), frame)!.Value.X, 0.5);
            Assert.Equal(stepper.DesiredSize.Width, stepper.Bounds.Width, 0.5);
            Assert.Equal(fieldLeft, segmented.TranslatePoint(new Point(0, 0), frame)!.Value.X, 0.5);
            Assert.Equal(segmented.DesiredSize.Width, segmented.Bounds.Width, 0.5);
            Assert.True(segmented.Bounds.Width < fieldRight - fieldLeft);

            int layouts = 0;
            group.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);
            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void ControlColumn_FollowsTheControlsAlignmentWhenItChanges() {
            using var fixture = new KitFixture();
            var field = new TextBox { Theme = KitTheme("Kit.TextField"), HorizontalAlignment = HorizontalAlignment.Left, Text = "a" };
            FormRow row = Themed("Save to", field);
            fixture.Window.Content = new StackPanel { Width = 360, Children = { row } };
            Dispatcher.UIThread.RunJobs();
            Grid grid = FrameGrid(row);
            Assert.True(field.TranslatePoint(new Point(field.Bounds.Width, 0), grid)!.Value.X < grid.Bounds.Width - 1);

            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(grid.Bounds.Width, field.TranslatePoint(new Point(field.Bounds.Width, 0), grid)!.Value.X, 0.5);

            field.Width = 100;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, field.Bounds.Width, 0.5);
            Assert.Equal(grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[1].ActualWidth, field.TranslatePoint(new Point(0, 0), grid)!.Value.X, 0.5);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void NoteAndError_LongerThanTheControlColumn_WrapWithinIt(double scale) {
            using var fixture = new KitFixture();
            ScaleContract.Apply(scale);
            const string longText = "Language, mods, and mod settings come from Factorio. Change them there before importing.";
            FormRow noteRow = Themed("Factorio install", new TextBox { Theme = KitTheme("Kit.TextField") });
            noteRow.Note = longText;
            FormRow errorRow = Themed("Mods folder", new TextBox { Theme = KitTheme("Kit.TextField") });
            errorRow.Error = longText;
            fixture.Window.Content = new StackPanel { Width = 420 * scale, Children = { noteRow, errorRow } };
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            TextBlock note = noteRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.True(note.TextLayout.TextLines.Count >= 2);
            Assert.True(note.TranslatePoint(new Point(note.Bounds.Width, 0), noteRow)!.Value.X <= FrameGrid(noteRow).Bounds.Right + 0.5);

            TextBlock error = errorRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ErrorText");
            Assert.True(error.TextLayout.TextLines.Count >= 2);
            Assert.True(error.TranslatePoint(new Point(error.Bounds.Width, 0), errorRow)!.Value.X <= FrameGrid(errorRow).Bounds.Right + 0.5);
            Assert.Equal(Resolved("Foreman.Type.Caption.FontSize"), note.FontSize);
        }
    }
}
