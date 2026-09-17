using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //ButtonRow's own panel: leading helper separated by a flexible gap from the trailing cluster (Other/Destructive/Cancel/Default), never full-bleed.
    public sealed class ButtonRowPanel : Panel {
        private double gap = 8;
        private double minButtonWidth = 64;
        private readonly List<IDisposable> tokenSubs = [];

        private bool IsCentered => (TemplatedParent as ButtonRow)?.IsCentered ?? false;

        private static ButtonRowRole RoleOf(Control c) => c is Button b ? ButtonRow.GetRole(b) : ButtonRowRole.Other;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space2").Subscribe(new ResourceSinks.DoubleSink(v => {
                gap = v;
                InvalidateMeasure();
            }, gap)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Metrics.ButtonMinWidth").Subscribe(new ResourceSinks.DoubleSink(v => {
                minButtonWidth = v;
                InvalidateMeasure();
            }, minButtonWidth)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
        }

        private (Control? Helper, List<Control> Cluster) Groups() {
            Control? helper = null;
            var byRole = new Dictionary<ButtonRowRole, Control>();
            foreach (Control child in Children) {
                ButtonRowRole role = RoleOf(child);
                if (role == ButtonRowRole.Helper)
                    helper = child;
                else
                    byRole[role] = child;
            }
            ButtonRowRole[] order = [ButtonRowRole.Other, ButtonRowRole.Destructive, ButtonRowRole.Cancel, ButtonRowRole.Default];
            List<Control> cluster = [.. order.Where(byRole.ContainsKey).Select(r => byRole[r])];
            return (helper, cluster);
        }

        protected override Size MeasureOverride(Size availableSize) {
            foreach (Control child in Children) {
                if (child is Button button)
                    button.MinWidth = minButtonWidth;
                child.Measure(Size.Infinity);
            }

            double avail = availableSize.Width;
            double rowHeight = Children.Count == 0 ? 0 : Children.Max(c => c.DesiredSize.Height);

            //Wrap decision is the same regardless of alignment - only Arrange differs on IsCentered.
            (Control? helper, List<Control> cluster) = Groups();
            double clusterWidth = LineWidth(cluster, gap);
            double helperWidth = helper?.DesiredSize.Width ?? 0;
            double combined = helperWidth + (helper is not null && cluster.Count > 0 ? gap : 0) + clusterWidth;

            if (double.IsInfinity(avail) || combined <= avail)
                return new Size(double.IsInfinity(avail) ? combined : avail, rowHeight);

            List<List<Control>> lines = WrapCluster(cluster, avail, gap);
            double height = (helper is not null ? rowHeight + gap : 0) + (lines.Count * rowHeight) + (Math.Max(0, lines.Count - 1) * gap);
            return new Size(avail, height);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            double rowHeight = Children.Count == 0 ? 0 : Children.Max(c => c.DesiredSize.Height);
            bool centered = IsCentered;

            (Control? h, List<Control> clu) = Groups();
            double clusterWidth = LineWidth(clu, gap);
            double helperWidth = h?.DesiredSize.Width ?? 0;
            double combined = helperWidth + (h is not null && clu.Count > 0 ? gap : 0) + clusterWidth;

            if (combined <= finalSize.Width) {
                double y = (finalSize.Height - rowHeight) / 2;
                if (centered) {
                    List<Control> all = h is not null ? [h, .. clu] : clu;
                    ArrangeCentered(all, finalSize.Width, y, rowHeight, gap);
                } else {
                    if (h is not null)
                        h.Arrange(new Rect(0, y, h.DesiredSize.Width, rowHeight));
                    ArrangeTrailing(clu, finalSize.Width, y, rowHeight, gap);
                }
                return finalSize;
            }

            double top = 0;
            if (h is not null) {
                if (centered)
                    ArrangeCentered([h], finalSize.Width, top, rowHeight, gap);
                else
                    h.Arrange(new Rect(0, top, h.DesiredSize.Width, rowHeight));
                top = rowHeight + gap;
            }
            foreach (List<Control> line in WrapCluster(clu, finalSize.Width, gap)) {
                if (centered)
                    ArrangeCentered(line, finalSize.Width, top, rowHeight, gap);
                else
                    ArrangeTrailing(line, finalSize.Width, top, rowHeight, gap);
                top += rowHeight + gap;
            }
            return finalSize;
        }

        private static void ArrangeTrailing(List<Control> line, double totalWidth, double y, double rowHeight, double gap) {
            double x = Math.Max(0, totalWidth - LineWidth(line, gap));
            foreach (Control c in line) {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, rowHeight));
                x += c.DesiredSize.Width + gap;
            }
        }

        private static void ArrangeCentered(List<Control> line, double totalWidth, double y, double rowHeight, double gap) {
            double x = Math.Max(0, (totalWidth - LineWidth(line, gap)) / 2);
            foreach (Control c in line) {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, rowHeight));
                x += c.DesiredSize.Width + gap;
            }
        }

        private static double LineWidth(List<Control> line, double gap) =>
            line.Count == 0 ? 0 : line.Sum(c => c.DesiredSize.Width) + (gap * (line.Count - 1));

        //Fills from the trailing (Default) end backward, so overflow moves up a line rather than shrinking.
        private static List<List<Control>> WrapCluster(List<Control> cluster, double maxWidth, double gap) {
            var lines = new List<List<Control>>();
            var current = new List<Control>();
            double currentWidth = 0;
            for (int i = cluster.Count - 1; i >= 0; i--) {
                Control c = cluster[i];
                double add = current.Count == 0 ? c.DesiredSize.Width : c.DesiredSize.Width + gap;
                if (current.Count > 0 && currentWidth + add > maxWidth) {
                    lines.Add(current);
                    current = [];
                    add = c.DesiredSize.Width;
                    currentWidth = 0;
                }
                current.Insert(0, c);
                currentWidth += add;
            }
            if (current.Count > 0)
                lines.Add(current);
            lines.Reverse();
            return lines;
        }
    }
}
