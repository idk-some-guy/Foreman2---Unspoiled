using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using System.Linq;
using System.Reflection;

namespace ForemanTest.support {
    /// <summary>Creates Foreman prototypes for isolated solver tests.</summary>
    internal static class TestPrototypeFactory {
        public static AssemblerPrototype CreateTestAssembler(DataCache cache) => new(cache, "§§test:assembler", "Test Assembler", EntityType.Assembler, EnergySource.Electric, false);

        public static AssemblerPrototype CreateTestAssembler(DataCache cache, EffectLimits limits) {
            AssemblerPrototype assembler = CreateTestAssembler(cache);
            assembler.EffectLimits = limits;
            assembler.ModuleSlots = 4;
            assembler.AllowModules = true;
            foreach (IQuality quality in cache.Qualities.Values)
                assembler.SpeedInternal[quality] = 1;
            return assembler;
        }

        public static AssemblerPrototype CreateTestTower(DataCache cache, int moduleSlots, EffectLimits limits) {
            var tower = new AssemblerPrototype(cache, "§§test:tower", "Test Tower", EntityType.AgriculturalTower, EnergySource.Electric, false) {
                EffectLimits = limits,
                ModuleSlots = moduleSlots,
                AllowModules = true,
            };
            foreach (IQuality quality in cache.Qualities.Values)
                tower.SpeedInternal[quality] = 1;
            return tower;
        }

        public static ModulePrototype CreateTestModule(DataCache cache, ModuleEffects effects) => CreateTestModule(cache, "§§test:module", "Test Module", effects);

        public static ModulePrototype CreateTestModule(DataCache cache, string name, string friendlyName, ModuleEffects effects) {
            var module = new ModulePrototype(cache, name, friendlyName) {
                ConsumptionBonus = effects.Consumption,
                SpeedBonus = effects.Speed,
                ProductivityBonus = effects.Productivity,
                PollutionBonus = effects.Pollution,
                QualityBonus = effects.Quality,
                QualityEffects = cache.Qualities.Values.ToDictionary(q => q, _ => effects),
            };
            return module;
        }

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
