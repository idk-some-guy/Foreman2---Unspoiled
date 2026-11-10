using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Gallery {
    public sealed class GalleryRegistry {
        private readonly List<GalleryTile> tiles = [];

        public IReadOnlyList<GalleryTile> Tiles => tiles;

        public void Register(GalleryTile tile) {
            if (tiles.Any(t => t.Name == tile.Name))
                throw new InvalidOperationException($"Gallery: a tile named '{tile.Name}' is already registered.");
            tiles.Add(tile);
        }
    }
}
