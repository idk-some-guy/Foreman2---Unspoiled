namespace Foreman.DataCaching.DataTypes {
    public interface IFluid : IItem {
        bool IsTemperatureDependent { get; }
        double DefaultTemperature { get; }
        double SpecificHeatCapacity { get; }
        double GasTemperature { get; }
        double MaxTemperature { get; }

        string GetTemperatureRangeFriendlyName(FRange tempRange);
        string GetTemperatureFriendlyName(double temperature);
    }

    public class FluidPrototype(DataCache dCache, string name, string friendlyName, SubgroupPrototype subgroup, string order, bool isMissing = false) : ItemPrototype(dCache, name, friendlyName, subgroup, order, isMissing), IFluid {
        public bool IsTemperatureDependent { get; internal set; }
        public double DefaultTemperature { get; internal set; }
        public double SpecificHeatCapacity { get; internal set; }
        public double GasTemperature { get; internal set; }
        public double MaxTemperature { get; internal set; }

        public string GetTemperatureRangeFriendlyName(FRange tempRange) {
            if (tempRange.Ignore)
                return FriendlyName;

            string name = FriendlyName;
            bool includeMin = tempRange.Min >= double.MinValue;
            bool includeMax = tempRange.Max <= double.MaxValue;

            if (tempRange.Min == tempRange.Max)
                name += string.Create(DisplayCulture.Format, $" ({tempRange.Min.ToString("0", DisplayCulture.Format)}°c)");
            else if (includeMin && includeMax)
                name += string.Create(DisplayCulture.Format, $" ({tempRange.Min.ToString("0", DisplayCulture.Format)}-{tempRange.Max.ToString("0", DisplayCulture.Format)}°c)");
            else if (includeMin)
                name += string.Create(DisplayCulture.Format, $" (min {tempRange.Min.ToString("0", DisplayCulture.Format)}°c)");
            else if (includeMax)
                name += string.Create(DisplayCulture.Format, $" (max {tempRange.Max.ToString("0", DisplayCulture.Format)}°c)");
            else
                name += "(any°)";

            return name;
        }

        public string GetTemperatureFriendlyName(double temperature) {
            return string.Create(DisplayCulture.Format, $"{FriendlyName} ({temperature.ToString("0", DisplayCulture.Format)}°c)");
        }


        public override string ToString() { return string.Format(CultureInfo.InvariantCulture, "Item: {0}", Name); }
    }
}
