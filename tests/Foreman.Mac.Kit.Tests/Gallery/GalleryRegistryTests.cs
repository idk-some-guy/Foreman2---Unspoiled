using Avalonia.Controls;
using Foreman.Mac.Kit.Gallery;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Gallery {
    public class GalleryRegistryTests {
        private static readonly Func<Control> NewControl = () => new Canvas();

        [Fact]
        public void Register_AddsTheTileToTiles() {
            var registry = new GalleryRegistry();
            var tile = new GalleryTile("Canvas", "QualityPicker", "Default", NewControl);

            registry.Register(tile);

            Assert.Same(tile, Assert.Single(registry.Tiles));
        }

        [Fact]
        public void Register_PreservesRegistrationOrder() {
            var registry = new GalleryRegistry();
            registry.Register(new GalleryTile("Theme", "First", "Default", NewControl));
            registry.Register(new GalleryTile("Theme", "Second", "Default", NewControl));
            registry.Register(new GalleryTile("Theme", "Third", "Default", NewControl));

            Assert.Equal(["First", "Second", "Third"], registry.Tiles.Select(t => t.Name));
        }

        [Fact]
        public void Register_DuplicateName_Throws() {
            var registry = new GalleryRegistry();
            registry.Register(new GalleryTile("Theme", "Toolbar / Titlebar", "Default", NewControl));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new GalleryTile("Canvas", "Toolbar / Titlebar", "Default", NewControl)));
        }

        [Fact]
        public void Build_InvokesTheFactoryEachCall() {
            var registry = new GalleryRegistry();
            int calls = 0;
            registry.Register(new GalleryTile("Canvas", "QualityPicker", "Default", () => {
                calls++;
                return NewControl();
            }));

            registry.Tiles[0].Build();
            registry.Tiles[0].Build();

            Assert.Equal(2, calls);
        }
    }
}
