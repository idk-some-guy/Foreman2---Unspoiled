using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    public enum SolveStatusState { Idle, Solving, Failed }

    public class SolveStatus : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(SolveStatus);

        public static readonly StyledProperty<SolveStatusState> StateProperty =
            AvaloniaProperty.Register<SolveStatus, SolveStatusState>(nameof(State));

        public static readonly StyledProperty<object?> ErrorContentProperty =
            AvaloniaProperty.Register<SolveStatus, object?>(nameof(ErrorContent));

        public static readonly StyledProperty<bool> IsErrorPopoverOpenProperty =
            AvaloniaProperty.Register<SolveStatus, bool>(nameof(IsErrorPopoverOpen));

        public static readonly StyledProperty<PopoverHost?> PopoverHostProperty =
            AvaloniaProperty.Register<SolveStatus, PopoverHost?>(nameof(PopoverHost));

        public SolveStatusState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
        public object? ErrorContent { get => GetValue(ErrorContentProperty); set => SetValue(ErrorContentProperty, value); }
        public bool IsErrorPopoverOpen { get => GetValue(IsErrorPopoverOpenProperty); set => SetValue(IsErrorPopoverOpenProperty, value); }
        public PopoverHost? PopoverHost { get => GetValue(PopoverHostProperty); set => SetValue(PopoverHostProperty, value); }

        public event EventHandler? ErrorDismissed;

        private Panel? slot;
        private Control? spinner;
        private Button? triangle;
        private PopoverFrame? errorPopover;
        private PopoverHost? usedHost;
        private bool templateApplied;
        private SolveStatusAutomationPeer? peer;

        static SolveStatus() {
            StateProperty.Changed.AddClassHandler<SolveStatus>((c, _) => c.ApplyState());
            IsErrorPopoverOpenProperty.Changed.AddClassHandler<SolveStatus>((c, e) => c.ApplyErrorPopoverOpen((bool)e.NewValue!));
            ErrorContentProperty.Changed.AddClassHandler<SolveStatus>((c, _) => {
                if (c.errorPopover is { } popover)
                    popover.Content = c.ErrorContent;
            });
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (triangle is not null)
                triangle.Click -= OnTriangleClick;
            slot = e.NameScope.Find<Panel>("PART_Slot");
            spinner = e.NameScope.Find<Control>("PART_Spinner");
            triangle = e.NameScope.Find<Button>("PART_Triangle");
            if (triangle is not null)
                triangle.Click += OnTriangleClick;
            templateApplied = true;
            ApplyState();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (IsErrorPopoverOpen && errorPopover is null)
                OpenErrorPopover();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            if (errorPopover is not { } popover)
                return;
            popover.Closed -= OnErrorPopoverClosed;
            usedHost?.Remove(popover);
            errorPopover = null;
            usedHost = null;
            IsErrorPopoverOpen = false;
        }

        private void ApplyState() {
            if (!templateApplied)
                return;
            SolveStatusState state = State;
            PseudoClasses.Set(":idle", state == SolveStatusState.Idle);
            PseudoClasses.Set(":solving", state == SolveStatusState.Solving);
            PseudoClasses.Set(":failed", state == SolveStatusState.Failed);
            if (slot is not null)
                slot.IsVisible = state != SolveStatusState.Idle;
            if (spinner is not null)
                spinner.IsVisible = state == SolveStatusState.Solving;
            if (triangle is not null)
                triangle.IsVisible = state == SolveStatusState.Failed;
            peer?.Refresh();
            if (state != SolveStatusState.Failed)
                IsErrorPopoverOpen = false;
        }

        private void OnTriangleClick(object? sender, RoutedEventArgs e) => IsErrorPopoverOpen = true;

        private void ApplyErrorPopoverOpen(bool open) {
            if (open) {
                if (State != SolveStatusState.Failed) {
                    IsErrorPopoverOpen = false;
                    return;
                }
                if (VisualRoot is not null)
                    OpenErrorPopover();
            } else if (errorPopover is { IsOpen: true } popover) {
                popover.IsOpen = false;
            }
        }

        private void OnErrorPopoverClosed(object? sender, EventArgs e) {
            IsErrorPopoverOpen = false;
            if (State == SolveStatusState.Failed)
                ErrorDismissed?.Invoke(this, EventArgs.Empty);
        }

        private void OpenErrorPopover() {
            if (errorPopover is { } existing) {
                existing.Content = ErrorContent;
                existing.IsOpen = true;
                return;
            }
            if ((PopoverHost ?? this.FindAncestorOfType<PopoverHost>()) is not { } host)
                throw new InvalidOperationException("SolveStatus requires a PopoverHost to open its error popover.");
            if (!this.TryFindResource("Kit.PopoverFrame", null, out object? themeObj) || themeObj is not Avalonia.Styling.ControlTheme theme) {
                IsErrorPopoverOpen = false;
                return;
            }

            PopoverFrame popover = new() { Theme = theme, Anchor = this };
            popover.Bind(PopoverFrame.PanelWidthProperty, popover.GetResourceObservable("Foreman.Size.PopoverWidth"));
            popover.Closed += OnErrorPopoverClosed;
            errorPopover = popover;
            usedHost = host;
            host.Add(popover, () => {
                popover.Content = ErrorContent;
                popover.ApplyTemplate();
                popover.IsOpen = true;
            });
        }

        protected override AutomationPeer OnCreateAutomationPeer() => peer = new SolveStatusAutomationPeer(this);

        internal Control? ActivePart => State switch {
            SolveStatusState.Solving => spinner,
            SolveStatusState.Failed => triangle,
            _ => null,
        };

        private sealed class SolveStatusAutomationPeer(SolveStatus owner) : ControlAutomationPeer(owner) {
            internal void Refresh() => InvalidateChildren();
            protected override bool IsControlElementCore() => false;
            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
                owner.ActivePart is { } part ? [ControlAutomationPeer.CreatePeerForElement(part)] : [];
        }
    }
}
