using Foreman.Mac.Kit.Controls;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    //Toolbar row order (K3 composites addendum §08): document -> add -> arrange -> share/view.
    public enum ToolbarGroupRole { Document, Add, Arrange, ShareView }

    //One 2pt-spaced cluster of Kit.ToolbarItems in a Kit.ToolbarRow, separated from its neighbors by the row's 16pt fixed spacer.
    public sealed class ToolbarGroup {
        public required ToolbarGroupRole Role { get; init; }
        public required IReadOnlyList<ToolbarItem> Items { get; init; }
    }
}
