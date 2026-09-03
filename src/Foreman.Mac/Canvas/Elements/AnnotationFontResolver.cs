using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas.Elements {
    //Divergence: upstream builds a GDI Font straight from the settings-provided family name (default
    //"Segoe UI"), a Windows-only font absent on macOS. This maps known Windows family names to a
    //macOS-available fallback stack, checked against SKFontManager's installed families, and falls back
    //further to the system default typeface when neither the requested family nor its mapped stack is
    //installed - mirroring upstream's try/catch-to-default without needing a font that doesn't exist here.
    internal static class AnnotationFontResolver {
        private static readonly Dictionary<string, string[]> FallbackStacks = new(StringComparer.OrdinalIgnoreCase) {
            ["Segoe UI"] = ["Helvetica Neue", "Arial"]
        };

        public static SKTypeface Resolve(string requestedFamily, SKFontStyle style) {
            if (IsFamilyAvailable(requestedFamily))
                return SKFontManager.Default.MatchFamily(requestedFamily, style);

            if (FallbackStacks.TryGetValue(requestedFamily, out string[]? fallbacks))
                foreach (string fallback in fallbacks)
                    if (IsFamilyAvailable(fallback))
                        return SKFontManager.Default.MatchFamily(fallback, style);

            return SKTypeface.FromFamilyName(null, style);
        }

        private static bool IsFamilyAvailable(string family) {
            foreach (string candidate in SKFontManager.Default.FontFamilies)
                if (string.Equals(candidate, family, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
