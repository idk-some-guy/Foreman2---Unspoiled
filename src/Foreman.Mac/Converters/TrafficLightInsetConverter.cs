using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Foreman.Mac.Converters {
    public sealed class TrafficLightInsetConverter : IValueConverter {
        public const double TrafficLightInsetWidth = 78;

        public static readonly TrafficLightInsetConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Thickness { Top: > 0 } ? new Thickness(TrafficLightInsetWidth, 0, 0, 0) : new Thickness(0);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
