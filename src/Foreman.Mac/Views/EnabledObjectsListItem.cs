using Avalonia.Media.Imaging;
using Foreman.DataCaching.DataTypes;
using System.ComponentModel;

namespace Foreman.Mac.Views {
    //Avalonia stand-in for upstream's virtual-mode ListView row (SettingsForm.LoadUnfilteredList,
    //reference §5): DataObject/Name mirror ListViewItem.Tag/Text, IsChecked mirrors ListViewItem.Checked
    //and raises change notification so a bound CheckBox reflects updates the row itself didn't make
    //(Enable All).
    public sealed class EnabledObjectsListItem : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;

        public IDataObjectBase DataObject { get; }
        public string Name { get; }
        public Bitmap? Icon { get; }
        public bool IsUnavailable => !DataObject.Available;

        //Recipes-only: baked RecipePainter output (docs/panels-reference.md §5's RecipeToolTip hover),
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
        }
    }
}
