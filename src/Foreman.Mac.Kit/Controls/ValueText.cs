using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using System;

namespace Foreman.Mac.Kit.Controls {
    public class ValueText : TextBlock {
        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override AutomationPeer OnCreateAutomationPeer() => new ValueTextAutomationPeer(this);

        private sealed class ValueTextAutomationPeer(ValueText owner) : TextBlockAutomationPeer(owner), IValueProvider {
            //TextBlock's own peer names itself by its text, over any automation name.
            protected override string? GetNameCore() =>
                AutomationProperties.GetName(Owner) is { Length: > 0 } name ? name : base.GetNameCore();

            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => ((ValueText)Owner).Text;
            void IValueProvider.SetValue(string? value) { }
        }
    }
}
