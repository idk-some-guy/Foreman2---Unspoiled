using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Linq;

namespace Foreman.Mac.Kit.Controls {
    public class MenuButton : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(MenuButton);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<MenuButton, string?>(nameof(Label));

        public static readonly StyledProperty<Geometry?> GlyphProperty =
            AvaloniaProperty.Register<MenuButton, Geometry?>(nameof(Glyph));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<MenuButton, bool>(nameof(IsOpen));

        public static readonly DirectProperty<MenuButton, bool> IsFilledProperty =
            AvaloniaProperty.RegisterDirect<MenuButton, bool>(nameof(IsFilled), o => o.IsFilled);

        public static readonly DirectProperty<MenuButton, string?> DisplayLabelProperty =
            AvaloniaProperty.RegisterDirect<MenuButton, string?>(nameof(DisplayLabel), o => o.DisplayLabel);

        public static readonly DirectProperty<MenuButton, MenuPanel> MenuProperty =
            AvaloniaProperty.RegisterDirect<MenuButton, MenuPanel>(nameof(Menu), o => o.Menu);

        private bool isFilled;
        private Popup? host;
        private string? displayLabel;
        private NavigationMethod closeNavigationMethod = NavigationMethod.Pointer;

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public Geometry? Glyph {
            get => GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public bool IsOpen {
            get => GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public bool IsFilled => isFilled;

        public string? DisplayLabel => displayLabel;

        public AvaloniaList<MenuButtonItem> Items { get; } = [];

        //Plain one-shot actions (no check slot, no Reset row) - what a toolbar overflow moves its trailing group's commands into. Mutually exclusive with Items.
        public AvaloniaList<MenuButtonAction> ActionItems { get; } = [];

        public MenuPanel Menu { get; } = new();

        static MenuButton() {
            FocusableProperty.OverrideDefaultValue<MenuButton>(true);
            IsOpenProperty.Changed.AddClassHandler<MenuButton>((b, e) => b.OnIsOpenChanged((bool)e.NewValue!));
            LabelProperty.Changed.AddClassHandler<MenuButton>((b, _) => b.Recompute());
        }

        public MenuButton() {
            Menu.RequestClose += (_, _) => IsOpen = false;
            Items.CollectionChanged += (_, _) => { Recompute(); RebuildMenu(); };
            ActionItems.CollectionChanged += (_, _) => RebuildMenu();
            Recompute();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (host is not null)
                host.Closed -= OnHostClosed;
            host = e.NameScope.Find<Popup>("PART_Popup");
            if (host is null)
                return;
            host.Closed += OnHostClosed;
            host.IsOpen = IsOpen;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            RebuildMenu();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            IsOpen = false;
        }

        //Light dismiss closes the templated Popup directly; without this the control stays :open and its Popup never reopens.
        private void OnHostClosed(object? sender, EventArgs e) {
            closeNavigationMethod = NavigationMethod.Pointer;
            IsOpen = false;
        }

        private void OnIsOpenChanged(bool isOpen) {
            PseudoClasses.Set(":open", isOpen);
            if (host is not null)
                host.IsOpen = isOpen;
            if (!isOpen) {
                NavigationMethod method = closeNavigationMethod;
                closeNavigationMethod = NavigationMethod.Pointer;
                if (VisualRoot is not null)
                    Focus(method);
            }
        }

        private void Recompute() {
            bool filled = Items.Any(i => i.IsChecked != i.IsDefault);
            SetAndRaise(IsFilledProperty, ref isFilled, filled);
            PseudoClasses.Set(":filled", filled);

            int nonDefault = Items.Count(i => i.IsChecked != i.IsDefault);
            string? next = filled ? $"{Label} ({nonDefault})" : Label;
            SetAndRaise(DisplayLabelProperty, ref displayLabel, next);
        }

        private void RebuildMenu() {
            int keepHighlight = Menu.HighlightedIndex;
            Menu.Items.Clear();

            if (ActionItems.Count > 0) {
                foreach (MenuButtonAction action in ActionItems) {
                    MenuButtonAction captured = action;
                    Menu.Items.Add(new MenuRow {
                        Text = action.Text,
                        HasCheckSlot = false,
                        IsEnabled = action.IsEnabled,
                        KeepOpenOnActivate = false,
                        Activate = () => captured.Activate?.Invoke(),
                    });
                }
                if (keepHighlight >= 0 && keepHighlight < Menu.Items.Count)
                    Menu.HighlightedIndex = keepHighlight;
                return;
            }

            foreach (MenuButtonItem item in Items) {
                MenuButtonItem captured = item;
                Menu.Items.Add(new MenuRow {
                    Text = item.Text,
                    IsChecked = item.IsChecked,
                    KeepOpenOnActivate = true,
                    Activate = () => {
                        captured.IsChecked = !captured.IsChecked;
                        Recompute();
                        RebuildMenu();
                    },
                });
            }

            Menu.Items.Add(MenuRow.Separator());
            Menu.Items.Add(new MenuRow {
                Text = "Reset to defaults",
                HasCheckSlot = false,
                IsEnabled = isFilled,
                KeepOpenOnActivate = false,
                Activate = () => {
                    foreach (MenuButtonItem item in Items)
                        item.IsChecked = item.IsDefault;
                    Recompute();
                    RebuildMenu();
                },
            });

            if (keepHighlight >= 0 && keepHighlight < Menu.Items.Count)
                Menu.HighlightedIndex = keepHighlight;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (IsEnabled)
                Open();
        }

        private void Open() {
            RebuildMenu();
            IsOpen = true;
            Menu.HighlightedIndex = Menu.Items.Count > 0 ? 0 : -1;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (!IsOpen) {
                if (e.Key is Key.Space or Key.Down or Key.Enter) {
                    Open();
                    e.Handled = true;
                }
                return;
            }

            switch (e.Key) {
                case Key.Down:
                    Menu.MoveHighlight(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Menu.MoveHighlight(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Space:
                    closeNavigationMethod = NavigationMethod.Tab;
                    Menu.ActivateHighlighted();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    closeNavigationMethod = NavigationMethod.Tab;
                    IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }
    }
}
