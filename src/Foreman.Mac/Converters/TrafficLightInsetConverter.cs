using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Foreman.Mac.Converters {
    //Window.WindowDecorationMargin only ever carries a
    //top-only titlebar-height inset on macOS, never a left one - binding it straight to Margin pushed the
    //whole toolbar group down into a zero-height arrange. Top > 0 is the only signal Avalonia gives for
    //"the client area is actually extended", so that's the gate; when it fires, the traffic lights get a
    //fixed left clearance instead (three 14pt buttons, their spacing, and a trailing gap at a 52pt titlebar).
    public sealed class TrafficLightInsetConverter : IValueConverter {
        public const double TrafficLightInsetWidth = 78;

        public static readonly TrafficLightInsetConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Thickness { Top: > 0 } ? new Thickness(TrafficLightInsetWidth, 0, 0, 0) : new Thickness(0);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
