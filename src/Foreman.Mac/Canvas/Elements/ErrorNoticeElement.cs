using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ErrorNoticeElement.cs: draw/tooltip path plus left-click autoresolve
    //and the right-click resolution menu (reference §4e).
    public sealed class ErrorNoticeElement : GraphElement {
        private const int ErrorIconSize = 24;
        private static readonly SKBitmap ErrorIcon = IconCache.GetIcon(Path.Combine("Graphics", "ErrorIcon.png"), 64);

        private readonly NodeElementContext context;
        private readonly INodeViewModel nodeViewModel;

        public ErrorNoticeElement(NodeElementContext context, BaseNodeElement parent) : base(parent) {
            this.context = context;
            nodeViewModel = parent.ViewModel;
            Width = ErrorIconSize;
            Height = ErrorIconSize;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (style == NodeDrawingStyle.IconsOnly)
                return;

            Point trans = LocalToGraph(new Point(-Width / 2, -Height / 2));
            canvas.DrawBitmap(ErrorIcon, SKRect.Create(trans.X, trans.Y, ErrorIconSize, ErrorIconSize));
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible)
                return [];

            List<string> text = nodeViewModel.State switch {
                NodeState.Error => nodeViewModel.GetErrors(),
                NodeState.Warning => nodeViewModel.GetWarnings(),
                _ => []
            };
            if (text.Count == 0)
                return [];

            string body = string.Concat(text.Select(line => line + "\n"));
            if (text.Any(line => line.StartsWith('>')))
                body += "\nLeft click to autoresolve.\nRight click for options.";

            Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(0, Height / 2)));
            return [new TooltipInfo(screen, Direction.Up, body)];
        }

        //Ports the resolutions lookup shared by MouseUp's left/right branches (reference §4e):
        //BaseNodeController.GetErrorResolutions()/GetWarningResolutions(), picked by node state.
        private Dictionary<string, Action>? GetResolutions() {
            if (context.Editor?.RequestNodeController(nodeViewModel.Id) is not BaseNodeController controller)
                return null;
            return nodeViewModel.State switch {
                NodeState.Error => controller.GetErrorResolutions(),
                NodeState.Warning => controller.GetWarningResolutions(),
                _ => null
            };
        }

        //Ports left-click autoresolve: runs every resolution at once, no menu (reference §4e).
        public void Autoresolve() {
            if (!Visible)
                return;
            Dictionary<string, Action>? resolutions = GetResolutions();
            if (resolutions is null)
                return;

            foreach (Action resolution in resolutions.Values)
                resolution.Invoke();
            context.Editor?.Graph.UpdateNodeValues();
        }

        //Ports right-click's data-driven menu: one item per resolution, keyed by its description, shown only
        //if there's at least one - otherwise no menu opens at all (reference §4e).
        public List<MenuEntry> BuildRightClickMenu() {
            if (!Visible)
                return [];
            Dictionary<string, Action>? resolutions = GetResolutions();
            if (resolutions is null || resolutions.Count == 0)
                return [];

            var entries = new List<MenuEntry>();
            foreach (KeyValuePair<string, Action> resolution in resolutions)
                entries.Add(MenuEntry.Item(resolution.Key, () => {
                    resolution.Value.Invoke();
                    context.Editor?.Graph.UpdateNodeValues();
                }));
            return entries;
        }
    }
}
