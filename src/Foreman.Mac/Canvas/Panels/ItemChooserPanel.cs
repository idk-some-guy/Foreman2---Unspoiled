using Avalonia.Input;
using Avalonia.Media;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/IRChooserPanel.cs's ItemChooserPanel subclass (docs/panels-reference.md §2/§9 step 2):
    //group/subgroup organization and the filter predicate are a verbatim translation of upstream's onto the
    //Avalonia-flavored base chrome (IsChecked/Content instead of Checked/Text). A null itemList means "every
    //item in the cache" (the Add Item entry point); a non-null one restricts the grid to it (the spoil/plant
    //multi-origin sub-picker, reference §2's selection flows) - upstream's showAllItems/requestedItemList
    //split, same names.
    public sealed class ItemChooserPanel : IRChooserPanel {
        public event EventHandler<ItemChooserRequestEventArgs>? ItemRequested;

        private readonly DataCache dCache;
        private readonly bool showAllItems;
        private readonly HashSet<IItem>? requestedItemList;

        public ItemChooserPanel(DataCache dCache, AppSettings settings, IReadOnlyCollection<IItem>? itemList = null, IQuality? itemQuality = null) : base(settings) {
            this.dCache = dCache;
            showAllItems = itemList is null;
            if (!showAllItems)
                requestedItemList = [.. itemList!];

            QualityPicker.IsVisible = true;
            if (itemQuality is null)
                QualityPicker.SetQualities(dCache.AvailableQualities.Where(q => q.Enabled));
            else
                QualityPicker.SetFixedQuality(itemQuality);
        }

        protected override List<IGroup> GetSortedGroups() {
            var groups = new List<IGroup>();
            if (showAllItems) {
                foreach (IGroup group in ShowUnavailable ? dCache.Groups.Values : dCache.AvailableGroups) {
                    int itemCount = 0;
                    foreach (ISubgroup sgroup in group.Subgroups)
                        itemCount += ShowUnavailable ? sgroup.Items.Count : sgroup.Items.Count(i => i.Available);
                    if (itemCount > 0)
                        groups.Add(group);
                }
            } else {
                foreach (IItem item in requestedItemList ?? []) {
                    if ((ShowUnavailable || item.Available) && item.MySubgroup.MyGroup is IGroup g && !groups.Contains(g))
                        groups.Add(g);
                }
            }
            groups.Sort();
            return groups;
        }

        protected override List<List<KeyValuePair<IDataObjectBase, Color>>> GetSubgroupList() {
            string filterString = FilterTextBox.Text?.ToLowerInvariant() ?? "";
            bool ignoreAssemblerStatus = IgnoreAssemblerCheckBox.IsChecked ?? false;
            bool showHidden = ShowHiddenCheckBox.IsChecked ?? false;

            var filteredItems = new Dictionary<IGroup, List<List<KeyValuePair<IDataObjectBase, Color>>>>();
            var filteredItemCount = new Dictionary<IGroup, int>();
            foreach (IGroup group in SortedGroups ?? []) {
                int itemCounter = 0;
                var sgList = new List<List<KeyValuePair<IDataObjectBase, Color>>>();
                foreach (ISubgroup sgroup in group.Subgroups) {
                    var itemList = new List<KeyValuePair<IDataObjectBase, Color>>();
                    foreach (IItem item in sgroup.Items.Where(i =>
                        (ShowUnavailable || i.Available) &&
                        (i.LFriendlyName.Contains(filterString) || i.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase)))) {
                        if (!showAllItems && requestedItemList?.Contains(item) is not true)
                            continue;

                        bool visible = (ShowUnavailable || item.Available) &&
                            (item.ConsumptionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available)) ||
                             item.ProductionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available)));
                        bool validAssembler =
                            item.ConsumptionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available) && r.Assemblers.Any(a => a.Enabled && (ShowUnavailable || a.Available))) ||
                            item.ProductionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available) && r.Assemblers.Any(a => a.Enabled && (ShowUnavailable || a.Available)));

                        Color bgColor = visible && item.Available
                            ? validAssembler ? IRButtonDefaultColor : IRButtonNoAssemblerColor
                            : IRButtonHiddenColor;

                        if ((visible || showHidden) && (validAssembler || ignoreAssemblerStatus)) {
                            itemCounter++;
                            itemList.Add(new KeyValuePair<IDataObjectBase, Color>(item, bgColor));
                        }
                    }
                    sgList.Add(itemList);
                }
                filteredItems.Add(group, sgList);
                filteredItemCount.Add(group, itemCounter);
                UpdateGroupButton(group, itemCounter != 0);
            }

            IGroup? alternateGroup = null;
            if (SelectedGroup is not null && SortedGroups is not null && filteredItemCount[SelectedGroup] == 0) {
                int selectedGroupIndex = 0;
                for (int i = 0; i < SortedGroups.Count; i++)
                    if (SortedGroups[i] == SelectedGroup)
                        selectedGroupIndex = i;
                for (int i = selectedGroupIndex; i >= 0; i--)
                    if (filteredItemCount[SortedGroups[i]] > 0)
                        alternateGroup = SortedGroups[i];
                if (alternateGroup is null)
                    for (int i = selectedGroupIndex; i < SortedGroups.Count; i++)
                        if (filteredItemCount[SortedGroups[i]] > 0)
                            alternateGroup = SortedGroups[i];
                alternateGroup ??= SelectedGroup;
            }
            SetSelectedGroup(alternateGroup ?? SelectedGroup, causeUpdate: false);

            return SelectedGroup is not null ? filteredItems[SelectedGroup] : [];
        }

        protected override void IRButtonMouseUp(IconButton button, PointerReleasedEventArgs e) {
            if (button.DataObject is IItem item && e.InitialPressMouseButton == MouseButton.Left) {
                var picked = new ItemQualityPair(item, QualityPicker.SelectedQuality);
                ItemRequested?.Invoke(this, new ItemChooserRequestEventArgs(picked));
                ClosePanel(ChooserPanelCloseReason.ItemSelected);
            }
        }
    }

    public sealed class ItemChooserRequestEventArgs(ItemQualityPair item) : EventArgs {
        public ItemQualityPair Item { get; } = item;
    }
}
