using Avalonia.Controls;
using System;

namespace Foreman.Mac.Kit.Gallery {
    public sealed record GalleryTile(string Family, string Name, string State, Func<Control> Build);
}
