using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class TextTests {
        private static ControlTheme KitTextTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static TextBlock Themed(string text, params string[] classes) {
            var block = new TextBlock { Theme = KitTextTheme(), Text = text };
            foreach (string cls in classes)
                block.Classes.Add(cls);
            return block;
        }

        [AvaloniaFact]
        public void KitText_IsAControlThemeTargetingTextBlock() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(TextBlock), KitTextTheme().TargetType);
        }

        [AvaloniaFact]
        public void NoRampClass_DefaultsForegroundToTextToken() {
            using var fixture = new KitFixture();
            var block = Themed("Iron Plate");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.Text")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }

        public static IEnumerable<object[]> RampSteps() {
            yield return ["title", "Foreman.Type.LargeTitle.FontSize", "Foreman.Type.LargeTitle.FontWeight"];
            yield return ["headline", "Foreman.Type.EmphasizedBody.FontSize", "Foreman.Type.EmphasizedBody.FontWeight"];
            yield return ["body", "Foreman.Type.Body.FontSize", "Foreman.Type.Body.FontWeight"];
            yield return ["callout", "Foreman.Type.Callout.FontSize", "Foreman.Type.Callout.FontWeight"];
            yield return ["caption", "Foreman.Type.Caption.FontSize", "Foreman.Type.Caption.FontWeight"];
        }

        [AvaloniaTheory]
        [MemberData(nameof(RampSteps))]
        public void RampClass_SetsFontSizeAndWeightFromItsStepToken(string rampClass, string sizeKey, string weightKey) {
            using var fixture = new KitFixture();
            var block = Themed("Iron Plate Line", rampClass);
            fixture.Window.Content = block;

            Application.Current!.TryGetResource(sizeKey, Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource(weightKey, Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), block.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), block.FontWeight);
        }

        [AvaloniaFact]
        public void SecondaryClass_SetsForegroundToTextSecondaryToken() {
            using var fixture = new KitFixture();
            var block = Themed("v 1.1.0u", "caption", "secondary");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextSecondary")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }

        [AvaloniaFact]
        public void TertiaryClass_SetsForegroundToTextTertiaryToken() {
            using var fixture = new KitFixture();
            var block = Themed("AppSettings.DefaultNodeDirection", "caption", "tertiary");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextTertiary")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }
    }

    public class TextMiddleEllipsisTests {
        private const string Name = "Factorio 2.0 Space Age Modified Extended Edition 2026";

        private static TextBlock Trimmed(string text, double width) {
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var block = new TextBlock { Theme = Assert.IsType<ControlTheme>(theme), Text = text, Width = width };
            block.Classes.Add("middle-ellipsis");
            return block;
        }

        private static string Shown(TextBlock block) =>
            string.Concat(block.TextLayout.TextLines.SelectMany(l => l.TextRuns).Select(r => r.Text.ToString()));

        private static double NaturalWidth(TextBlock block) {
            var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
            using var layout = new TextLayout(block.Text, typeface, block.FontSize, null, TextAlignment.Left, TextWrapping.NoWrap,
                TextTrimming.None, null, FlowDirection.LeftToRight, double.PositiveInfinity, double.PositiveInfinity);
            return layout.WidthIncludingTrailingWhitespace;
        }

        [AvaloniaFact]
        public void MiddleEllipsisClass_SetsTheMiddleTrimming() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 100);
            fixture.Window.Content = block;

            Assert.IsType<MiddleTrimming>(block.TextTrimming);
        }

        [AvaloniaFact]
        public void Overflowing_KeepsTheHeadAndTheTailAroundOneEllipsis_TextStaysWhole() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 0);
            fixture.Window.Content = block;
            block.Width = NaturalWidth(block) / 2;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, block.Width, 100));

            string shown = Shown(block);

            Assert.Equal(Name, block.Text);
            Assert.Equal(1, shown.Count(c => c == '…'));
            Assert.StartsWith("Fact", shown, StringComparison.Ordinal);
            Assert.EndsWith("2026", shown, StringComparison.Ordinal);
            Assert.True(shown.Length < Name.Length);
            Assert.True(block.TextLayout.TextLines[0].Width <= block.Width + 0.01);
        }

        [AvaloniaFact]
        public void Overflowing_HeadAndTailDifferByAtMostOneCharacter() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 0);
            fixture.Window.Content = block;
            block.Width = NaturalWidth(block) / 2;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, block.Width, 100));

            string[] parts = Shown(block).Split('…');

            Assert.InRange(parts[0].Length - parts[1].Length, 0, 1);
            Assert.StartsWith(parts[0], Name, StringComparison.Ordinal);
            Assert.EndsWith(parts[1], Name, StringComparison.Ordinal);
        }

        [AvaloniaFact]
        public void Overflowing_InlineRuns_KeepTheirOwnWeightOnEachSide() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var block = new TextBlock { Theme = Assert.IsType<ControlTheme>(theme) };
            block.Classes.Add("middle-ellipsis");
            block.Inlines!.Add(new Run("Factorio 2.0 "));
            block.Inlines.Add(new Run("Space Age Modified Extended Edition 2026") { FontWeight = FontWeight.Bold });
            fixture.Window.Content = block;
            block.Measure(new Size(10000, 100));
            double natural = block.DesiredSize.Width;
            block.Width = natural / 2;
            block.Measure(new Size(10000, 100));
            block.Arrange(new Rect(0, 0, block.Width, 100));

            List<ShapedTextRun> runs = [.. block.TextLayout.TextLines[0].TextRuns.OfType<ShapedTextRun>()];

            Assert.EndsWith("2026", Shown(block), StringComparison.Ordinal);
            Assert.Equal(FontWeight.Normal, runs[0].Properties.Typeface.Weight);
            Assert.Equal(FontWeight.Bold, runs[^1].Properties.Typeface.Weight);
        }

        [AvaloniaFact]
        public void Overflowing_KeepsAsManyCharactersAsFit() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 0);
            fixture.Window.Content = block;
            block.Width = NaturalWidth(block) / 2;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, block.Width, 100));

            string[] parts = Shown(block).Split('…');
            int head = parts[0].Length;
            int tail = parts[1].Length;
            int moreHead = head == tail ? head + 1 : head;
            int moreTail = head == tail ? tail : tail + 1;
            string wider = Name[..moreHead] + "…" + Name[^moreTail..];
            var probe = new TextBlock { Text = wider, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight };

            Assert.True(head + tail > 8);
            Assert.True(NaturalWidth(probe) > block.Width);
        }

        [AvaloniaFact]
        public void Overflowing_NeverSplitsASurrogatePair() {
            using var fixture = new KitFixture();
            const string astral = "Ab\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600Yz\U0001F600\U0001F600\U0001F600\U0001F600";
            TextBlock block = Trimmed(astral, 0);
            fixture.Window.Content = block;
            block.Width = 1000;
            block.Measure(new Size(1000, 100));
            double natural = NaturalWidth(block);

            for (double width = 18; width < natural; width += 3) {
                block.Width = width;
                block.InvalidateMeasure();
                block.Measure(new Size(1000, 100));
                block.Arrange(new Rect(0, 0, width, 100));
                string shown = Shown(block);
                for (int i = 0; i < shown.Length; i++) {
                    if (char.IsHighSurrogate(shown[i]))
                        Assert.True(i + 1 < shown.Length && char.IsLowSurrogate(shown[i + 1]), $"width {width}");
                    if (char.IsLowSurrogate(shown[i]))
                        Assert.True(i > 0 && char.IsHighSurrogate(shown[i - 1]), $"width {width}");
                }
            }
        }

        [AvaloniaFact]
        public void Overflowing_ADecomposedLetterAtTheEnd_NeverStartsTheTailOnItsMark() {
            using var fixture = new KitFixture();
            const string decomposed = "Factorio Cafe\u0301";
            TextBlock block = Trimmed(decomposed, 0);
            fixture.Window.Content = block;
            block.Width = 1000;
            block.Measure(new Size(1000, 100));
            double natural = NaturalWidth(block);

            for (double width = 1; width < natural; width += 1) {
                block.Width = width;
                block.InvalidateMeasure();
                block.Measure(new Size(1000, 100));
                block.Arrange(new Rect(0, 0, width, 100));
                string shown = Shown(block);
                int cut = shown.IndexOf('…');
                if (cut < 0)
                    continue;
                string tail = shown[(cut + 1)..];
                Assert.EndsWith(tail, decomposed, StringComparison.Ordinal);
                Assert.False(tail.StartsWith('\u0301'), $"width {width}");
            }
        }

        [AvaloniaFact]
        public void Collapsing_DisposesTheSplitSourceRunAndKeepsTheReturnedOnes() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var block = new TextBlock { Theme = Assert.IsType<ControlTheme>(theme), Text = Name };
            fixture.Window.Content = block;
            var properties = new GenericTextRunProperties(
                new Typeface(block.FontFamily), block.FontSize, null, Brushes.Black);
            var paragraph = new LineProperties(properties);
            TextLine line = TextFormatter.Current.FormatLine(new SingleRunSource(Name, properties), 0, double.PositiveInfinity, paragraph)!;
            ShapedTextRun source = line.TextRuns.OfType<ShapedTextRun>().Single();
            int glyphs = source.ShapedBuffer.Length;

            TextLine collapsed = line.Collapse(MiddleTrimming.Instance.CreateCollapsingProperties(
                new TextCollapsingCreateInfo(line.Width / 2, properties, FlowDirection.LeftToRight)));

            List<ShapedTextRun> kept = [.. collapsed.TextRuns.OfType<ShapedTextRun>()];
            Assert.True(glyphs > 0);
            Assert.Equal(0, source.ShapedBuffer.Length);
            Assert.True(kept[0].ShapedBuffer.Length > 0);
            Assert.True(kept[^1].ShapedBuffer.Length > 0);
            Assert.StartsWith("Fact", kept[0].Text.ToString(), StringComparison.Ordinal);
            Assert.EndsWith("2026", kept[^1].Text.ToString(), StringComparison.Ordinal);
        }

        private sealed class SingleRunSource(string text, TextRunProperties properties) : ITextSource {
            public TextRun? GetTextRun(int textSourceIndex) =>
                textSourceIndex < text.Length
                    ? new TextCharacters(text[textSourceIndex..], properties)
                    : new TextEndOfParagraph();
        }

        private sealed class LineProperties(TextRunProperties properties) : TextParagraphProperties {
            public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
            public override TextAlignment TextAlignment => TextAlignment.Left;
            public override double LineHeight => double.NaN;
            public override bool FirstLineInParagraph => true;
            public override TextRunProperties DefaultTextRunProperties => properties;
            public override TextWrapping TextWrapping => TextWrapping.NoWrap;
            public override double Indent => 0;
        }

        [AvaloniaFact]
        public void Fitting_ShowsTheWholeText() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed("Vanilla", 0);
            fixture.Window.Content = block;
            block.Width = NaturalWidth(block) + 10;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, block.Width, 100));

            Assert.Equal("Vanilla", Shown(block));
        }

        [AvaloniaFact]
        public void NarrowerThanTheEllipsis_ShowsNoText() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 1);
            fixture.Window.Content = block;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, 1, 100));

            Assert.DoesNotContain("F", Shown(block));
        }

        [AvaloniaFact]
        public void PeerName_IsTheFullText() {
            using var fixture = new KitFixture();
            TextBlock block = Trimmed(Name, 100);
            fixture.Window.Content = block;
            block.Measure(new Size(1000, 100));
            block.Arrange(new Rect(0, 0, 100, 100));

            Assert.Equal(Name, ControlAutomationPeer.CreatePeerForElement(block).GetName());
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class TextScaleTests {
        [AvaloniaFact]
        public void TitleClass_FontSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var block = new TextBlock { Theme = Assert.IsType<ControlTheme>(theme), Text = "Graph Summary" };
            block.Classes.Add("title");
            fixture.Window.Content = block;

            ScaleContract.Apply(1.5);

            Assert.Equal(30.0, block.FontSize);
        }
    }
}
