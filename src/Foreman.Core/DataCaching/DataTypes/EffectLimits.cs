namespace Foreman.DataCaching.DataTypes {
    public readonly record struct EffectRange(double Low, double High);

    public sealed record EffectLimits(EffectRange Consumption, EffectRange Speed, EffectRange Productivity, EffectRange Pollution, EffectRange Quality) {
        private const double MultiplierFloor = (double)0.2f - 1.0;

        public static EffectLimits Legacy { get; } = new(
            new EffectRange(MultiplierFloor, double.PositiveInfinity),
            new EffectRange(MultiplierFloor, double.PositiveInfinity),
            new EffectRange(double.NegativeInfinity, double.PositiveInfinity),
            new EffectRange(MultiplierFloor, double.PositiveInfinity),
            new EffectRange(0, double.PositiveInfinity));

        public bool IsLegacy => ReferenceEquals(this, Legacy);
    }
}
