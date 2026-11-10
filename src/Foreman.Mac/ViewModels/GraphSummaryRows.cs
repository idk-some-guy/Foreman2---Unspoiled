using Avalonia.Media.Imaging;

namespace Foreman.Mac.ViewModels {
    internal enum RowStatus { None, Warning, Error }

    internal sealed class BuildingRow(object tag, string rowKey, Bitmap? icon, double countValue, string countText, string name,
            string powerText, double powerValue, string powerBText, double powerBValue, bool isStale) {
        public object Tag { get; } = tag;
        public string RowKey { get; } = rowKey;
        public Bitmap? Icon { get; } = icon;
        public double CountValue { get; } = countValue;
        public string CountText { get; } = countText;
        public string Name { get; } = name;
        public string PowerText { get; } = powerText;
        public double PowerValue { get; } = powerValue;
        public string PowerBText { get; } = powerBText;
        public double PowerBValue { get; } = powerBValue;
        public bool IsStale { get; } = isStale;
    }

    internal sealed class BeaconRow(object tag, string rowKey, Bitmap? icon, double countValue, string countText, string name,
            string powerBText, double powerBValue, bool isStale) {
        public object Tag { get; } = tag;
        public string RowKey { get; } = rowKey;
        public Bitmap? Icon { get; } = icon;
        public double CountValue { get; } = countValue;
        public string CountText { get; } = countText;
        public string Name { get; } = name;
        public string PowerBText { get; } = powerBText;
        public double PowerBValue { get; } = powerBValue;
        public bool IsStale { get; } = isStale;
    }

    internal sealed class ItemRow(object tag, string rowKey, Bitmap? icon, string name,
            string inText, double inValue, string inULText, double inULValue, string outText, double outValue,
            string outULText, double outULValue, string overprodText, double overprodValue, string producedText, double producedValue,
            string consumedText, double consumedValue, bool isStale) {
        public object Tag { get; } = tag;
        public string RowKey { get; } = rowKey;
        public Bitmap? Icon { get; } = icon;
        public string Name { get; } = name;
        public string InText { get; } = inText;
        public double InValue { get; } = inValue;
        public string InULText { get; } = inULText;
        public double InULValue { get; } = inULValue;
        public string OutText { get; } = outText;
        public double OutValue { get; } = outValue;
        public string OutULText { get; } = outULText;
        public double OutULValue { get; } = outULValue;
        public string OverprodText { get; } = overprodText;
        public double OverprodValue { get; } = overprodValue;
        public string ProducedText { get; } = producedText;
        public double ProducedValue { get; } = producedValue;
        public string ConsumedText { get; } = consumedText;
        public double ConsumedValue { get; } = consumedValue;
        public bool IsStale { get; } = isStale;
        public RowStatus Status => InULValue > 0 ? RowStatus.Error : OutULValue > 0 || OverprodValue > 0 ? RowStatus.Warning : RowStatus.None;
    }

    internal sealed class KeyNodeRow(object tag, string rowKey, Bitmap? icon, string typeText, string detailsText, string titleText,
            string throughputText, double throughputValue, string factoriesText, double factoriesValue, bool isStale) {
        public object Tag { get; } = tag;
        public string RowKey { get; } = rowKey;
        public Bitmap? Icon { get; } = icon;
        public string TypeText { get; } = typeText;
        public string DetailsText { get; } = detailsText;
        public string TitleText { get; } = titleText;
        public string ThroughputText { get; } = throughputText;
        public double ThroughputValue { get; } = throughputValue;
        public string FactoriesText { get; } = factoriesText;
        public double FactoriesValue { get; } = factoriesValue;
        public bool IsStale { get; } = isStale;
    }
}
