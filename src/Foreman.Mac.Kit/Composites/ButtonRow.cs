using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    public enum ButtonRowRole { Helper, Other, Cancel, Default, Destructive }

    //Trailing-aligned action row: [helper] ····· [other] [Cancel] [default], or [other][Destructive][Cancel] when the trailing action is destructive.
    public class ButtonRow : ItemsControl {
        protected override Type StyleKeyOverride => typeof(ButtonRow);

        public static readonly AttachedProperty<ButtonRowRole> RoleProperty =
            AvaloniaProperty.RegisterAttached<ButtonRow, Button, ButtonRowRole>("Role", ButtonRowRole.Other);

        public static ButtonRowRole GetRole(Button button) => button.GetValue(RoleProperty);
        public static void SetRole(Button button, ButtonRowRole value) => button.SetValue(RoleProperty, value);

        public static readonly StyledProperty<bool> IsCenteredProperty =
            AvaloniaProperty.Register<ButtonRow, bool>(nameof(IsCentered));

        public bool IsCentered {
            get => GetValue(IsCenteredProperty);
            set => SetValue(IsCenteredProperty, value);
        }

        //Carries the role actually resolved, Default or the promoted Cancel, so a host can tell the two invocations apart.
        public event EventHandler<ButtonRowRole>? DefaultInvoked;
        public event EventHandler? CancelInvoked;
        public event EventHandler? DestructiveInvoked;

        static ButtonRow() {
            IsCenteredProperty.Changed.AddClassHandler<ButtonRow>((r, _) => r.PseudoClasses.Set(":centered", r.IsCentered));
        }

        public ButtonRow() => AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (Items is INotifyCollectionChanged incc)
                incc.CollectionChanged += OnItemsChanged;
            ApplyRoles();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            if (Items is INotifyCollectionChanged incc)
                incc.CollectionChanged -= OnItemsChanged;
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyRoles();

        //.default styling goes on the Default-role button when present, else promoted onto Cancel when a Destructive button is present.
        private void ApplyRoles() {
            Button[] buttons = [.. Items.OfType<Button>()];
            foreach (Button b in buttons) {
                //Never full-bleed/equal-width: the row arranges every button at its own desired size.
                b.HorizontalAlignment = HorizontalAlignment.Left;
                b.Classes.Set("primary", false);
                b.Classes.Set("destructive", GetRole(b) == ButtonRowRole.Destructive);
                b.Classes.Set("plain", GetRole(b) == ButtonRowRole.Helper);
            }

            Button? def = Find(buttons, ButtonRowRole.Default);
            Button? destructive = Find(buttons, ButtonRowRole.Destructive);
            Button? cancel = Find(buttons, ButtonRowRole.Cancel);
            if (def is not null)
                def.Classes.Set("primary", true);
            else if (destructive is not null && cancel is not null)
                cancel.Classes.Set("primary", true);
        }

        private static Button? Find(Button[] buttons, ButtonRowRole role) =>
            buttons.FirstOrDefault(b => GetRole(b) == role);

        public bool TryInvokeDefault() {
            Button? button = EffectiveDefault();
            if (!Click(button))
                return false;
            DefaultInvoked?.Invoke(this, GetRole(button!));
            return true;
        }

        public bool TryInvokeCancel() {
            if (!Click(EffectiveCancel()))
                return false;
            CancelInvoked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool TryInvokeDestructive() {
            Button[] buttons = [.. Items.OfType<Button>()];
            if (!Click(Find(buttons, ButtonRowRole.Destructive)))
                return false;
            DestructiveInvoked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        //Exposed for a host that needs the resolved default button without depending on the "primary" class's visual-tree realization.
        public Button? ResolveDefaultButton() => EffectiveDefault();

        private Button? EffectiveDefault() {
            Button[] buttons = [.. Items.OfType<Button>()];
            Button? def = Find(buttons, ButtonRowRole.Default);
            if (def is not null)
                return def;
            Button? destructive = Find(buttons, ButtonRowRole.Destructive);
            Button? cancel = Find(buttons, ButtonRowRole.Cancel);
            if (destructive is not null && cancel is not null)
                return cancel;
            //Dismiss-only row (a lone "Done"): its one button is both default and cancel target.
            return buttons.Length == 1 ? buttons[0] : null;
        }

        private Button? EffectiveCancel() {
            Button[] buttons = [.. Items.OfType<Button>()];
            Button? cancel = Find(buttons, ButtonRowRole.Cancel);
            return cancel ?? EffectiveDefault();
        }

        private static bool Click(Button? button) {
            if (button is null || !button.IsEffectivelyEnabled)
                return false;
            //Mirrors Button.OnClick: Command runs only if nothing handled Click first.
            var click = new RoutedEventArgs(Button.ClickEvent);
            button.RaiseEvent(click);
            if (!click.Handled && button.Command is { } command && command.CanExecute(button.CommandParameter))
                command.Execute(button.CommandParameter);
            return true;
        }

        //Tunnel so Return/Escape/⌘⌫ win over whichever row button happens to have focus.
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    if (TryInvokeDefault())
                        e.Handled = true;
                    break;
                case Key.Escape:
                    if (TryInvokeCancel())
                        e.Handled = true;
                    break;
                case Key.OemPeriod when e.KeyModifiers == KeyModifiers.Meta:
                    if (TryInvokeCancel())
                        e.Handled = true;
                    break;
                case Key.Back when e.KeyModifiers == KeyModifiers.Meta:
                    if (TryInvokeDestructive())
                        e.Handled = true;
                    break;
            }
        }
    }
}
