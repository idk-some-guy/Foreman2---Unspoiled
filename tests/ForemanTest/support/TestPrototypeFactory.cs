using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using System.Reflection;

namespace ForemanTest.support {
    /// <summary>Creates Foreman prototypes for isolated solver tests.</summary>
    internal static class TestPrototypeFactory {
        public static AssemblerPrototype CreateTestAssembler(DataCache cache) => new(cache, "§§test:assembler", "Test Assembler", EntityType.Assembler, EnergySource.Electric, false);

        public static QualityPrototype[] CreateQualityChain(DataCache cache, params (double next, double chain)[] tiers) {
            var chain = new QualityPrototype[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
                chain[i] = new QualityPrototype(cache, "tier" + i, "Tier " + i, ((char)('a' + i)).ToString()) {
                    Level = i,
                    NextProbability = tiers[i].next,
                    ChainProbability = tiers[i].chain,
                };
            for (int i = 0; i < tiers.Length - 1; i++) {
                chain[i].NextQuality = chain[i + 1];
                chain[i + 1].PrevQuality = chain[i];
            }
            return chain;
        }

        public static void LinkRecipeAndAssembler(RecipePrototype recipe, AssemblerPrototype assembler) {
            recipe.AssemblersInternal.Add(assembler);
            assembler.RecipesInternal.Add(recipe);
        }

        public static void SetRecipeTime(RecipePrototype recipe, double time) {
            PropertyInfo timeProperty = ReflectionTestHelper.RequireProperty(
                typeof(RecipePrototype), "Time", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo setter = ReflectionTestHelper.Require(
                timeProperty.GetSetMethod(nonPublic: true),
                "RecipePrototype.Time setter was not found.");
            setter.Invoke(recipe, [time]);
        }
    }
}
