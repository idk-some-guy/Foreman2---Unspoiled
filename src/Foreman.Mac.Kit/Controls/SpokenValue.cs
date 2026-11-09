namespace Foreman.Mac.Kit.Controls {
    internal static class SpokenValue {
        public static string Describe(string number, string? unit, string? spokenUnit) {
            string? word = spokenUnit ?? (unit == "%" ? "percent" : unit);
            return string.IsNullOrEmpty(word) ? number : $"{number} {word}";
        }
    }
}
