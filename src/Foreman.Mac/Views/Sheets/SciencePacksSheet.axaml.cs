using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.Sheets {
    //Ports Forms/SciencePacksLoadForm.cs(+.Designer.cs) (reference io-reference.md §6): a grid of
    //Kit.ChooserCells built from DCache.SciencePacks, an accepted pack shown selected, with a prerequisite
    //cascade in both directions. Confirm re-derives EnabledObjects from the accepted packs, reusing
    //EnabledObjectsDerivation (Foreman.Core, shared with SaveFileLoadSheet's ProcessSaveData - io-
    //reference.md risk 5) for the assembler/beacon/module transitive-enable step rather than a second copy.
    public partial class SciencePacksSheet : SheetContent<bool?> {
        private const int IconSize = 48;

        private readonly DataCache dCache;
        private readonly HashSet<IDataObjectBase> enabledObjects;
        private readonly WrapPanel sciencePackGrid;
        private readonly Button confirmationButton;
        private readonly Button cancellationButton;
        private readonly Dictionary<ChooserCell, bool> sciencePackButtons = [];
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        internal bool Accepted { get; private set; }

        public SciencePacksSheet() : this(new DataCache(false), []) {
        }

        public SciencePacksSheet(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            InitializeComponent();
            dCache = cache;
            this.enabledObjects = enabledObjects;
            Title = "Select researched science packs";

            sciencePackGrid = this.FindControl<WrapPanel>("SciencePackGrid")!;
            cancellationButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            confirmationButton = SheetButtons.Make("Confirm", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancellationButton, confirmationButton);
            DefaultButton = confirmationButton;

            confirmationButton.Click += (_, _) => Confirm();
            cancellationButton.Click += (_, _) => Cancel();

            PopulateSciencePackOptions();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void PopulateSciencePackOptions() {
            Application.Current!.TryGetResource("Kit.ChooserCell", null, out object? cellTheme);
            foreach (IItem sciencePack in dCache.SciencePacks) {
                var cell = new ChooserCell {
                    Theme = cellTheme as ControlTheme,
                    Icon = GetOrBakeIcon(sciencePack.Icon),
                    Label = sciencePack.FriendlyName,
                    FullName = sciencePack.FriendlyName,
                    Focusable = true,
                    Tag = sciencePack,
                };
                cell.Tapped += (_, _) => Toggle(cell);
                cell.Invoked += (_, _) => Toggle(cell);
                cell.KeyDown += (_, e) => {
                    if (e.Key is not Key.Space)
                        return;
                    e.Handled = true;
                    Toggle(cell);
                };

                sciencePackGrid.Children.Add(cell);
                sciencePackButtons.Add(cell, false);
            }
        }

        //Ports Button_Click's cascade (reference §6, SciencePacksLoadForm.cs:79-105) verbatim, including
        //upstream's own documented imprecision (its comment at :87-88, docs/upstream-divergences.md): a
        //science pack reachable through multiple alternate tech-tree branches gets every branch's
        //prerequisites treated as required (AND) instead of any one branch (OR) - not fixed here.
        private void Toggle(ChooserCell clicked) {
            if (clicked.Tag is not IItem sciPack)
                return;

            bool enabled = !sciencePackButtons[clicked];
            SetAccepted(clicked, enabled);

            foreach (ChooserCell cell in sciencePackButtons.Keys.ToArray()) {
                if (cell.Tag is not IItem item)
                    continue;
                if (enabled) {
                    if (dCache.SciencePackPrerequisites[sciPack].Contains(item))
                        SetAccepted(cell, true);
                } else {
                    if (dCache.SciencePackPrerequisites[item].Contains(sciPack))
                        SetAccepted(cell, false);
                }
            }
        }

        private void SetAccepted(ChooserCell cell, bool accepted) {
            sciencePackButtons[cell] = accepted;
            cell.IsSelected = accepted;
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

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal Button ConfirmationButtonControl => confirmationButton;
        internal Button CancellationButtonControl => cancellationButton;
        internal IReadOnlyDictionary<ChooserCell, bool> SciencePackButtonStates => sciencePackButtons;
        internal ChooserCell? PackButtonFor(IItem sciencePack) => sciencePackButtons.Keys.FirstOrDefault(b => ReferenceEquals(b.Tag, sciencePack));
        internal void SimulateConfirmClick() => Confirm();
        internal void SimulateCancelClick() => Cancel();
    }
}
