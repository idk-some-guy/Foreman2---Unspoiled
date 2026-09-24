namespace Foreman.Mac.Kit.Controls {
    //The value a range control speaks: the number, then the unit as a word ("40 percent", "2 pixels").
    internal static class SpokenValue {
        public static string Describe(string number, string? unit, string? spokenUnit) {
            string? word = spokenUnit ?? (unit == "%" ? "percent" : unit);
            return string.IsNullOrEmpty(word) ? number : $"{number} {word}";
        }
    }
}
