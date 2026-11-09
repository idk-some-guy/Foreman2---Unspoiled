using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.Sheets {
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

        internal Button ConfirmationButtonControl => confirmationButton;
        internal IReadOnlyDictionary<ChooserCell, bool> SciencePackButtonStates => sciencePackButtons;
        internal ChooserCell? PackButtonFor(IItem sciencePack) => sciencePackButtons.Keys.FirstOrDefault(b => ReferenceEquals(b.Tag, sciencePack));
        internal void SimulateConfirmClick() => Confirm();
        internal void SimulateCancelClick() => Cancel();
    }
}
