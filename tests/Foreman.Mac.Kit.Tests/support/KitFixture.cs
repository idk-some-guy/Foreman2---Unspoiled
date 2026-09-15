using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Foreman.Mac.Kit.Appearance;
using System;

namespace Foreman.Mac.Kit.Tests.support {
    //Shared across every Controls/<Family>Tests.cs: a merged Kit dictionary, a shown window, and the scale/state/brush helpers the fidelity tests repeat.
    public sealed class KitFixture : IDisposable {
        private readonly ScaleGuard scale = new();

        public KitFixture() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);

            Appearance = new FakePlatformAppearance();
            Window = new Window { Width = 400, Height = 300 };
            //Scoped to this window, not Application.Styles, so stock controls get a real template without theming the rest of the suite.
            Window.Styles.Add(new FluentTheme());
            Window.Show();
        }

        public Window Window { get; }
        public FakePlatformAppearance Appearance { get; }

        public ControlState StateOf(Control control) => new(control, Window);

        public IBrush Brush(string tokenKey) {
            Application.Current!.TryGetResource(tokenKey, Window.ActualThemeVariant, out object? value);
            return value switch {
                IBrush brush => brush,
                Color color => new SolidColorBrush(color),
                _ => throw new InvalidOperationException($"KitFixture: '{tokenKey}' did not resolve to a brush or color."),
            };
        }

        public void Dispose() {
            scale.Dispose();
            //Keyboard focus is process-wide in the headless runner and outlives a closed window otherwise.
            Window.FocusManager?.Focus(null, NavigationMethod.Unspecified);
            Dispatcher.UIThread.RunJobs();
            Window.Close();
        }
    }

    //Resets the multiplied kit tokens to 1.0 on both construction and disposal, so no fixture inherits a leftover scale.
    public sealed class ScaleGuard : IDisposable {
        public ScaleGuard() => ScaleContract.Apply(1.0);
        public void Dispose() => ScaleContract.Apply(1.0);
    }

    //Fakeable IPlatformAppearance a test can toggle directly, mirroring the FakeAppearance in Appearance/*Tests.cs.
    public sealed class FakePlatformAppearance : IPlatformAppearance {
        private EventHandler? changed;

        public Color? AccentColor { get; set; }
        public ColorContrastPreference ContrastPreference { get; set; } = ColorContrastPreference.NoPreference;
        public bool ReduceTransparency { get; set; }
        public bool ReduceMotion { get; set; }
        public PlatformThemeVariant ThemeVariant { get; set; } = PlatformThemeVariant.Light;

        public event EventHandler? Changed { add => changed += value; remove => changed -= value; }

        public void RaiseChanged() => changed?.Invoke(this, EventArgs.Empty);
    }

    //Raises real pointer/keyboard events at a control, mirroring RealClick.cs/CanvasKeys.cs in tests/Foreman.Mac.UiTests/support.
    public sealed class ControlState {
        private readonly Control control;
        private readonly Window window;

        internal ControlState(Control control, Window window) {
            this.control = control;
            this.window = window;
        }

        public void Hover() {
            window.MouseMove(Center(), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        public void Press() {
            window.MouseDown(Center(), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        public void Release() {
            window.MouseUp(Center(), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        public void Click() {
            Hover();
            Press();
            Release();
        }

        public void Focus() {
            control.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
        }

        //Real TextInput through the headless window, mirroring how a keyboard actually fills a field (vs. setting Text directly).
        public void Type(string text) {
            Focus();
            window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
        }

        public void KeyDown(Key key, KeyModifiers modifiers = KeyModifiers.None) {
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        public void KeyUp(Key key, KeyModifiers modifiers = KeyModifiers.None) {
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        //Press-move-release through the control's own local points, mirroring how a real mouse drag selects text.
        public void Drag(Point from, Point to) {
            window.MouseDown(Translate(from), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(Translate(to), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(Translate(to), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        private Point Center() {
            Dispatcher.UIThread.RunJobs();
            Point center = new(control.Bounds.Width / 2, control.Bounds.Height / 2);
            return Translate(center);
        }

        private Point Translate(Point localPoint) {
            Dispatcher.UIThread.RunJobs();
            return Avalonia.VisualExtensions.TranslatePoint(control, localPoint, window) ?? localPoint;
        }
    }
}
