using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.ViewModels;
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
        private readonly TextBlock recipeSegmentLabel;
        private readonly TextBlock flowSegmentLabel;
        private readonly ContentControl segmentHost;
        private readonly FlowSegmentView flowSegmentView;
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
            recipeSegmentLabel = this.FindControl<TextBlock>("RecipeSegmentLabel")!;
            flowSegmentLabel = this.FindControl<TextBlock>("FlowSegmentLabel")!;
            segmentHost = this.FindControl<ContentControl>("SegmentHost")!;

            //The compiled XAML name scope is sealed right after InitializeComponent runs (Avalonia calls
            //INameScope.Complete once loading finishes), so a control built later in code - FlowSegmentView,
            //swapped into SegmentHost.Content on selection - could never register into it. This scope is
            //ours instead, built once with every existing named part plus the one FlowSegmentView instance
            //Refresh() reuses, so FindControl keeps working for both old and new names.
            flowSegmentView = new FlowSegmentView { Name = "FlowSegment" };
            var scope = new NameScope();
            scope.Register("IdentityIcon", identityIcon);
            scope.Register("IdentityName", identityName);
            scope.Register("IdentitySubtitle", identitySubtitle);
            scope.Register("RecipeSegmentLabel", recipeSegmentLabel);
            scope.Register("FlowSegmentLabel", flowSegmentLabel);
            scope.Register("SegmentHost", segmentHost);
            scope.Register("SegmentControl", this.FindControl<Control>("SegmentControl")!);
            scope.Register("EmptyState", this.FindControl<Control>("EmptyState")!);
            scope.Register("FlowSegment", flowSegmentView);
            NameScope.SetNameScope(this, scope);

            DataContextChanged += (_, _) => BindViewModel();
            BindViewModel();
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
                return;
            }

            (string name, string subtitle, SKBitmap? icon) = Describe(node);
            identityName.Text = name;
            identitySubtitle.Text = subtitle;
            identityIcon.Source = icon is null ? null : GetOrBakeIcon(icon);

            bool recipeActive = boundViewModel!.Segment == InspectorSegment.Recipe;
            recipeSegmentLabel.Classes.Set("active", recipeActive);
            flowSegmentLabel.Classes.Set("active", !recipeActive);

            //Recipe segment host stays empty until Task 5 builds RecipeSegmentView.
            if (!recipeActive && Viewer is GraphViewer viewer) {
                flowSegmentView.Bind(node, viewer, RequestRedraw);
                segmentHost.Content = flowSegmentView;
            } else {
                segmentHost.Content = null;
            }
        }

        //Forwarded from MainWindow's two rate-unit change paths (MenuSetRateUnit, the View Options
        //popover's SelectionChanged) - DesiredSetValue/ActualSetValue read in the new unit immediately, but
        //nothing else tells an already-bound Flow segment its display is stale.
        public void RefreshSegment() {
            if (segmentHost.Content == flowSegmentView)
                flowSegmentView.Refresh();
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

        private Bitmap GetOrBakeIcon(SKBitmap icon) {
            if (!bakedIconCache.TryGetValue(icon, out Bitmap? baked)) {
                baked = BakeIcon(icon);
                bakedIconCache[icon] = baked;
            }
            return baked;
        }

        private static Bitmap BakeIcon(SKBitmap icon) {
            const int size = 28;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(icon, new SKRect(0, 0, size, size), paint);
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }
    }
}
