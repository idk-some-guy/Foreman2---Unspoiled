using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Canvas {
    public enum AddScope { All, Items, Recipes }

    public sealed record AddLinkContext(ItemQualityPair Item, NewNodeType NodeType, FRange TempRange, BaseNodeElement OriginElement, NodeDirection Direction);

    public sealed record AddRequest(DrawingPoint Location, AddScope Scope, AddLinkContext? Link = null, string SeedText = "", NodeType? OriginsRole = null);
}
