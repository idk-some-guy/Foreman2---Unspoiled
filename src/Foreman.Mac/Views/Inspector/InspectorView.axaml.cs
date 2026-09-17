using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace Foreman.Mac.Views.Inspector {
    public partial class InspectorView : UserControl {
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];
        private readonly Dictionary<SKBitmap, Bitmap> qualityIconCache = [];
        private readonly Image identityIcon;
        private readonly TextBlock identityName;
        private readonly TextBlock identitySubtitle;
        private readonly StackPanel qualityRow;
        private readonly Image qualityIcon;
        private readonly PopUp qualityMiniSelect;
        private readonly ContentControl segmentHost;
        private readonly StatePanel emptyState;
        private readonly SplitDivider widthDivider;
        private readonly FlowSegmentView flowSegmentView;
        private readonly RecipeSegmentView recipeSegmentView;
        private InspectorViewModel? boundViewModel;

        //Set by MainWindow right after construction (InspectorView's own DataContext only ever carries the
        //InspectorViewModel, not the canvas) - the Flow segment needs it to build FlowSegmentViewModel.
        public GraphViewer? Viewer { get; set; }

        //Set by MainWindow to GraphCanvas.InvalidateVisual - EditFlowPanel's own RequestRedraw callback,
        //ported here since the floating panel it used to thread that through no longer exists.
        public Action? RequestRedraw { get; set; }

        //Set by MainWindow to Commands.AddRecipeCommand - the no-selection state panel's own action.
        public ICommand? AddRecipeCommand { get; set; }

        //Fires once per resize gesture (SplitDivider.ValueCommitted) - the seam MainWindow saves through.
        public event EventHandler<double>? WidthCommitted;

        //Forwarded from the Recipe segment's error panel (A25 .error) - MainWindow wires these to ShowSettings/OpenBrowser.
        public event EventHandler? OpenPresetsRequested;
        public event EventHandler<IRecipeNodeViewModel>? PickAnotherRecipeRequested;

        public InspectorView() {
            InitializeComponent();
            identityIcon = this.FindControl<Image>("IdentityIcon")!;
            identityName = this.FindControl<TextBlock>("IdentityName")!;
            identitySubtitle = this.FindControl<TextBlock>("IdentitySubtitle")!;
            qualityRow = this.FindControl<StackPanel>("QualityRow")!;
            qualityIcon = this.FindControl<Image>("QualityIcon")!;
            qualityMiniSelect = this.FindControl<PopUp>("QualityMiniSelect")!;
            qualityMiniSelect.Command = new RelayCommand<string>(PickQualityByName);
            segmentHost = this.FindControl<ContentControl>("SegmentHost")!;
            emptyState = this.FindControl<StatePanel>("EmptyState")!;
            widthDivider = this.FindControl<SplitDivider>("WidthDivider")!;
            widthDivider.ValueCommitted += (_, value) => WidthCommitted?.Invoke(this, value);
            var addRecipeButton = new Button { Content = "Add Recipe…", Theme = KitTheme("Kit.Button") };
            ButtonRow.SetRole(addRecipeButton, ButtonRowRole.Default);
            addRecipeButton.Click += (_, _) => AddRecipeCommand?.Execute(null);
            emptyState.Buttons = new ButtonRow { Theme = KitTheme("Kit.ButtonRow"), Items = { addRecipeButton } };

            //The compiled XAML name scope is sealed right after InitializeComponent runs (Avalonia calls
            //INameScope.Complete once loading finishes), so a control built later in code - FlowSegmentView/
            //RecipeSegmentView, swapped into SegmentHost.Content on selection - could never register into
            //it. This scope is ours instead, built once with every existing named part plus the two segment
            //instances Refresh() reuses, so FindControl keeps working for both old and new names.
            flowSegmentView = new FlowSegmentView { Name = "FlowSegment" };
            recipeSegmentView = new RecipeSegmentView { Name = "RecipeSegment" };
            recipeSegmentView.OpenPresetsRequested += (_, _) => OpenPresetsRequested?.Invoke(this, EventArgs.Empty);
            recipeSegmentView.PickAnotherRecipeRequested += (_, node) => PickAnotherRecipeRequested?.Invoke(this, node);
            var scope = new NameScope();
            scope.Register("IdentityIcon", identityIcon);
            scope.Register("IdentityName", identityName);
            scope.Register("IdentitySubtitle", identitySubtitle);
            scope.Register("QualityRow", qualityRow);
            scope.Register("QualityIcon", qualityIcon);
            scope.Register("QualityMiniSelect", qualityMiniSelect);
            scope.Register("SegmentHost", segmentHost);
            scope.Register("EmptyState", emptyState);
            scope.Register("InspectorScroller", this.FindControl<Control>("InspectorScroller")!);
            scope.Register("FlowSegment", flowSegmentView);
            scope.Register("RecipeSegment", recipeSegmentView);
            NameScope.SetNameScope(this, scope);

            DataContextChanged += (_, _) => BindViewModel();
            DetachedFromVisualTree += (_, _) => DisposeBakedIcons();
            BindViewModel();

            //Baked icons carry no theme tint of their own; dispose-then-Refresh rebakes and reassigns
            //Source synchronously, so nothing paints a disposed bitmap in between.
            ThemeAware.Attach(this, () => {
                DisposeBakedIcons();
                Refresh();
            });
        }

        private void DisposeBakedIcons() {
            foreach (Bitmap bitmap in bakedIconCache.Values)
                bitmap.Dispose();
            bakedIconCache.Clear();
            foreach (Bitmap bitmap in qualityIconCache.Values)
                bitmap.Dispose();
            qualityIconCache.Clear();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void BindViewModel() {
            if (boundViewModel is not null)
                boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            boundViewModel = DataContext as InspectorViewModel;
            if (boundViewModel is not null)
                boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            Refresh();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName is nameof(InspectorViewModel.Node) or nameof(InspectorViewModel.Segment))
                Refresh();
        }

        private void Refresh() {
            INodeViewModel? node = boundViewModel?.Node;
            if (node is null) {
                identityIcon.Source = null;
                identityName.Text = "";
                identitySubtitle.Text = "";
                segmentHost.Content = null;
                SetQualityMiniSelect(null);
                return;
            }

            (string name, string subtitle, SKBitmap? icon) = Describe(node);
            identityName.Text = name;
            identitySubtitle.Text = subtitle;
            identityIcon.Source = icon is null ? null : GetOrBakeIcon(icon);

            bool recipeActive = boundViewModel!.Segment == InspectorSegment.Recipe;

            if (Viewer is not GraphViewer viewer) {
                segmentHost.Content = null;
                SetQualityMiniSelect(null);
            } else if (recipeActive && node is IRecipeNodeViewModel recipeNode && viewer.Context.DCache is DataCache cache) {
                recipeSegmentView.Bind(recipeNode, viewer, cache, RequestRedraw);
                segmentHost.Content = recipeSegmentView;
                SetQualityMiniSelect(recipeSegmentView.ViewModel);
            } else if (!recipeActive) {
                flowSegmentView.Bind(node, viewer, RequestRedraw);
                segmentHost.Content = flowSegmentView;
                SetQualityMiniSelect(null);
            } else {
                segmentHost.Content = null;
                SetQualityMiniSelect(null);
            }
        }

        //Kit.PopUp has no leading-icon slot of its own, so the badge sits beside it as a plain Image
        //here - the shipped means, ported as-is from before the PopUp migration.
        private void SetQualityMiniSelect(RecipeSegmentViewModel? recipeViewModel) {
            bool shows = recipeViewModel?.ShowsQuality ?? false;
            qualityRow.IsVisible = shows;
            qualityMiniSelect.IsVisible = shows;
            qualityMiniSelect.Items.Clear();
            if (recipeViewModel is null) {
                qualityIcon.Source = null;
                return;
            }
            foreach (PickGridItem option in recipeViewModel.QualityOptions)
                qualityMiniSelect.Items.Add(option.Name);
            qualityMiniSelect.SelectedItem = recipeViewModel.CurrentQualityName;
            SKBitmap? icon = recipeViewModel.CurrentQualityIcon;
            qualityIcon.Source = icon is null ? null : IconBaker.GetOrBake(qualityIconCache, icon, QualityIconSize);
        }

        //Picking a quality re-quals the node's assembler directly (RecipeSegmentViewModel.PickQuality), so
        //the identity subtitle and the mini-select's own label both need a fresh Refresh() here.
        private void PickQualityByName(string? name) {
            PickGridItem? item = recipeSegmentView.ViewModel?.QualityOptions.FirstOrDefault(i => i.Name == name);
            if (item is null)
                return;
            recipeSegmentView.ViewModel!.PickQuality(item);
            Refresh();
        }

        //Forwarded from MainWindow's two rate-unit change paths (MenuSetRateUnit, the View Options
        //popover's SelectionChanged) - DesiredSetValue/ActualSetValue read in the new unit immediately, but
        //nothing else tells an already-bound segment its display is stale.
        public void RefreshSegment() {
            if (segmentHost.Content == flowSegmentView)
                flowSegmentView.Refresh();
            else if (segmentHost.Content == recipeSegmentView)
                recipeSegmentView.Refresh();
        }

        //A23: sub-line names the node type then the building; quality lives in the mini-select instead.
        private static (string Name, string Subtitle, SKBitmap? Icon) Describe(INodeViewModel node) => node switch {
            IRecipeNodeViewModel recipe => (
                recipe.BaseRecipe.FriendlyName ?? "",
                $"Recipe node · {recipe.SelectedAssembler.Assembler.FriendlyName}",
                recipe.BaseRecipe.Icon),
            ISupplierNodeViewModel supplier => (supplier.SuppliedItem.FriendlyName ?? "", "Supplier node", supplier.SuppliedItem.Icon),
            IConsumerNodeViewModel consumer => (consumer.ConsumedItem.FriendlyName ?? "", "Consumer node", consumer.ConsumedItem.Icon),
            IPassthroughNodeViewModel passthrough => (passthrough.PassthroughItem.FriendlyName ?? "", "Passthrough node", passthrough.PassthroughItem.Icon),
            ISpoilNodeViewModel spoil => (spoil.InputItem.FriendlyName ?? "", "Spoil node", spoil.InputItem.Icon),
            IPlantNodeViewModel plant => (plant.Seed.FriendlyName ?? "", "Plant node", plant.Seed.Icon),
            _ => ("", "", null),
        };

        private const int IdentityIconSize = 17;
        private const int QualityIconSize = 16;

        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, IdentityIconSize);

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"InspectorView: kit theme '{key}' not found.");
        }
    }
}
