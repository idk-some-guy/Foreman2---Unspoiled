using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class KeyCap : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(KeyCap);

        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<KeyCap, string?>(nameof(Text));

        public static readonly StyledProperty<string?> AccessibleNameProperty =
            AvaloniaProperty.Register<KeyCap, string?>(nameof(AccessibleName));

        public string? Text {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        //Spelled-out name for VoiceOver ("Command K"); Text carries the glyph ("⌘") shown on the cap itself.
        public string? AccessibleName {
            get => GetValue(AccessibleNameProperty);
            set => SetValue(AccessibleNameProperty, value);
        }

        private readonly List<IDisposable> subs = [];

        static KeyCap() {
            FocusableProperty.OverrideDefaultValue<KeyCap>(false);
            IsTabStopProperty.OverrideDefaultValue<KeyCap>(false);
            AccessibleNameProperty.Changed.AddClassHandler<KeyCap>((cap, e) => AutomationProperties.SetName(cap, e.NewValue as string));
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.KeyCapMinSquare").Subscribe(new ResourceSinks.DoubleSink(v => { MinWidth = v; MinHeight = v; }, MinWidth)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }
    }
}
