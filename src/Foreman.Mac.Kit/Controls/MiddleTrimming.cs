using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Controls {
    public sealed class MiddleTrimming : TextTrimming {
        private const string Ellipsis = "…";

        public static MiddleTrimming Instance { get; } = new();

        public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo createInfo) =>
            new MiddleCollapsing(createInfo.Width, createInfo.TextRunProperties, createInfo.FlowDirection);

        public override string ToString() => "MiddleEllipsis";

        private sealed class MiddleCollapsing : TextCollapsingProperties {
            public MiddleCollapsing(double width, TextRunProperties properties, FlowDirection flowDirection) {
                Width = width;
                FlowDirection = flowDirection;
                Symbol = new TextCharacters(Ellipsis, properties);
            }

            public override double Width { get; }
            public override TextRun Symbol { get; }
            public override FlowDirection FlowDirection { get; }

            public override TextRun[]? Collapse(TextLine textLine) {
                List<ShapedTextRun> source = [.. textLine.TextRuns.OfType<ShapedTextRun>()];
                if (Advance(source) <= Width)
                    return null;

                ShapedTextRun symbol = TextFormatter.CreateSymbol(Symbol, FlowDirection);
                if (symbol.GlyphRun.Bounds.Width > Width)
                    return [];

                string full = string.Concat(source.Select(r => r.Text.ToString()));
                double budget = Width - symbol.Size.Width;
                int low = 0;
                int high = full.Length;
                while (low < high) {
                    int keep = (low + high + 1) / 2;
                    (int head, int tail) = Split(full, keep);
                    List<ShapedTextRun> probeHead = Take(source, 0, head, null);
                    List<ShapedTextRun> probeTail = Take(source, full.Length - tail, full.Length, null);
                    bool fits = budget >= Advance(probeHead) + Advance(probeTail);
                    Release(probeHead, source);
                    Release(probeTail, source);
                    if (fits)
                        low = keep;
                    else
                        high = keep - 1;
                }

                (int headLength, int tailLength) = Split(full, low);
                var split = new HashSet<ShapedTextRun>();
                var runs = new List<TextRun>();
                runs.AddRange(Take(source, 0, headLength, split));
                runs.Add(symbol);
                runs.AddRange(Take(source, full.Length - tailLength, full.Length, split));
                foreach (ShapedTextRun run in split)
                    run.Dispose();
                return [.. runs];
            }

            private static void Release(List<ShapedTextRun> slices, List<ShapedTextRun> source) {
                foreach (ShapedTextRun slice in slices)
                    if (!source.Contains(slice))
                        slice.Dispose();
            }

            private static double Advance(IEnumerable<ShapedTextRun> runs) {
                double width = 0;
                foreach (ShapedTextRun run in runs)
                    foreach (GlyphInfo glyph in run.ShapedBuffer)
                        width += glyph.GlyphAdvance;
                return width;
            }

            private static List<ShapedTextRun> Take(List<ShapedTextRun> source, int start, int end, HashSet<ShapedTextRun>? split) {
                var slices = new List<ShapedTextRun>();
                int offset = 0;
                foreach (ShapedTextRun run in source) {
                    int from = Math.Max(start, offset) - offset;
                    int to = Math.Min(end, offset + run.Length) - offset;
                    offset += run.Length;
                    if (to <= from)
                        continue;
                    if (from == 0 && to == run.Length) {
                        slices.Add(run);
                        continue;
                    }
                    ShapedBuffer origin = run.ShapedBuffer;
                    ShapedBuffer? buffer = origin;
                    if (from > 0) {
                        (ShapedBuffer? before, ShapedBuffer? after) = buffer.Split(from);
                        Discard(before, origin);
                        buffer = after;
                    }
                    if (buffer is not null && to - from < buffer.Text.Length) {
                        (ShapedBuffer? inside, ShapedBuffer? outside) = buffer.Split(to - from);
                        Discard(outside, origin);
                        if (!ReferenceEquals(inside, buffer))
                            Discard(buffer, origin);
                        buffer = inside;
                    }
                    if (buffer is null)
                        continue;
                    if (ReferenceEquals(buffer, origin)) {
                        slices.Add(run);
                        continue;
                    }
                    split?.Add(run);
                    slices.Add(new ShapedTextRun(buffer, run.Properties));
                }
                return slices;
            }

            private static void Discard(ShapedBuffer? buffer, ShapedBuffer origin) {
                if (buffer is not null && !ReferenceEquals(buffer, origin))
                    buffer.Dispose();
            }

            private static (int Head, int Tail) Split(string full, int keep) {
                int head = (keep + 1) / 2;
                int tail = keep / 2;
                if (tail > 0 && tail < full.Length && char.IsLowSurrogate(full[^tail]))
                    tail--;
                return (head, tail);
            }
        }
    }
}
