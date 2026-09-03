using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/IRChooserPanel.cs's RecipeChooserPanel subclass (docs/panels-reference.md §2/§9 step 2/§8):
    //the KeyItem-driven filter (RecipeMatchesKeyItem, including the fluid temperature-range clauses traced in
    //the task-3 report before this file was written), the footer "alt node" buttons, and the spoil/plant
    //multi-origin UnSpoil/UnPlant branches that hand off to GraphCanvasControl's item sub-picker via
    //ChooserPanelCloseReason.RequiresItemSelection instead of closing. Footer buttons use PointerReleased
    //(not Click) purely to read e.KeyModifiers for the Shift-stays-open gesture, same as the grid buttons.
    //Wired via AddHandler(..., handledEventsToo: true) rather than a plain += subscription: Avalonia's own
    //Button marks a real (non-synthetic) press+release pair Handled once it converts that pair into its own
    //Click, which runs ahead of an instance handler added the plain way and would otherwise swallow every
    //genuine mouse click before our handler ever saw it - RecipeRequested only fired for synthetic release-only
    //test events until this was in place.
    public sealed class RecipeChooserPanel : IRChooserPanel {
        public event EventHandler<RecipeChooserRequestEventArgs>? RecipeRequested;

        private readonly DataCache dCache;
        private readonly ItemQualityPair keyItem;
        private readonly FRange keyItemTempRange;
        private readonly bool isDefaultQuality;

        public RecipeChooserPanel(DataCache dCache, AppSettings settings, ItemQualityPair keyItem, FRange tempRange, NewNodeType nodeType) : base(settings) {
            this.dCache = dCache;
            this.keyItem = keyItem;

            QualityPicker.IsVisible = true;
            if (!keyItem)
                QualityPicker.SetQualities(dCache.AvailableQualities.Where(q => q.Enabled));
            else if (keyItem.Quality is IQuality fixedQuality)
                QualityPicker.SetFixedQuality(fixedQuality);

            bool asIngredient = nodeType is NewNodeType.Consumer or NewNodeType.Disconnected;
            bool asProduct = nodeType is NewNodeType.Supplier or NewNodeType.Disconnected;

            AsIngredientCheckBox.IsChecked = asIngredient;
            AsProductCheckBox.IsChecked = asProduct;
            ShowHiddenCheckBox.Content = "Show Disabled";

            AddConsumerButton.AddHandler(PointerReleasedEvent, AddConsumerButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddPassthroughButton.AddHandler(PointerReleasedEvent, AddPassthroughButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddSupplyButton.AddHandler(PointerReleasedEvent, AddSupplyButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddSpoilButton.AddHandler(PointerReleasedEvent, AddSpoilButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddUnspoilButton.AddHandler(PointerReleasedEvent, AddUnspoilButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddPlantButton.AddHandler(PointerReleasedEvent, AddPlantButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddUnplantButton.AddHandler(PointerReleasedEvent, AddUnplantButtonReleased, RoutingStrategies.Bubble, handledEventsToo: true);

            AsIngredientCheckBox.IsCheckedChanged += FilterCheckBoxChanged;
            AsProductCheckBox.IsCheckedChanged += FilterCheckBoxChanged;
            AsFuelCheckBox.IsCheckedChanged += FilterCheckBoxChanged;
            RecipeNameOnlyFilterCheckBox.IsCheckedChanged += FilterCheckBoxChanged;

            keyItemTempRange = nodeType == NewNodeType.Disconnected ? new FRange(0, 0, true) : tempRange;
            isDefaultQuality = !keyItem || keyItem.Quality == dCache.DefaultQuality;

            RecipeNameOnlyFilterCheckBox.IsVisible = true;
            if (keyItem is { Item: IItem realKeyItem }) {
                ItemIconPanel.IsVisible = true;
                ItemIconPanel.SetPopulated(realKeyItem, IRButtonDefaultColor);
                NodeOptionsRowA.IsVisible = true;
                AddConsumerButton.IsVisible = asIngredient;
                AddSupplyButton.IsVisible = asProduct;

                AddSpoilButton.IsVisible = asIngredient && realKeyItem.SpoilResult is not null;
                AddUnspoilButton.IsVisible = asProduct && realKeyItem.SpoilOrigins.Count > 0;
                AddPlantButton.IsVisible = asIngredient && realKeyItem.PlantResult is not null;
                AddUnplantButton.IsVisible = asProduct && isDefaultQuality && realKeyItem.PlantOrigins.Count > 0;
                int totalVisible = (AddSpoilButton.IsVisible ? 1 : 0) + (AddUnspoilButton.IsVisible ? 1 : 0) + (AddPlantButton.IsVisible ? 1 : 0) + (AddUnplantButton.IsVisible ? 1 : 0);
                NodeOptionsRowB.IsVisible = totalVisible > 0;

                bool hasConsumptionRecipes = ShowUnavailable ? realKeyItem.ConsumptionRecipes.Count > 0 : realKeyItem.ConsumptionRecipes.Any(r => r.Available);
                bool hasFuelConsumptionRecipes = isDefaultQuality && realKeyItem.FuelsEntities.Any(a => a is IAssembler { Enabled: true } assembler && assembler.Recipes.Any(r => r.Enabled));
                bool hasProductionRecipes = ShowUnavailable ? realKeyItem.ProductionRecipes.Count > 0 : realKeyItem.ProductionRecipes.Any(r => r.Available);
                bool hasFuelProductionRecipes = isDefaultQuality && realKeyItem.FuelOrigin is not null && realKeyItem.FuelOrigin.FuelsEntities.Any(a => a is IAssembler { Enabled: true } assembler && assembler.Recipes.Any(r => r.Enabled));

                if (!(asIngredient && (hasConsumptionRecipes || hasFuelConsumptionRecipes)) && !(asProduct && (hasProductionRecipes || hasFuelProductionRecipes))) {
                    GroupsPanel.IsVisible = false;
                    IconGrid.IsVisible = false;
                    FilterTextBox.IsVisible = false;
                    FilterLabel.IsVisible = false;
                    RecipeNameOnlyFilterCheckBox.IsVisible = false;
                    ShowHiddenCheckBox.IsVisible = false;
                    IgnoreAssemblerCheckBox.IsVisible = false;
                    RecipeRoleRow.IsVisible = false;
                } else if (asIngredient && asProduct) {
                    RecipeRoleRow.IsVisible = true;
                    AsFuelCheckBox.IsVisible = hasFuelConsumptionRecipes || hasFuelProductionRecipes;
                    AsIngredientCheckBox.IsVisible = true;
                    AsProductCheckBox.IsVisible = true;
                } else if (asIngredient) {
                    RecipeRoleRow.IsVisible = true;
                    AsFuelCheckBox.IsVisible = realKeyItem.FuelsEntities.Count > 0;
                } else if (asProduct) {
                    RecipeRoleRow.IsVisible = true;
                }
            } else {
                NodeOptionsRowA.IsVisible = false;
                NodeOptionsRowB.IsVisible = false;
                RecipeRoleRow.IsVisible = true;
            }
        }

        private static bool RecipeMatchesKeyItem(IRecipe recipe, IItem keyItem, bool includeConsumers, bool includeSuppliers, bool includeFuel, bool ignoreAssemblerStatus, FRange keyItemTempRange) {
            return (includeConsumers && recipe.IngredientSet.ContainsKey(keyItem) && (keyItemTempRange.Ignore || recipe.IngredientTemperatureMap[keyItem].Contains(keyItemTempRange))) ||
                (includeSuppliers && recipe.ProductSet.ContainsKey(keyItem) && (keyItemTempRange.Ignore || keyItemTempRange.Contains(recipe.ProductTemperatureMap[keyItem]))) ||
                (includeConsumers && includeFuel && keyItem.FuelsEntities.Count > 0 && recipe.Assemblers.Any(a => a.Fuels.Contains(keyItem) && (a.Enabled || ignoreAssemblerStatus))) ||
                (includeSuppliers && includeFuel && keyItem.FuelOrigin is IItem fuelOrigin && recipe.Assemblers.Any(a => a.Fuels.Contains(fuelOrigin) && (a.Enabled || ignoreAssemblerStatus)));
        }

        protected override List<IGroup> GetSortedGroups() {
            var groups = new List<IGroup>();
            foreach (IGroup group in ShowUnavailable ? dCache.Groups.Values : dCache.AvailableGroups) {
                int recipeCount = 0;
                foreach (ISubgroup sgroup in group.Subgroups)
                    recipeCount += ShowUnavailable ? sgroup.Recipes.Count : sgroup.Recipes.Count(r => r.Available);
                if (recipeCount > 0)
                    groups.Add(group);
            }
            groups.Sort();
            return groups;
        }

        protected override List<List<KeyValuePair<IDataObjectBase, Color>>> GetSubgroupList() {
            string filterString = FilterTextBox.Text?.ToLowerInvariant() ?? "";
            bool ignoreAssemblerStatus = IgnoreAssemblerCheckBox.IsChecked ?? false;
            bool checkRecipeIPs = !(RecipeNameOnlyFilterCheckBox.IsChecked ?? false);
            bool showHidden = ShowHiddenCheckBox.IsChecked ?? false;
            bool includeSuppliers = AsProductCheckBox.IsChecked ?? false;
            bool includeConsumers = AsIngredientCheckBox.IsChecked ?? false;
            bool includeFuel = (AsFuelCheckBox.IsChecked ?? false) && isDefaultQuality;
            bool ignoreItem = !keyItem;
            IItem? filterKeyItem = keyItem.Item;

            var filteredRecipes = new Dictionary<IGroup, List<List<KeyValuePair<IDataObjectBase, Color>>>>();
            var filteredRecipeCount = new Dictionary<IGroup, int>();
            foreach (IGroup group in SortedGroups ?? []) {
                int recipeCounter = 0;
                var sgList = new List<List<KeyValuePair<IDataObjectBase, Color>>>();
                foreach (ISubgroup sgroup in group.Subgroups) {
                    var recipeList = new List<KeyValuePair<IDataObjectBase, Color>>();
                    foreach (IRecipe recipe in sgroup.Recipes.Where(r => ignoreItem || (filterKeyItem is IItem keyItemForFilter && RecipeMatchesKeyItem(r, keyItemForFilter, includeConsumers, includeSuppliers, includeFuel, ignoreAssemblerStatus, keyItemTempRange)))) {
                        if ((recipe.Enabled || showHidden) && (recipe.Assemblers.Any(a => a.Enabled) || ignoreAssemblerStatus) && (recipe.Available || ShowUnavailable)) {
                            if (recipe.LFriendlyName.Contains(filterString) ||
                                recipe.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase) || (checkRecipeIPs && (
                                recipe.IngredientList.Any(i => i.LFriendlyName.Contains(filterString) || i.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase)) ||
                                recipe.ProductList.Any(i => i.LFriendlyName.Contains(filterString) || i.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase))))) {
                                Color bgColor = !recipe.Enabled ? IRButtonHiddenColor :
                                    (!recipe.Available || !recipe.Assemblers.Any(a => a.Available)) ? IRButtonUnavailableColor :
                                    !recipe.Assemblers.Any(a => a.Enabled) ? IRButtonNoAssemblerColor : IRButtonDefaultColor;
                                recipeCounter++;
                                recipeList.Add(new KeyValuePair<IDataObjectBase, Color>(recipe, bgColor));
                            }
                        }
                    }
                    sgList.Add(recipeList);
                }
                filteredRecipes.Add(group, sgList);
                filteredRecipeCount.Add(group, recipeCounter);
                UpdateGroupButton(group, recipeCounter != 0);
            }

            IGroup? alternateGroup = null;
            if (SelectedGroup is not null && SortedGroups is not null && filteredRecipeCount[SelectedGroup] == 0) {
                int selectedGroupIndex = 0;
                for (int i = 0; i < SortedGroups.Count; i++)
                    if (SortedGroups[i] == SelectedGroup)
                        selectedGroupIndex = i;
                for (int i = selectedGroupIndex; i >= 0; i--)
                    if (filteredRecipeCount[SortedGroups[i]] > 0)
                        alternateGroup = SortedGroups[i];
                if (alternateGroup is null)
                    for (int i = selectedGroupIndex; i < SortedGroups.Count; i++)
                        if (filteredRecipeCount[SortedGroups[i]] > 0)
                            alternateGroup = SortedGroups[i];
                alternateGroup ??= SelectedGroup;
            }
            SetSelectedGroup(alternateGroup ?? SelectedGroup, causeUpdate: false);

            return SelectedGroup is not null ? filteredRecipes[SelectedGroup] : [];
        }

        protected override void IRButtonMouseUp(IconButton button, PointerReleasedEventArgs e) {
            if (button.DataObject is IRecipe recipe && e.InitialPressMouseButton == MouseButton.Left) {
                RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(new RecipeQualityPair(recipe, QualityPicker.SelectedQuality)));
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    ClosePanel(ChooserPanelCloseReason.RecipeSelected);
            } else if (button.DataObject is IRecipe toggled && e.InitialPressMouseButton == MouseButton.Right) {
                toggled.Enabled = !toggled.Enabled;
                UpdateIRButtons();
            }
        }

        private void AddSupplyButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Supplier));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
        }

        private void AddConsumerButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Consumer));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
        }

        private void AddPassthroughButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Passthrough));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
        }

        private void AddSpoilButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Spoil, NodeDirection.Up));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
        }

        private void AddUnspoilButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            if (keyItem.Item is not IItem spoilKeyItem)
                return;
            if (spoilKeyItem.SpoilOrigins.Count < 2) {
                RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Spoil, NodeDirection.Down));
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
            } else {
                PanelCloseReason = ChooserPanelCloseReason.RequiresItemSelection;
                RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Spoil, NodeDirection.Down));
            }
        }

        private void AddPlantButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Plant, NodeDirection.Up));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
        }

        private void AddUnplantButtonReleased(object? sender, PointerReleasedEventArgs e) {
            e.Handled = true;
            if (keyItem.Item is not IItem plantKeyItem)
                return;
            if (plantKeyItem.PlantOrigins.Count < 2) {
                RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Plant, NodeDirection.Down));
                ClosePanel(ChooserPanelCloseReason.AltNodeSelected);
            } else {
                PanelCloseReason = ChooserPanelCloseReason.RequiresItemSelection;
                RecipeRequested?.Invoke(this, new RecipeChooserRequestEventArgs(NodeType.Plant, NodeDirection.Down));
            }
        }
    }

    public sealed class RecipeChooserRequestEventArgs : EventArgs {
        public NodeType NodeType { get; }
        public RecipeQualityPair Recipe { get; }
        public NodeDirection Direction { get; }

        public RecipeChooserRequestEventArgs(RecipeQualityPair recipe) : this(NodeType.Recipe, recipe, NodeDirection.Down) { }
        public RecipeChooserRequestEventArgs(NodeType nodeType) : this(nodeType, default, NodeDirection.Down) { }
        public RecipeChooserRequestEventArgs(NodeType nodeType, NodeDirection direction) : this(nodeType, default, direction) { }

        private RecipeChooserRequestEventArgs(NodeType nodeType, RecipeQualityPair recipe, NodeDirection direction) {
            NodeType = nodeType;
            Recipe = recipe;
            Direction = direction;
        }
    }
}
