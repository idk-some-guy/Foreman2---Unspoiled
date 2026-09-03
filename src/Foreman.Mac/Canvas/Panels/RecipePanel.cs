using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Foreman.DataCaching.DataTypes;
using SkiaSharp;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/RecipePanel.cs (docs/panels-reference.md §3): a thin paint wrapper around
    //RecipePainter (phase 3) for a fixed recipe set, sized via RecipePainter.GetSize. EditRecipePanel
    //embeds one bound to the edited node's single recipe as its always-visible recipe info card.
    public sealed class RecipePanel : Control {
        private readonly IRecipe[] recipes;
        private readonly bool abbreviateSciPacks;

        public RecipePanel(IRecipe[] recipes, bool abbreviateSciPacks = false) {
            this.recipes = recipes;
            this.abbreviateSciPacks = abbreviateSciPacks;
            System.Drawing.Size size = RecipePainter.GetSize(recipes, abbreviateSciPacks);
            Width = size.Width;
            Height = size.Height;
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            context.Custom(new DrawOp(this, new Rect(Bounds.Size)));
        }

        internal void PaintOnto(SKCanvas canvas) => RecipePainter.Paint(recipes, canvas, new DrawingPoint(0, 0), abbreviateSciPacks);

        private sealed class DrawOp(RecipePanel owner, Rect bounds) : ICustomDrawOperation {
            public Rect Bounds { get; } = bounds;

            public bool HitTest(Point p) => Bounds.Contains(p);
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context) {
                ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature is null)
                    return;

                using ISkiaSharpApiLease lease = leaseFeature.Lease();
                owner.PaintOnto(lease.SkCanvas);
            }
        }
    }
}
