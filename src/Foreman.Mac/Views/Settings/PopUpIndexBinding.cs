using Avalonia.Controls;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using PopUp = Foreman.Mac.Kit.Controls.PopUp;

namespace Foreman.Mac.Views.Settings {
    internal static class PopUpIndexBinding {
        public static void Bind(Control pane, PopUp popUp, IReadOnlyList<string> names, string property,
                Func<SettingsViewModel, int> get, Action<SettingsViewModel, int> set) {
            popUp.Items.AddRange(names);
            SettingsViewModel? viewModel = null;

            void Pull() {
                if (viewModel is not null && get(viewModel) is int index && index >= 0 && index < names.Count)
                    popUp.SelectedItem = names[index];
            }

            void OnChanged(object? sender, PropertyChangedEventArgs e) {
                if (e.PropertyName == property)
                    Pull();
            }

            pane.DataContextChanged += (_, _) => {
                viewModel?.PropertyChanged -= OnChanged;
                viewModel = pane.DataContext as SettingsViewModel;
                viewModel?.PropertyChanged += OnChanged;
                Pull();
            };
            popUp.PropertyChanged += (_, e) => {
                if (e.Property == PopUp.SelectedItemProperty && viewModel is not null && popUp.SelectedItem is string name && popUp.Items.IndexOf(name) is int i and >= 0)
                    set(viewModel, i);
            };
        }
    }
}
