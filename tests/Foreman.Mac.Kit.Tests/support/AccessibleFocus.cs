using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Tests.support {
    //What VoiceOver reads on focus is the root provider's focused peer, not the control's own peer.
    public static class AccessibleFocus {
        public static AutomationPeer? TabOnto(Window window, Control control) {
            var before = new Button { Content = "Before" };
            window.Content = new StackPanel { Children = { before, control } };
            Dispatcher.UIThread.RunJobs();
            before.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            return Of(window);
        }

        public static AutomationPeer? Of(Window root) =>
            ((WindowBaseAutomationPeer)ControlAutomationPeer.CreatePeerForElement(root)).GetFocus();

        public static List<AutomationPropertyChangedEventArgs> Record(AutomationPeer peer) {
            var events = new List<AutomationPropertyChangedEventArgs>();
            peer.PropertyChanged += (_, e) => events.Add(e);
            return events;
        }
    }
}
