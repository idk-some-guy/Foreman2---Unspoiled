using Foreman.Models.Nodes;

namespace Foreman.Models {
    public sealed record NodeReplacement(RecipeNode Old, RecipeNode New, int RemovedLinkCount);
}
