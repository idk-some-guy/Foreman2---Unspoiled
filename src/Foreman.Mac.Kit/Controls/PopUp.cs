using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;

namespace Foreman.Mac.Kit.Controls {
    public class PopUp : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PopUp);

        public static readonly StyledProperty<string?> SelectedItemProperty =
            AvaloniaProperty.Register<PopUp, string?>(nameof(SelectedItem));

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<PopUp, string?>(nameof(Label));

        public static readonly StyledProperty<ICommand?> CommandProperty =
            AvaloniaProperty.Register<PopUp, ICommand?>(nameof(Command));

        public static readonly StyledProperty<bool> IsPullDownProperty =
            AvaloniaProperty.Register<PopUp, bool>(nameof(IsPullDown));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<PopUp, bool>(nameof(IsOpen));

        //Optional 16pt leading icon (2026-09-17 building-picker ruling): present only when the consumer supplies one.
        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<PopUp, IImage?>(nameof(Icon));

        public static readonly DirectProperty<PopUp, MenuPanel> MenuProperty =
            AvaloniaProperty.RegisterDirect<PopUp, MenuPanel>(nameof(Menu), o => o.Menu);

        public static readonly DirectProperty<PopUp, string?> DisplayTextProperty =
            AvaloniaProperty.RegisterDirect<PopUp, string?>(nameof(DisplayText), o => o.DisplayText);

        private string? displayText;
        private Popup? host;
        private NavigationMethod closeNavigationMethod = NavigationMethod.Pointer;

        public string? SelectedItem {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public ICommand? Command {
            get => GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public bool IsPullDown {
            get => GetValue(IsPullDownProperty);
            set => SetValue(IsPullDownProperty, value);
        }

        public bool IsOpen {
            get => GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public IImage? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string? DisplayText => displayText;

        public AvaloniaList<string> Items { get; } = [];

        //Parallel to Items, matched by index - the per-item icon (Quality's badge). Unset for an iconless pop-up.
        public AvaloniaList<IImage?> ItemIcons { get; } = [];

        //Parallel to Items, matched by index - the face each menu row draws its label in (a font menu). Unset keeps the menu's own.
        public AvaloniaList<FontFamily?> ItemFontFamilies { get; } = [];

        public MenuPanel Menu { get; } = new();

        static PopUp() {
            FocusableProperty.OverrideDefaultValue<PopUp>(true);
            IsPullDownProperty.Changed.AddClassHandler<PopUp>((p, _) => p.PseudoClasses.Set(":pulldown", p.IsPullDown));
            IsOpenProperty.Changed.AddClassHandler<PopUp>((p, e) => p.OnIsOpenChanged((bool)e.NewValue!));
            IconProperty.Changed.AddClassHandler<PopUp>((p, _) => p.PseudoClasses.Set(":hasicon", p.Icon is not null));
            LabelProperty.Changed.AddClassHandler<PopUp>((p, _) => p.UpdateDisplayText());
            SelectedItemProperty.Changed.AddClassHandler<PopUp>((p, _) => p.UpdateDisplayText());
        }

        public PopUp() {
            Menu.RequestClose += (_, _) => IsOpen = false;
            Items.CollectionChanged += OnItemsChanged;
            ItemIcons.CollectionChanged += OnItemsChanged;
            ItemFontFamilies.CollectionChanged += OnItemsChanged;
            UpdateDisplayText();
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

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildMenu();

        private void UpdateDisplayText() => SetAndRaise(DisplayTextProperty, ref displayText, Label ?? SelectedItem);

        private void RebuildMenu() {
            Menu.Items.Clear();
            for (int i = 0; i < Items.Count; i++) {
                string captured = Items[i];
                Menu.Items.Add(new MenuRow {
                    Text = captured,
                    IsChecked = captured == SelectedItem,
                    Icon = i < ItemIcons.Count ? ItemIcons[i] : null,
                    FontFamily = i < ItemFontFamilies.Count ? ItemFontFamilies[i] : null,
                    Activate = () => Commit(captured),
                });
            }
        }

        private void Commit(string item) {
            SelectedItem = item;
            if (Command?.CanExecute(item) == true)
                Command.Execute(item);
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
                return;
            }

            RebuildMenu();
            int current = Items.ToList().IndexOf(SelectedItem ?? "");
            Menu.HighlightedIndex = current >= 0 ? current : (Items.Count > 0 ? 0 : -1);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (IsEnabled)
                IsOpen = !IsOpen;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (IsOpen) {
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
                return;
            }

            //Pop-up style has a value to step through when closed; pull-down has none, so its ↓ opens instead.
            switch (e.Key) {
                case Key.Enter:
                case Key.Space:
                    IsOpen = true;
                    e.Handled = true;
                    break;
                case Key.Down when IsPullDown:
                    IsOpen = true;
                    e.Handled = true;
                    break;
                case Key.Down when !IsPullDown:
                    StepAdjacentValue(1);
                    e.Handled = true;
                    break;
                case Key.Up when !IsPullDown:
                    StepAdjacentValue(-1);
                    e.Handled = true;
                    break;
            }
        }

        //AppKit behaviour: closed, ↑/↓ step to the adjacent value without opening the menu.
        private void StepAdjacentValue(int step) {
            if (Items.Count == 0)
                return;

            int current = Items.ToList().IndexOf(SelectedItem ?? "");
            int next = Math.Clamp((current < 0 ? 0 : current) + step, 0, Items.Count - 1);
            Commit(Items[next]);
        }
    }
}
