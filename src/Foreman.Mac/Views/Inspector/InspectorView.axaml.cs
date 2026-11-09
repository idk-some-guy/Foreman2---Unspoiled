using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
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
        private readonly IdentityRow identityRow;
        private readonly Image identityIcon;
        private readonly TextBlock identityName;
        private readonly TextBlock identitySubtitle;
        private readonly StackPanel qualityRow;
        private readonly PopUp qualityMiniSelect;
        private readonly ContentControl segmentHost;
        private readonly StatePanel emptyState;
        private readonly SplitDivider widthDivider;
        private readonly FlowSegmentView flowSegmentView;
        private readonly RecipeSegmentView recipeSegmentView;
        private InspectorViewModel? boundViewModel;

        public GraphViewer? Viewer { get; set; }

        public Action? RequestRedraw { get; set; }

        public ICommand? AddRecipeCommand { get; set; }

        public event EventHandler<double>? WidthCommitted;

        public event EventHandler? OpenPresetsRequested;
        public event EventHandler<IRecipeNodeViewModel>? PickAnotherRecipeRequested;

        public event EventHandler<ProductionGraph.RateUnit>? RateUnitChanged;

        public InspectorView() {
            InitializeComponent();
            identityRow = this.FindControl<IdentityRow>("IdentityRow")!;
            identityIcon = this.FindControl<Image>("IdentityIcon")!;
            identityName = this.FindControl<TextBlock>("IdentityName")!;
            identitySubtitle = this.FindControl<TextBlock>("IdentitySubtitle")!;
            TrimmedTooltip.SetEnabled(identitySubtitle, true);
            qualityRow = this.FindControl<StackPanel>("QualityRow")!;
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

            //The compiled XAML name scope is sealed after InitializeComponent, so controls built in code register into this scope instead.
            flowSegmentView = new FlowSegmentView { Name = "FlowSegment" };
            recipeSegmentView = new RecipeSegmentView { Name = "RecipeSegment" };
            recipeSegmentView.OpenPresetsRequested += (_, _) => OpenPresetsRequested?.Invoke(this, EventArgs.Empty);
            recipeSegmentView.PickAnotherRecipeRequested += (_, node) => PickAnotherRecipeRequested?.Invoke(this, node);
            recipeSegmentView.RateUnitChanged += (_, unit) => RateUnitChanged?.Invoke(this, unit);
            flowSegmentView.RateUnitChanged += (_, unit) => RateUnitChanged?.Invoke(this, unit);
            var scope = new NameScope();
            scope.Register("IdentityRow", identityRow);
            scope.Register("IdentityIcon", identityIcon);
            scope.Register("IdentityName", identityName);
            scope.Register("IdentitySubtitle", identitySubtitle);
            scope.Register("QualityRow", qualityRow);
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
                AutomationProperties.SetName(identityRow, null);
                identityRow.AccessibleValue = null;
                return;
            }

            (string name, string subtitle, SKBitmap? icon) = Describe(node);
            identityName.Text = name;
            identitySubtitle.Text = subtitle;
            identityIcon.Source = icon is null ? null : GetOrBakeIcon(icon);
            AutomationProperties.SetName(identityRow, name);
            identityRow.AccessibleValue = subtitle;

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
            bool shows = recipeViewModel?.ShowsQuality ?? false;
            qualityRow.IsVisible = shows;
            qualityMiniSelect.IsVisible = shows;
            qualityMiniSelect.Items.Clear();
            qualityMiniSelect.ItemIcons.Clear();
            if (recipeViewModel is null) {
                qualityMiniSelect.Icon = null;
                return;
            }
            foreach (PickGridItem option in recipeViewModel.QualityOptions) {
                qualityMiniSelect.Items.Add(option.Name);
                qualityMiniSelect.ItemIcons.Add(option.Icon);
            }
            qualityMiniSelect.SelectedItem = recipeViewModel.CurrentQualityName;
            SKBitmap? icon = recipeViewModel.CurrentQualityIcon;
            qualityMiniSelect.Icon = icon is null ? null : IconBaker.GetOrBake(qualityIconCache, icon, QualityIconSize);
        }

        private void PickQualityByName(string? name) {
            PickGridItem? item = recipeSegmentView.ViewModel?.QualityOptions.FirstOrDefault(i => i.Name == name);
            if (item is null)
                return;
            recipeSegmentView.ViewModel!.PickQuality(item);
            Refresh();
        }

        public bool FocusFirstControl() {
            InputElement? first = this.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(c => c.Focusable && KeyboardNavigation.GetIsTabStop(c) && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
            return first?.Focus(NavigationMethod.Tab) == true;
        }

        public void RefreshSegment() {
            if (segmentHost.Content == flowSegmentView)
                flowSegmentView.Refresh();
            else if (segmentHost.Content == recipeSegmentView)
                recipeSegmentView.Refresh();
        }

        private static (string Name, string Subtitle, SKBitmap? Icon) Describe(INodeViewModel node) => node switch {
            IRecipeNodeViewModel recipe => (
                recipe.BaseRecipe.FriendlyName ?? "",
                $"Recipe node · {recipe.SelectedAssembler.Assembler.FriendlyName}{QualitySuffix(recipe.SelectedAssembler.Quality)}",
                recipe.BaseRecipe.Icon),
            ISupplierNodeViewModel supplier => (supplier.SuppliedItem.FriendlyName ?? "", "Supplier node", supplier.SuppliedItem.Icon),
            IConsumerNodeViewModel consumer => (consumer.ConsumedItem.FriendlyName ?? "", "Consumer node", consumer.ConsumedItem.Icon),
            IPassthroughNodeViewModel passthrough => (passthrough.PassthroughItem.FriendlyName ?? "", "Passthrough node", passthrough.PassthroughItem.Icon),
            ISpoilNodeViewModel spoil => (spoil.InputItem.FriendlyName ?? "", "Spoil node", spoil.InputItem.Icon),
            IPlantNodeViewModel plant => (plant.Seed.FriendlyName ?? "", "Plant node", plant.Seed.Icon),
            _ => ("", "", null),
        };

        private static string QualitySuffix(IQuality quality) =>
            quality.PrevQuality is null ? "" : $" ({quality.FriendlyName})";

        private const int IdentityIconSize = 17;
        private const int QualityIconSize = 16;

        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, IdentityIconSize);

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"InspectorView: kit theme '{key}' not found.");
        }
    }

    internal sealed class IdentityRow : Border {
        public static readonly StyledProperty<string?> AccessibleValueProperty =
            AvaloniaProperty.Register<IdentityRow, string?>(nameof(AccessibleValue));

        public string? AccessibleValue {
            get => GetValue(AccessibleValueProperty);
            set => SetValue(AccessibleValueProperty, value);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new IdentityRowAutomationPeer(this);

        private sealed class IdentityRowAutomationPeer(IdentityRow owner) : ControlAutomationPeer(owner), IValueProvider {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => owner.AccessibleValue;
            void IValueProvider.SetValue(string? value) { }
        }
    }
}
