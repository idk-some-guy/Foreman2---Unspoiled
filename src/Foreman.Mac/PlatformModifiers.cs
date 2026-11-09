using Avalonia.Input;
using System;
using System.Threading;

namespace Foreman.Mac {
    //Platform-conditional keyboard modifier (docs/upstream-divergences.md, phase 8 Task 2): this port's own
    //Cmd mapping is macOS-only - Linux keeps Ctrl, exactly like upstream and every other Linux app. Every
    //hardcoded KeyModifiers.Meta site (shortcuts, drag/lasso modifiers, menu gestures) reads Primary instead.
    public static class PlatformModifiers {
        private static readonly AsyncLocal<bool?> isMacOsOverride = new();

        public static KeyModifiers Primary => (isMacOsOverride.Value ?? OperatingSystem.IsMacOS()) ? KeyModifiers.Meta : KeyModifiers.Control;

        //Test-only seam: forces this async flow's Primary to resolve as if running on the given platform, so
        //both branches are exercised without gating tests on the host OS.
        internal static IDisposable UseIsMacOs(bool isMacOs) {
            bool? previous = isMacOsOverride.Value;
            isMacOsOverride.Value = isMacOs;
            return new OverrideRestorer(previous);
        }

        private sealed class OverrideRestorer : IDisposable {
            private readonly bool? previous;
            public OverrideRestorer(bool? previous) => this.previous = previous;
            public void Dispose() => isMacOsOverride.Value = previous;
        }
    }
}
