using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Foreman.Mac.Kit.Controls {
    public class RadioGroup : ItemsControl {
        protected override Type StyleKeyOverride => typeof(RadioGroup);

        static RadioGroup() {
            KeyDownEvent.AddClassHandler<RadioGroup>((group, e) => group.OnGroupKeyDown(e), handledEventsToo: true);
        }

        private void OnGroupKeyDown(KeyEventArgs e) {
            if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right))
                return;

            var radios = this.GetVisualDescendants().OfType<RadioButton>().ToList();
            if (radios.Count == 0)
                return;

            //e.Source, not IsFocused: Avalonia may have already moved focus before this bubbles up.
            int current = e.Source is RadioButton source ? radios.IndexOf(source) : -1;
            bool anchored = current >= 0;
            if (!anchored)
                current = radios.FindIndex(r => r.IsChecked == true);
            anchored |= current >= 0;

            bool forward = e.Key is Key.Down or Key.Right;
            RadioButton next = anchored
                ? radios[(current + (forward ? 1 : -1) + radios.Count) % radios.Count]
                : radios[forward ? 0 : radios.Count - 1];

            next.Focus(NavigationMethod.Directional);
            next.IsChecked = true;
            e.Handled = anchored;
        }
    }
}
