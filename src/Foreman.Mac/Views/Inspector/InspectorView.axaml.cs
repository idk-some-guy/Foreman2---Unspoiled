using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Foreman.Mac.Views.Inspector {
    public partial class InspectorView : UserControl {
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];
        private readonly Image identityIcon;
        private readonly TextBlock identityName;
        private readonly TextBlock identitySubtitle;
        private readonly Button qualityMiniSelect;
        private readonly TextBlock qualityMiniSelectLabel;
        private readonly PickGrid qualityGrid;
        private readonly ContentControl segmentHost;
        private readonly FlowSegmentView flowSegmentView;
        private readonly RecipeSegmentView recipeSegmentView;
        private InspectorViewModel? boundViewModel;

        //Set by MainWindow right after construction (InspectorView's own DataContext only ever carries the
        //InspectorViewModel, not the canvas) - the Flow segment needs it to build FlowSegmentViewModel.
        public GraphViewer? Viewer { get; set; }

        //Set by MainWindow to GraphCanvas.InvalidateVisual - EditFlowPanel's own RequestRedraw callback,
        //ported here since the floating panel it used to thread that through no longer exists.
        public Action? RequestRedraw { get; set; }

        public InspectorView() {
            InitializeComponent();
            identityIcon = this.FindControl<Image>("IdentityIcon")!;
            identityName = this.FindControl<TextBlock>("IdentityName")!;
            identitySubtitle = this.FindControl<TextBlock>("IdentitySubtitle")!;
            qualityMiniSelect = this.FindControl<Button>("QualityMiniSelect")!;
            qualityMiniSelectLabel = this.FindControl<TextBlock>("QualityMiniSelectLabel")!;
            qualityGrid = this.FindControl<PickGrid>("QualityGrid")!;
            segmentHost = this.FindControl<ContentControl>("SegmentHost")!;

            //The compiled XAML name scope is sealed right after InitializeComponent runs (Avalonia calls
            //INameScope.Complete once loading finishes), so a control built later in code - FlowSegmentView/
            //RecipeSegmentView, swapped into SegmentHost.Content on selection - could never register into
            //it. This scope is ours instead, built once with every existing named part plus the two segment
            //instances Refresh() reuses, so FindControl keeps working for both old and new names.
            flowSegmentView = new FlowSegmentView { Name = "FlowSegment" };
            recipeSegmentView = new RecipeSegmentView { Name = "RecipeSegment" };
            var scope = new NameScope();
            scope.Register("IdentityIcon", identityIcon);
            scope.Register("IdentityName", identityName);
            scope.Register("IdentitySubtitle", identitySubtitle);
            scope.Register("QualityMiniSelect", qualityMiniSelect);
            scope.Register("QualityMiniSelectLabel", qualityMiniSelectLabel);
            scope.Register("QualityGrid", qualityGrid);
            scope.Register("SegmentHost", segmentHost);
            scope.Register("EmptyState", this.FindControl<Control>("EmptyState")!);
            scope.Register("InspectorScroller", this.FindControl<Control>("InspectorScroller")!);
            scope.Register("FlowSegment", flowSegmentView);
            scope.Register("RecipeSegment", recipeSegmentView);
            NameScope.SetNameScope(this, scope);

            qualityGrid.ItemActivated += QualityGrid_ItemActivated;
            DataContextChanged += (_, _) => BindViewModel();
            DetachedFromVisualTree += (_, _) => DisposeBakedIcons();
            BindViewModel();
        }

        private void DisposeBakedIcons() {
            foreach (Bitmap bitmap in bakedIconCache.Values)
                bitmap.Dispose();
            bakedIconCache.Clear();
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

        private void SetQualityMiniSelect(RecipeSegmentViewModel? recipeViewModel) {
            qualityMiniSelect.IsVisible = recipeViewModel?.ShowsQuality ?? false;
            qualityMiniSelectLabel.Text = recipeViewModel?.CurrentQualityName ?? "";
            qualityGrid.Items = recipeViewModel?.QualityOptions ?? [];
        }

        //Picking a quality re-quals the node's assembler directly (RecipeSegmentViewModel.PickQuality), so
        //the identity subtitle and the grid's own selection highlight both need a fresh Refresh() here.
        private void QualityGrid_ItemActivated(object? sender, PickGridItem item) {
            recipeSegmentView.ViewModel?.PickQuality(item);
            Refresh();
            qualityMiniSelect.Flyout?.Hide();
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

        private static (string Name, string Subtitle, SKBitmap? Icon) Describe(INodeViewModel node) => node switch {
            IRecipeNodeViewModel recipe => (
                recipe.BaseRecipe.FriendlyName ?? "",
                $"{recipe.SelectedAssembler.Assembler.FriendlyName} · {recipe.SelectedAssembler.Quality.FriendlyName}",
                recipe.BaseRecipe.Icon),
            ISupplierNodeViewModel supplier => (supplier.SuppliedItem.FriendlyName ?? "", "Supplier node", supplier.SuppliedItem.Icon),
            IConsumerNodeViewModel consumer => (consumer.ConsumedItem.FriendlyName ?? "", "Consumer node", consumer.ConsumedItem.Icon),
            IPassthroughNodeViewModel passthrough => (passthrough.PassthroughItem.FriendlyName ?? "", "Passthrough node", passthrough.PassthroughItem.Icon),
            ISpoilNodeViewModel spoil => (spoil.InputItem.FriendlyName ?? "", "Spoil node", spoil.InputItem.Icon),
            IPlantNodeViewModel plant => (plant.Seed.FriendlyName ?? "", "Plant node", plant.Seed.Icon),
            _ => ("", "", null),
        };

        private const int IdentityIconSize = 18;

        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, IdentityIconSize);
    }
}
