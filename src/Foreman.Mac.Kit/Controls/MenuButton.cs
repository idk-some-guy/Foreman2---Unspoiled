using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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
        private string? displayLabel;

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

        public MenuPanel Menu { get; } = new();

        static MenuButton() {
            FocusableProperty.OverrideDefaultValue<MenuButton>(true);
            IsOpenProperty.Changed.AddClassHandler<MenuButton>((b, e) => b.PseudoClasses.Set(":open", (bool)e.NewValue!));
            LabelProperty.Changed.AddClassHandler<MenuButton>((b, _) => b.Recompute());
        }

        public MenuButton() {
            Menu.RequestClose += (_, _) => IsOpen = false;
            Items.CollectionChanged += (_, _) => Recompute();
            Recompute();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (Menu.Theme is null && this.TryGetResource("Kit.MenuPanel", ActualThemeVariant, out object? theme) && theme is ControlTheme controlTheme)
                Menu.Theme = controlTheme;
            RebuildMenu();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            IsOpen = false;
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
                    Menu.ActivateHighlighted();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }
    }
}
