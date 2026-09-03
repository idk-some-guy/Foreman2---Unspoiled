using Foreman;
using Foreman.DataCaching.DataTypes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    //Ports the RecipePainter class nested inside Controls/GraphicsStuff.cs (it never had its own upstream
    //source file - see docs/canvas-reference.md §6): the recipe hover tooltip's mini ingredients/products/
    //crafting-time layout. abbreviateSciPacks is threaded as an explicit parameter rather than reading
    //Properties.Settings.Default.AbbreviateSciPacks, matching how every other display flag reaches this
    //port's element tree through NodeElementContext instead of a static settings singleton.
    public static class RecipePainter {
        private const int SectionWidth = 200;

        private static readonly SKTypeface RegularTypeface = SKTypeface.Default;
        private static readonly SKTypeface BoldTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        private const float RecipeFontSize = 8f;
        private const float SectionFontSize = 8f;
        private const float QuantityFontSize = 8f;
        private const float ItemFontSize = 7.8f;

        private static readonly SKColor TextColor = SKColors.White;
        private static readonly SKPaint BackgroundPaint = Fill(new SKColor(65, 65, 65));
        private static readonly SKPaint DarkBackgroundPaint = Fill(new SKColor(40, 40, 40));
        private static readonly SKPaint BorderPaint = Stroke(SKColors.Black, 2);
        private static readonly SKPaint BreakerPaint = Stroke(SKColors.Black, 10);

        private static SKPaint Fill(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        private static SKPaint Stroke(SKColor color, float width) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
        private static SKRect ToSkRect(Rectangle rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

        //Upstream inlines `value.ToString("0.##", DisplayCulture.Format) + "x"` at both the ingredient and
        //product call sites; GraphicsStuff.DoubleToString's magnitude-branched precision (fewer decimals
        //above 100, none above 10000) is a different formatter meant for other UI text and must not be used
        //here, or fractional quantities silently render wrong.
        internal static string FormatQuantity(double quantity) => quantity.ToString("0.##", DisplayCulture.Format) + "x";

        public static Size GetSize(ICollection<IRecipe> recipes, bool abbreviateSciPacks) {
            int width = ((SectionWidth + 10) * recipes.Count) - 10;
            int height = 110 + 20 + recipes.Max(r => r.IngredientList.Count) * 40 + recipes.Max(r => r.ProductList.Count) * 40
                + recipes.Max(r => r.MyUnlockSciencePacks.Count) * (abbreviateSciPacks ? 40 : 30);
            return new Size(width, height);
        }

        public static void Paint(IList<IRecipe> recipes, SKCanvas canvas, Point offset, bool abbreviateSciPacks) {
            var boundary = new Rectangle(offset, GetSize(recipes, abbreviateSciPacks));
            canvas.DrawRect(ToSkRect(boundary), BackgroundPaint);

            int maxIngredientCount = 0, maxProductCount = 0, maxSciencePackListsCount = 0;
            foreach (IRecipe recipe in recipes) {
                maxIngredientCount = Math.Max(maxIngredientCount, recipe.IngredientList.Count);
                maxProductCount = Math.Max(maxProductCount, recipe.ProductList.Count);
                maxSciencePackListsCount = Math.Max(maxSciencePackListsCount, recipe.MyUnlockSciencePacks.Count);
            }

            int xOffset = boundary.X;
            for (int r = 0; r < recipes.Count; r++) {
                int yOffset = boundary.Y;

                canvas.DrawRect(ToSkRect(new Rectangle(xOffset, yOffset, SectionWidth, 40)), DarkBackgroundPaint);
                canvas.DrawBitmap(recipes[r].Icon, SKRect.Create(4 + xOffset, 4 + yOffset, 32, 32));

                var titleBox = new Rectangle(xOffset + 42, yOffset + 4, SectionWidth - 48, 32);
                GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, RecipeFontSize, titleBox, recipes[r].FriendlyName, TextHorizontalAlign.Near, TextVerticalAlign.Center);

                //ingredients
                yOffset += 44;
                canvas.DrawRect(ToSkRect(new Rectangle(xOffset, yOffset, SectionWidth, 20)), DarkBackgroundPaint);
                yOffset += 2;
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, SectionFontSize, "Ingredients:", 4 + xOffset, yOffset);
                yOffset += 20;
                for (int i = 0; i < maxIngredientCount; i++) {
                    if (i < recipes[r].IngredientList.Count) {
                        IItem ingredient = recipes[r].IngredientList[i];
                        canvas.DrawBitmap(ingredient.Icon, SKRect.Create(14 + xOffset, 4 + yOffset, 32, 32));

                        var textBox = new Rectangle(xOffset + 52, yOffset + 2, SectionWidth - 58, 20);
                        GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, QuantityFontSize, FormatQuantity(recipes[r].IngredientSet[ingredient]), 52 + xOffset, 20 + yOffset);
                        GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, ItemFontSize, textBox, recipes[r].GetIngredientFriendlyName(ingredient), TextHorizontalAlign.Near, TextVerticalAlign.Center);
                    }
                    yOffset += 40;
                }

                //products
                canvas.DrawRect(ToSkRect(new Rectangle(xOffset, yOffset, SectionWidth, 20)), DarkBackgroundPaint);
                yOffset += 2;
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, SectionFontSize, "Products:", 4 + xOffset, yOffset);
                yOffset += 20;
                for (int i = 0; i < maxProductCount; i++) {
                    if (i < recipes[r].ProductList.Count) {
                        IItem product = recipes[r].ProductList[i];
                        canvas.DrawBitmap(product.Icon, SKRect.Create(14 + xOffset, 4 + yOffset, 32, 32));

                        var textBox = new Rectangle(xOffset + 52, yOffset + 2, SectionWidth - 58, 20);
                        GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, QuantityFontSize, FormatQuantity(recipes[r].ProductSet[product]), 52 + xOffset, 20 + yOffset);
                        GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, ItemFontSize, textBox, recipes[r].GetProductFriendlyName(product), TextHorizontalAlign.Near, TextVerticalAlign.Center);
                    }
                    yOffset += 40;
                }

                //unlock science packs
                canvas.DrawRect(ToSkRect(new Rectangle(xOffset, yOffset, SectionWidth, 22)), DarkBackgroundPaint);
                yOffset += 2;
                string sciPackLabel = abbreviateSciPacks
                    ? (recipes[r].MyUnlockSciencePacks.Count > 1 ? "Key required science packs (any):" : "Key required science packs:")
                    : (recipes[r].MyUnlockSciencePacks.Count > 1 ? "Required science packs (any):" : "Required science packs:");
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, SectionFontSize, sciPackLabel, 4 + xOffset, yOffset);
                yOffset += 20;
                for (int i = 0; i < maxSciencePackListsCount; i++) {
                    if (i < recipes[r].MyUnlockSciencePacks.Count) {
                        IReadOnlyList<IItem> sciPacks = recipes[r].MyUnlockSciencePacks[i];
                        int sciPackSize = 24;
                        if (abbreviateSciPacks) { //dont show the science pack if it is a prerequisite of another science pack (that we show)
                            var filteredSciPacks = new List<IItem>(sciPacks);
                            foreach (IItem sciPack in sciPacks)
                                foreach (IItem prereq in recipes[r].Owner.SciencePackPrerequisites[sciPack])
                                    filteredSciPacks.Remove(prereq);
                            sciPacks = filteredSciPacks;
                            sciPackSize = 32;
                        }

                        int iconSize = sciPacks.Count == 0 ? sciPackSize : Math.Min(sciPackSize, (SectionWidth - 8) / sciPacks.Count);
                        for (int j = 0; j < sciPacks.Count; j++)
                            canvas.DrawBitmap(sciPacks[j].Icon, SKRect.Create(xOffset + 4 + (j * iconSize), 3 + yOffset, iconSize, iconSize));
                    }
                    yOffset += abbreviateSciPacks ? 40 : 30;
                }

                //time
                canvas.DrawRect(ToSkRect(new Rectangle(xOffset, yOffset, SectionWidth, 22)), DarkBackgroundPaint);
                yOffset += 2;
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, BoldTypeface, SectionFontSize, "Crafting Time: " + recipes[r].Time.ToString("0.##", DisplayCulture.Format) + " s", 4 + xOffset, yOffset);

                //breaker
                if (r < recipes.Count - 1) {
                    canvas.DrawLine(xOffset + SectionWidth + 5, boundary.Y, xOffset + SectionWidth + 5, boundary.Y + boundary.Height, BreakerPaint);
                    xOffset += SectionWidth + 10;
                }
            }

            canvas.DrawRect(ToSkRect(boundary), BorderPaint);
        }
    }
}
