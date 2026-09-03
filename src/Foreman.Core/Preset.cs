using System;

namespace Foreman {
    public class Preset(string name, bool isCurrentlySelected, bool isDefaultPreset) : IEquatable<Preset> {
        public string Name { get; set; } = name;
        public bool IsCurrentlySelected { get; set; } = isCurrentlySelected;
        public bool IsDefaultPreset { get; set; } = isDefaultPreset;

        public bool Equals(Preset? other) {
            return this == other;
        }

        public override bool Equals(object? obj) {
            return Equals(obj as Preset);
        }

        public override int GetHashCode() => HashCode.Combine(Name);
    }
}
