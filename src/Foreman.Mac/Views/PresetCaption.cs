using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    public static class PresetCaption {
        public static readonly AttachedProperty<Preset?> PresetProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, Preset?>("Preset", typeof(PresetCaption));

        public static readonly AttachedProperty<Control?> NameTargetProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, Control?>("NameTarget", typeof(PresetCaption));

        static PresetCaption() {
            PresetProperty.Changed.AddClassHandler<TextBlock>((caption, _) => {
                caption.AttachedToVisualTree -= OnAttached;
                caption.AttachedToVisualTree += OnAttached;
                Refresh(caption);
            });
        }

        public static Preset? GetPreset(TextBlock caption) => caption.GetValue(PresetProperty);

        public static void SetPreset(TextBlock caption, Preset? preset) => caption.SetValue(PresetProperty, preset);

        public static Control? GetNameTarget(TextBlock caption) => caption.GetValue(NameTargetProperty);

        public static void SetNameTarget(TextBlock caption, Control? target) => caption.SetValue(NameTargetProperty, target);

        private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) {
            if (sender is TextBlock caption)
                Refresh(caption);
        }

        private static void Refresh(TextBlock caption) {
            if (GetPreset(caption) is not { } preset) {
                Apply(caption, null, null);
                return;
            }
            if (PresetHeaders.TryGet(preset, out PresetHeader? header)) {
                Apply(caption, preset, header);
                return;
            }
            Apply(caption, preset, null);
            Async.Fire(LoadAsync(caption, preset), nameof(PresetCaption));
        }

        private static async Task LoadAsync(TextBlock caption, Preset preset) {
            PresetHeader? header = await PresetHeaders.GetAsync(preset).ConfigureAwait(true);
            if (ReferenceEquals(GetPreset(caption), preset))
                Apply(caption, preset, header);
        }

        private static void Apply(TextBlock caption, Preset? preset, PresetHeader? header) {
            caption.Text = PresetVersionCaption.ForList(header);
            caption.IsVisible = caption.Text.Length > 0;
            Control? target = GetNameTarget(caption) ?? caption.FindAncestorOfType<ListBoxItem>();
            if (target is not null && preset is not null)
                AutomationProperties.SetName(target, preset.Name + PresetVersionCaption.ListNameSuffix(header));
        }
    }
}
