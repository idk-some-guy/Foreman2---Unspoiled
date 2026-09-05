using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views.Inspector {
    //Folds the canvas's retired recipe hover tooltip into the inspector's Recipe segment (Task 9, contract
    //§03 CSS 708-718) - a plain data-bound card (RecipeSegmentView binds its DataContext to
    //RecipeSegmentViewModel.Card in XAML), no code-behind wiring beyond loading its own XAML.
    public sealed partial class RecipeCardView : UserControl {
        public RecipeCardView() => InitializeComponent();

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
