using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Foreman.DataCaching.DataTypes;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Panels {
    //Shared "Quality:" label + selector (docs/panels-reference.md §9 cross-cutting note): IRChooserPanel's
    //QualitySelector and EditRecipePanel's own copy are the same population dance in upstream (foreach
    //enabled IQuality, add FriendlyName, disable the combo when only one quality exists) - pulled out once
    //here instead of duplicated per panel.
    public sealed class QualityPicker : StackPanel {
        private readonly ComboBox selector;
        private readonly List<IQuality> qualities = [];

        public QualityPicker() {
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            var label = new TextBlock { Text = "Quality:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            selector = new ComboBox { Width = ChooserLayout.QualityComboWidth };
            Children.Add(label);
            Children.Add(selector);
        }

        public ComboBox Selector => selector;

        public void SetQualities(IEnumerable<IQuality> availableQualities) {
            qualities.Clear();
            qualities.AddRange(availableQualities);
            selector.ItemsSource = qualities.Select(q => q.FriendlyName).ToList();
            selector.SelectedIndex = 0;
            selector.IsEnabled = qualities.Count > 1;
        }

        public void SetFixedQuality(IQuality quality) {
            qualities.Clear();
            qualities.Add(quality);
            selector.ItemsSource = new[] { quality.FriendlyName };
            selector.SelectedIndex = 0;
            selector.IsEnabled = false;
        }

        public IQuality SelectedQuality {
            get {
                int index = selector.SelectedIndex;
                return qualities[index >= 0 && index < qualities.Count ? index : 0];
            }
        }
    }
}
