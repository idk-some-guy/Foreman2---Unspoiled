using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Views.Inspector {
    //Reusable icon-cell grid backing the Recipe segment's Assembler/Fuel pickers (Task 5). A plain
    //ItemsControl over Button cells rather than IconButton (Canvas/Panels' Skia-painted picker button):
    //this one lives in the Avalonia visual tree proper, so keyboard focus/activation come from Button's own
    //Enter/Space handling instead of a custom PointerReleased wire-up.
    public sealed partial class PickGrid : UserControl {
        public static readonly StyledProperty<IReadOnlyList<PickGridItem>> ItemsProperty =
            AvaloniaProperty.Register<PickGrid, IReadOnlyList<PickGridItem>>(nameof(Items), defaultValue: []);

        private readonly ItemsControl itemsHost;

        public IReadOnlyList<PickGridItem> Items {
            get => GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public event EventHandler<PickGridItem>? ItemActivated;

        public PickGrid() {
            InitializeComponent();
            itemsHost = this.FindControl<ItemsControl>("ItemsHost")!;
            AddHandler(Button.ClickEvent, OnCellClicked);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ItemsProperty)
                itemsHost.ItemsSource = Items;
        }

        //Button.ClickEvent bubbles, so one handler up here catches every cell's click (pointer or keyboard)
        //without wiring each generated container individually.
        private void OnCellClicked(object? sender, RoutedEventArgs e) {
            if (e.Source is Button { DataContext: PickGridItem item })
                ItemActivated?.Invoke(this, item);
        }
    }
}
