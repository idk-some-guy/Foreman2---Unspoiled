using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.Sheets {
    //Ports Forms/SciencePacksLoadForm.cs(+.Designer.cs) (reference io-reference.md §6): a 48px icon-button
    //grid (MaxColumns 14) built from DCache.SciencePacks, toggled Error/Accent, with a prerequisite
    //cascade in both directions. Confirm re-derives EnabledObjects from the accepted packs, reusing
    //EnabledObjectsDerivation (Foreman.Core, shared with SaveFileLoadSheet's ProcessSaveData - io-
    //reference.md risk 5) for the assembler/beacon/module transitive-enable step rather than a second copy.
    //C2c Task 2 moved this off SciencePacksWindow onto SheetHost: SheetWidth tracks the grid's own computed
    //column count, matching the former SizeToContent="WidthAndHeight" window.
    public partial class SciencePacksSheet : SheetContent<bool?> {
        private const int IconSize = 48;
        private const int MaxColumns = 14;

        private readonly DataCache dCache;
        private readonly HashSet<IDataObjectBase> enabledObjects;
        private readonly UniformGrid sciencePackGrid;
        private readonly Button confirmationButton;
        private readonly Button cancellationButton;
        private readonly Dictionary<Button, bool> sciencePackButtons = [];
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        internal bool Accepted { get; private set; }

        public SciencePacksSheet() : this(new DataCache(false), []) {
        }

        public SciencePacksSheet(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            InitializeComponent();
            dCache = cache;
            this.enabledObjects = enabledObjects;
            Title = "Select researched science packs";

            sciencePackGrid = this.FindControl<UniformGrid>("SciencePackGrid")!;
            confirmationButton = this.FindControl<Button>("ConfirmationButton")!;
            cancellationButton = this.FindControl<Button>("CancellationButton")!;
            DefaultButton = confirmationButton;

            confirmationButton.Click += (_, _) => Confirm();
            cancellationButton.Click += (_, _) => Cancel();

            PopulateSciencePackOptions();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports PopulateSciencePackOptions' row/column balancing (reference §6, SciencePacksLoadForm.cs:36-45):
        //a near-square grid capped at MaxColumns wide, not a plain wrap at 14 columns.
        internal static (int Rows, int Columns) ComputeGridDimensions(int count) {
            int rows = (count / MaxColumns) + (count % MaxColumns > 0 ? 1 : 0);
            int columns = rows == 0 ? 0 : (count / rows) + (count % rows > 0 ? 1 : 0);
            return (rows, columns);
        }

        private void PopulateSciencePackOptions() {
            (int rows, int columns) = ComputeGridDimensions(dCache.SciencePacks.Count);
            sciencePackGrid.Rows = rows;
            sciencePackGrid.Columns = columns;
            SheetWidth = columns * IconSize;

            foreach (IItem sciencePack in dCache.SciencePacks) {
                var button = new Button {
                    Width = IconSize,
                    Height = IconSize,
                    Padding = default,
                    Background = DeselectedBrush(),
                    Tag = sciencePack,
                    Content = new Image { Source = GetOrBakeIcon(sciencePack.Icon), Stretch = Stretch.Uniform },
                };
                ToolTip.SetTip(button, sciencePack.FriendlyName);
                button.Click += Button_Click;

                sciencePackGrid.Children.Add(button);
                sciencePackButtons.Add(button, false);
            }
        }

        //Ports Button_Click's cascade (reference §6, SciencePacksLoadForm.cs:79-105) verbatim, including
        //upstream's own documented imprecision (its comment at :87-88, docs/upstream-divergences.md): a
        //science pack reachable through multiple alternate tech-tree branches gets every branch's
        //prerequisites treated as required (AND) instead of any one branch (OR) - not fixed here.
        private void Button_Click(object? sender, RoutedEventArgs e) {
            if (sender is not Button clicked || clicked.Tag is not IItem sciPack)
                return;

            bool enabled = !sciencePackButtons[clicked];
            sciencePackButtons[clicked] = enabled;
            clicked.Background = enabled ? SelectedBrush() : DeselectedBrush();

            foreach (Button button in sciencePackButtons.Keys.ToArray()) {
                if (button.Tag is not IItem item)
                    continue;
                if (enabled) {
                    if (dCache.SciencePackPrerequisites[sciPack].Contains(item)) {
                        button.Background = SelectedBrush();
                        sciencePackButtons[button] = true;
                    }
                } else {
                    if (dCache.SciencePackPrerequisites[item].Contains(sciPack)) {
                        button.Background = DeselectedBrush();
                        sciencePackButtons[button] = false;
                    }
                }
            }
        }

        //Ports ConfirmationButton_Click (reference §6, SciencePacksLoadForm.cs:121-161).
        private void Confirm() {
            var accepted = sciencePackButtons.Where(kvp => kvp.Value).Select(kvp => (IItem)kvp.Key.Tag!).ToHashSet();

            EnabledObjectsDerivation.ResetToPlayerAssembler(dCache, enabledObjects);
            foreach (ITechnology tech in dCache.Technologies.Values)
                if (tech.Available && !tech.SciPackList.Except(accepted).Any())
                    enabledObjects.UnionWith(tech.UnlockedRecipes);
            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(dCache, enabledObjects);

            Accepted = true;
            Complete(true);
        }

        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, IconSize);

        private static IBrush SelectedBrush() => ThemeResources.ResolveBrush("Foreman.Color.Accent", Color.FromRgb(0x00, 0x7A, 0xFF));

        //was a solid DarkRed, not a pastel: vivid Error keeps the strength
        private static IBrush DeselectedBrush() => ThemeResources.ResolveBrush("Foreman.Color.Error", Color.FromRgb(0xD7, 0x00, 0x15));

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal UniformGrid SciencePackGridControl => sciencePackGrid;
        internal Button ConfirmationButtonControl => confirmationButton;
        internal Button CancellationButtonControl => cancellationButton;
        internal IReadOnlyDictionary<Button, bool> SciencePackButtonStates => sciencePackButtons;
        internal Button? PackButtonFor(IItem sciencePack) => sciencePackButtons.Keys.FirstOrDefault(b => ReferenceEquals(b.Tag, sciencePack));
        internal void SimulateConfirmClick() => Confirm();
        internal void SimulateCancelClick() => Cancel();
    }
}
