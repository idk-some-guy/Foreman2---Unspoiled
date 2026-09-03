using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
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

        //Called once by SettingsWindow before it builds this session's rows, ahead of the app's active
        //theme variant - see EnabledObjectsListItem's constructor below and docs/upstream-divergences.md.
        internal static void RefreshTheme() {
            AvailableBrush = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Color.FromRgb(255, 255, 255));
            UnavailableBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningBackground", Color.FromRgb(255, 192, 203));
            ForegroundBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Color.FromRgb(0, 0, 0));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IDataObjectBase DataObject { get; }
        public string Name { get; }
        public Bitmap? Icon { get; }
        public IBrush RowBackground { get; }
        public IBrush Foreground { get; } = ForegroundBrush;

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
            RowBackground = dataObject.Available ? AvailableBrush : UnavailableBrush;
        }
    }
}
