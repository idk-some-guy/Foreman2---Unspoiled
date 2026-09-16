using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //Gap owed before a child, resolved to a real number at arrange time - never a margin, so the collapse search below can read it back.
    internal enum ToolbarGapKind { None, Group, Flex }

    //Kit.ToolbarRow's layout: leading-to-trailing children with title/zoom fixed, three collapse steps (icon-only, search shrink, trailing group to overflow) as width runs out.
    public sealed class ToolbarRowPanel : Panel {
        private readonly List<ToolbarGapKind> gaps = [];
        private List<ToolbarItem> allItems = [];
        private List<StackPanel> groupPanels = [];
        private readonly Dictionary<ToolbarItem, double> labeledWidths = [];
        private readonly Dictionary<ToolbarItem, double> iconOnlyWidths = [];
        private readonly Dictionary<Control, Control> gapSpacerBefore = [];
        private readonly double[] requiredByStep = new double[4];
        private StackPanel? overflowGroupPanel;
        private Control? overflowGroupSpacer;
        private MenuButton? overflowButton;
        private Control? overflowSpacer;
        private SearchField? searchField;

        private double groupGap = 16;
        private double sidePadding = 12;
        private double searchNaturalWidth = 220;
        private double searchMinWidth = 120;
        private double itemWidthToken = 44;
        private double iconOnlyWidthToken = 28;
        private double glyph17 = 17;
        private Thickness itemPadding = new(8, 3);
        private double labelFontSize = 9.5;
        private FontWeight labelFontWeight = FontWeight.Medium;
        private bool itemWidthsDirty;
        private readonly List<IDisposable> subs = [];

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Space.Space4").Subscribe(new ResourceSinks.DoubleSink(v => { groupGap = v; RestyleSpacers(); RecomputeRequiredWidths(); InvalidateMeasure(); }, groupGap)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => { sidePadding = v; RecomputeRequiredWidths(); InvalidateMeasure(); }, sidePadding)));
            subs.Add(this.GetResourceObservable("Foreman.Metrics.SearchFieldNaturalWidth").Subscribe(new ResourceSinks.DoubleSink(v => { searchNaturalWidth = v; RecomputeRequiredWidths(); ReapplySearchWidth(); InvalidateMeasure(); }, searchNaturalWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Metrics.SearchFieldMinWidth").Subscribe(new ResourceSinks.DoubleSink(v => { searchMinWidth = v; RecomputeRequiredWidths(); ReapplySearchWidth(); InvalidateMeasure(); }, searchMinWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.ToolbarItemWidth").Subscribe(new ResourceSinks.DoubleSink(v => { itemWidthToken = v; itemWidthsDirty = true; InvalidateMeasure(); }, itemWidthToken)));
            subs.Add(this.GetResourceObservable("Foreman.Size.ToolbarItemIconOnlyWidth").Subscribe(new ResourceSinks.DoubleSink(v => { iconOnlyWidthToken = v; itemWidthsDirty = true; InvalidateMeasure(); }, iconOnlyWidthToken)));
            subs.Add(this.GetResourceObservable("Foreman.Size.Glyph17").Subscribe(new ResourceSinks.DoubleSink(v => { glyph17 = v; itemWidthsDirty = true; InvalidateMeasure(); }, glyph17)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.ToolbarItemPadding").Subscribe(new ResourceSinks.ThicknessSink(v => { itemPadding = v; itemWidthsDirty = true; InvalidateMeasure(); }, itemPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Legacy.ToolbarItem.Label.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => { labelFontSize = v; itemWidthsDirty = true; InvalidateMeasure(); }, labelFontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Legacy.ToolbarItem.Label.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { labelFontWeight = v; itemWidthsDirty = true; InvalidateMeasure(); }, labelFontWeight)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        //Called once per ToolbarRow.Rebuild(): flat leading-to-trailing child list tagged with gap kind, group panels, and the trailing group's overflow pairing.
        internal void SetContent(IReadOnlyList<(Control Child, ToolbarGapKind Gap)> content, IReadOnlyList<ToolbarItem> items, IReadOnlyList<StackPanel> groups, StackPanel? overflowGroup, MenuButton? overflow, SearchField? field) {
            Children.Clear();
            gaps.Clear();
            gapSpacerBefore.Clear();
            labeledWidths.Clear();
            iconOnlyWidths.Clear();
            foreach ((Control child, ToolbarGapKind gap) in content) {
                if (gap == ToolbarGapKind.Group) {
                    var spacer = new Control { Width = groupGap, IsHitTestVisible = false, Focusable = false };
                    Children.Add(spacer);
                    gaps.Add(ToolbarGapKind.None);
                    gapSpacerBefore[child] = spacer;
                }
                Children.Add(child);
                gaps.Add(gap == ToolbarGapKind.Group ? ToolbarGapKind.None : gap);
            }
            allItems = [.. items];
            groupPanels = [.. groups];
            overflowGroupPanel = overflowGroup;
            overflowGroupSpacer = overflowGroup is not null ? gapSpacerBefore.GetValueOrDefault(overflowGroup) : null;
            overflowButton = overflow;
            overflowSpacer = overflow is not null ? gapSpacerBefore.GetValueOrDefault(overflow) : null;
            searchField = field;

            RecacheItemWidths();
            foreach (Control child in Children)
                child.Measure(Size.Infinity);

            appliedStep = -1;
            RecomputeRequiredWidths();
            InvalidateMeasure();
        }

        private int appliedStep = -1;

        private void RestyleSpacers() {
            foreach (Control spacer in gapSpacerBefore.Values)
                spacer.Width = groupGap;
        }

        private void ReapplySearchWidth() {
            if (searchField is not null)
                searchField.Width = appliedStep >= 2 ? searchMinWidth : searchNaturalWidth;
        }

        //Deterministic, so no styling pass and no live-item mutation: each floor (icon-only, labeled) is its own MinWidth token vs. the item's real padding-plus-content width.
        private void RecacheItemWidths() {
            double paddingH = itemPadding.Left + itemPadding.Right;
            double iconOnly = Math.Max(iconOnlyWidthToken, paddingH + glyph17);
            foreach (ToolbarItem item in allItems) {
                iconOnlyWidths[item] = iconOnly;
                labeledWidths[item] = Math.Max(itemWidthToken, paddingH + Math.Max(glyph17, LabelWidth(item)));
            }
        }

        internal double LabeledWidthOf(ToolbarItem item) => labeledWidths.GetValueOrDefault(item);
        internal double IconOnlyWidthOf(ToolbarItem item) => iconOnlyWidths.GetValueOrDefault(item);
        internal double RequiredWidthAtStep(int step) => requiredByStep[step];

        private double LabelWidth(ToolbarItem item) {
            if (string.IsNullOrEmpty(item.Label))
                return 0;
            var typeface = new Typeface(item.FontFamily, item.FontStyle, labelFontWeight);
            return new TextLayout(item.Label, typeface, labelFontSize, Brushes.Transparent).Width;
        }

        private double GroupWidth(StackPanel group, bool iconOnly) {
            ToolbarItem[] its = [.. group.Children.OfType<ToolbarItem>()];
            if (its.Length == 0)
                return 0;
            double sum = its.Sum(i => iconOnly ? iconOnlyWidths.GetValueOrDefault(i) : labeledWidths.GetValueOrDefault(i));
            return sum + (group.Spacing * (its.Length - 1));
        }

        //requiredByStep[n] is constant for this content until it changes (a resize, a token change) - computed once, not on every Measure.
        private void RecomputeRequiredWidths() {
            double titleWidth = Children.Count > 0 ? Children[0].DesiredSize.Width : 0;
            double zoomWidth = Children.Count > 0 ? Children[^1].DesiredSize.Width : 0;
            double overflowWidth = overflowButton?.DesiredSize.Width ?? 0;

            for (int step = 0; step <= 3; step++) {
                bool iconOnly = step >= 1;
                double searchW = step >= 2 ? searchMinWidth : searchNaturalWidth;
                bool overflow = step >= 3 && overflowGroupPanel is not null;

                double sum = titleWidth;
                foreach (StackPanel group in groupPanels) {
                    double gWidth = group == overflowGroupPanel && overflow ? overflowWidth : GroupWidth(group, iconOnly);
                    sum += groupGap + gWidth;
                }
                sum += searchW;
                sum += groupGap + zoomWidth;
                requiredByStep[step] = sum + (2 * sidePadding);
            }
        }

        protected override Size MeasureOverride(Size availableSize) {
            //Deferred past the resource-change broadcast (not run inline from the sink) so every item's own DynamicResource binding has already resolved before we measure it.
            if (itemWidthsDirty) {
                itemWidthsDirty = false;
                RecacheItemWidths();
                foreach (Control child in Children)
                    child.Measure(Size.Infinity);
                RecomputeRequiredWidths();
            }

            double avail = availableSize.Width;
            int step = 0;
            for (; step <= 3; step++)
                if (step == 3 || double.IsInfinity(avail) || requiredByStep[step] <= avail)
                    break;

            if (step != appliedStep) {
                appliedStep = step;
                ApplyCollapseStep(step);
                foreach (Control child in Children)
                    child.Measure(Size.Infinity);
                InvalidateArrange();
            }

            double height = double.IsInfinity(availableSize.Height) ? MaxChildHeight() : availableSize.Height;
            double width = double.IsInfinity(avail) ? requiredByStep[step] : avail;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            double flexAmount = Math.Max(0, finalSize.Width - requiredByStep[Math.Max(0, appliedStep)]);
            double x = sidePadding;
            for (int i = 0; i < Children.Count; i++) {
                Control child = Children[i];
                if (!child.IsVisible)
                    continue;
                x += GapWidth(gaps[i], flexAmount);
                double y = Math.Max(0, (finalSize.Height - child.DesiredSize.Height) / 2);
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width;
            }
            return finalSize;
        }

        //Steps 0-3: shown, icon-only, +search at min width, +trailing group folds into overflow; applied once per real decision change.
        private void ApplyCollapseStep(int step) {
            bool iconOnly = step >= 1;
            foreach (ToolbarItem item in allItems) {
                item.Classes.Set("icon-only", iconOnly);
                ToolTip.SetTip(item, iconOnly ? item.Label : null);
            }

            if (searchField is not null)
                searchField.Width = step >= 2 ? searchMinWidth : searchNaturalWidth;

            bool overflow = step >= 3 && overflowGroupPanel is not null;
            if (overflowGroupPanel is not null)
                overflowGroupPanel.IsVisible = !overflow;
            if (overflowGroupSpacer is not null)
                overflowGroupSpacer.IsVisible = !overflow;
            if (overflowButton is not null)
                overflowButton.IsVisible = overflow;
            if (overflowSpacer is not null)
                overflowSpacer.IsVisible = overflow;
        }

        private static double GapWidth(ToolbarGapKind kind, double flexAmount) => kind switch {
            ToolbarGapKind.Flex => flexAmount,
            _ => 0,
        };

        private double MaxChildHeight() =>
            Children.Where(c => c.IsVisible).Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
    }
}
