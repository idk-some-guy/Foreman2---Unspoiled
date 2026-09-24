using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Foreman.Mac.Kit.Controls {
    public class Chip : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Chip);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<Chip, string?>(nameof(Label));

        public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
            AvaloniaProperty.Register<Chip, ICommand?>(nameof(RemoveCommand));

        public static readonly StyledProperty<string?> RemoveAccessibleNameProperty =
            AvaloniaProperty.Register<Chip, string?>(nameof(RemoveAccessibleName));

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public ICommand? RemoveCommand {
            get => GetValue(RemoveCommandProperty);
            set => SetValue(RemoveCommandProperty, value);
        }

        public string? RemoveAccessibleName {
            get => GetValue(RemoveAccessibleNameProperty);
            set => SetValue(RemoveAccessibleNameProperty, value);
        }

        public event EventHandler? Removed;

        private Border? closeBox;
        private Border? closeHoverFill;
        private Path? closeGlyph;
        private TextBlock? label;
        private IDisposable? labelBoundsSub;
        private bool labelTrimmed;
        private bool closeHovering;
        private double glyphIdleOpacity = 0.75;
        private readonly List<IDisposable> subs = [];

        static Chip() => FocusableProperty.OverrideDefaultValue<Chip>(true);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (closeBox is not null) {
                closeBox.PointerEntered -= OnCloseEntered;
                closeBox.PointerExited -= OnCloseExited;
                closeBox.PointerPressed -= OnClosePressed;
            }
            closeBox = e.NameScope.Find<Border>("PART_CloseBox");
            closeHoverFill = e.NameScope.Find<Border>("PART_CloseHoverFill");
            closeGlyph = e.NameScope.Find<Path>("PART_CloseGlyph");
            label = e.NameScope.Find<TextBlock>("PART_Label");
            SubscribeLabelBounds();
            if (closeBox is not null) {
                closeBox.PointerEntered += OnCloseEntered;
                closeBox.PointerExited += OnCloseExited;
                closeBox.PointerPressed += OnClosePressed;
            }
            ApplyCloseState();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            SubscribeLabelBounds();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
            labelBoundsSub?.Dispose();
            labelBoundsSub = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == LabelProperty)
                ApplyTrimmedTip();
        }

        //The label only reports its trim after arrange, so the tip follows the label's arranged bounds.
        private void SubscribeLabelBounds() {
            labelBoundsSub?.Dispose();
            labelBoundsSub = label?.GetObservable(BoundsProperty).Subscribe(new BoundsObserver(_ => {
                labelTrimmed = label!.TextLayout.TextLines.Any(l => l.HasCollapsed);
                ApplyTrimmedTip();
            }));
        }

        private void ApplyTrimmedTip() {
            string? tip = labelTrimmed ? Label : null;
            if (!Equals(ToolTip.GetTip(this), tip))
                ToolTip.SetTip(this, tip);
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Opacity.ChipCloseIdle").Subscribe(new ResourceSinks.DoubleSink(v => { glyphIdleOpacity = v; ApplyCloseState(); }, glyphIdleOpacity)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void OnCloseEntered(object? sender, PointerEventArgs e) {
            closeHovering = true;
            ApplyCloseState();
        }

        private void OnCloseExited(object? sender, PointerEventArgs e) {
            closeHovering = false;
            ApplyCloseState();
        }

        private void OnClosePressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(closeBox!).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            Remove();
        }

        private void ApplyCloseState() {
            if (closeHoverFill is not null)
                closeHoverFill.Opacity = closeHovering ? 1 : 0;
            if (closeGlyph is not null)
                closeGlyph.Opacity = closeHovering ? 1 : glyphIdleOpacity;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            switch (e.Key) {
                case Key.Space:
                case Key.Enter:
                case Key.Delete:
                case Key.Back:
                    Remove();
                    e.Handled = true;
                    return;
            }
            base.OnKeyDown(e);
        }

        internal void Remove() {
            Chip? landing = NextChip() ?? PreviousChip();
            Control? fallback = landing is null ? ChipHost.GetFocusFallback(this) : null;
            if (RemoveCommand?.CanExecute(null) == true)
                RemoveCommand.Execute(null);
            Removed?.Invoke(this, EventArgs.Empty);
            if (landing is not null)
                landing.Focus(NavigationMethod.Directional);
            else
                fallback?.Focus(NavigationMethod.Directional);
        }

        private Chip? NextChip() {
            if (Parent is not Panel panel)
                return null;
            List<Chip> chips = [.. panel.Children.OfType<Chip>()];
            int index = chips.IndexOf(this);
            return index < 0 ? null : chips.ElementAtOrDefault(index + 1);
        }

        private Chip? PreviousChip() {
            if (Parent is not Panel panel)
                return null;
            List<Chip> chips = [.. panel.Children.OfType<Chip>()];
            int index = chips.IndexOf(this);
            return index <= 0 ? null : chips[index - 1];
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ChipAutomationPeer(this);

        private sealed class BoundsObserver(Action<Rect> onNext) : IObserver<Rect> {
            public void OnNext(Rect value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class ChipAutomationPeer(Chip owner) : ControlAutomationPeer(owner) {
            protected override string? GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Label;
            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
                owner.closeBox is { } close ? [ControlAutomationPeer.CreatePeerForElement(close)] : [];
        }
    }

    //A named Border subclass so the ✕ gets its own invokable automation peer.
    public class ChipCloseBox : Border {
        protected override Type StyleKeyOverride => typeof(Border);

        protected override AutomationPeer OnCreateAutomationPeer() => new ChipCloseBoxAutomationPeer(this);

        private sealed class ChipCloseBoxAutomationPeer(ChipCloseBox owner) : ControlAutomationPeer(owner), IInvokeProvider {
            private Chip? Chip => owner.TemplatedParent as Chip;
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
            protected override string? GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : Chip?.RemoveAccessibleName;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            void IInvokeProvider.Invoke() => Chip?.Remove();
        }
    }
}
