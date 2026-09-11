using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Foreman.Mac.Views {
    //Avalonia stand-in for upstream's virtual-mode ListView row (SettingsForm.LoadUnfilteredList,
    //reference §5): DataObject/Name/RowBackground mirror ListViewItem.Tag/Text/BackColor, IsChecked
    //mirrors ListViewItem.Checked and raises change notification so a bound CheckBox reflects updates
    //the row itself didn't make (Enable All).
    public sealed class EnabledObjectsListItem : INotifyPropertyChanged {
        private static IBrush AvailableBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 255, 255));
        private static IBrush UnavailableBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 192, 203));
        private static IBrush ForegroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));

        //Every live row registers itself here (weakly) so a later RefreshTheme call can push the freshly
        //resolved brushes back out to rows already on screen, not just ones built after the call.
        private static readonly List<WeakReference<EnabledObjectsListItem>> liveInstances = [];

        //Called by SettingsWindow before it builds this session's rows (construction-time seed) and again
        //on every AppearanceHub notification while Settings is open - see docs/upstream-divergences.md.
        internal static void RefreshTheme() {
            AvailableBrush = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Color.FromRgb(255, 255, 255));
            UnavailableBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningBackground", Color.FromRgb(255, 192, 203));
            ForegroundBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Color.FromRgb(0, 0, 0));

            for (int i = liveInstances.Count - 1; i >= 0; i--) {
                if (liveInstances[i].TryGetTarget(out EnabledObjectsListItem? item))
                    item.NotifyThemeChanged();
                else
                    liveInstances.RemoveAt(i);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IDataObjectBase DataObject { get; }
        public string Name { get; }
        public Bitmap? Icon { get; }

        //Read live off the static brushes above rather than baked at construction - a theme flip while
        //Settings is open must repaint rows built before the flip, not just ones built after it.
        public IBrush RowBackground => DataObject.Available ? AvailableBrush : UnavailableBrush;

        private IBrush foreground = ForegroundBrush;
        public IBrush Foreground => foreground;

        //Recipes-tab-only: baked RecipePainter output (docs/panels-reference.md §5's RecipeToolTip hover),
        //set by the window after construction; null for every other category.
        public object? TooltipContent { get; set; }

        private bool isChecked;
        public bool IsChecked {
            get => isChecked;
            set {
                if (isChecked == value)
                    return;
                isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public EnabledObjectsListItem(IDataObjectBase dataObject, Bitmap? icon) {
            DataObject = dataObject;
            Name = dataObject.FriendlyName;
            Icon = icon;
            liveInstances.Add(new WeakReference<EnabledObjectsListItem>(this));
        }

        private void NotifyThemeChanged() {
            foreground = ForegroundBrush;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
        }
    }
}
