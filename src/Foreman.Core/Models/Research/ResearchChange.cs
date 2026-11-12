namespace Foreman.Models.Research {
    internal readonly record struct ResearchChange(ResearchField? Field, string? Recipe, double? Old, double? New, bool ReplacedEdit);
}
