using SkiaSharp;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas.KitSkia {
    public static class GlyphPaths {
        private static readonly Dictionary<string, string> Masters = new() {
            ["Kit.Glyph.Node.Over"] = "M10,16 V4.5 M5.5,9 L10,4.5 L14.5,9",
            ["Kit.Glyph.Node.Short"] = "M10,4 V15.5 M5.5,11 L10,15.5 L14.5,11",
            ["Kit.Glyph.Node.Error"] = "M10,4 V11.5 M10,15.6 V15.7",
            ["Kit.Glyph.Node.Key"] = "M2.8,10 A3.2,3.2 0 1,0 9.2,10 A3.2,3.2 0 1,0 2.8,10 Z M9.2,10 H17 M14,10 V13 M16.5,10 V12.5",
            ["Kit.Glyph.Canvas.Paused"] = "M7.5,5 V15 M12.5,5 V15",
            ["Kit.Glyph.Link.Orthogonal"] = "M3,15 H9 V5 H17 M7.2,10 A1.8,1.8 0 1,0 10.8,10 A1.8,1.8 0 1,0 7.2,10 Z",
            ["Kit.Glyph.Link.Rounded"] = "M3,15 C9,15 11,5 17,5",
            ["Kit.Glyph.Beacon"] = "M10,4 v3 M6,9 h8 M7,9 l-2,7 h10 l-2,-7",
            ["Kit.Glyph.Beacon.Fill"] = "M11.2,2.6 A1.2,1.2 0 1,0 8.8,2.6 A1.2,1.2 0 1,0 11.2,2.6 Z",
            ["Kit.Glyph.RadialSpoil"] = "M9.5,2 V11 M5,7 L9.5,11 L14,7 M4,14 Q7,12 9.5,14 Q12,16 15,14",
            ["Kit.Glyph.RadialPlant"] = "M9.5,17 V8 M9.5,10 Q5,9 4,4 M9.5,10 Q14,9 15,4",
        };

        private static readonly Dictionary<string, string> BadgeMasters = new() {
            ["Kit.Glyph.Node.Over"] = "M5,8 V2.25 M2.75,4.5 L5,2.25 L7.25,4.5",
            ["Kit.Glyph.Node.Short"] = "M5,2 V7.75 M2.75,5.5 L5,7.75 L7.25,5.5",
            ["Kit.Glyph.Node.Error"] = "M5,2 V5.75 M5,7.8 V7.85",
            ["Kit.Glyph.Node.Key"] = "M1.4,5 A1.6,1.6 0 1,0 4.6,5 A1.6,1.6 0 1,0 1.4,5 Z M4.6,5 H8.5 M7,5 V6.5 M8.25,5 V6.25",
        };

        private static readonly ConcurrentDictionary<string, SKPath> Parsed = new();
        private static readonly ConcurrentDictionary<string, SKPath> ParsedBadges = new();

        //The returned path is shared; callers draw it and never mutate it.
        public static SKPath Get(string key) => Parsed.GetOrAdd(key, k => SKPath.ParseSvgPathData(Masters[k]));

        public static SKPath GetBadge(string key) => ParsedBadges.GetOrAdd(key, k => SKPath.ParseSvgPathData(BadgeMasters[k]));
    }
}
