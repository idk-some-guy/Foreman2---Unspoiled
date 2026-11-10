using Foreman.Mac.Kit.Controls;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    public enum ToolbarGroupRole { Document, Add, Arrange, Research, ShareView }

    public sealed class ToolbarGroup {
        public required ToolbarGroupRole Role { get; init; }
        public required IReadOnlyList<ToolbarItem> Items { get; init; }
    }
}
