using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Views {
    internal static class SolveErrorPanel {
        internal const string Headline = "Couldn’t solve this graph";
        internal const string Announcement = "Couldn't solve this graph";
        internal const string FallbackReason = "The solver stopped before it found a result.";
        private const string Staleness = "The rates shown are from the last solve that worked.";

        private static readonly Dictionary<SolveFailureReason, string> ReasonSentences = [];

        internal static string Sentence(SolveFailureReason reason) =>
            $"{(ReasonSentences.TryGetValue(reason, out string? sentence) ? sentence : FallbackReason)} {Staleness}";

        internal static StatePanel Build(SolveFailure? failure, Action showNode, Action tryAgain) {
            Button again = SheetButtons.Make("Try Again", ButtonRowRole.Default);
            again.Click += (_, _) => tryAgain();
            ButtonRow row;
            if (failure is { NodeIds.Count: > 0 }) {
                Button node = SheetButtons.Make("Show Node", ButtonRowRole.Other);
                node.Click += (_, _) => showNode();
                row = SheetButtons.Row(node, again);
            } else {
                row = SheetButtons.Row(again);
            }

            Application.Current!.TryGetResource("Kit.StatePanel", null, out object? theme);
            return new StatePanel {
                Theme = theme as ControlTheme ?? throw new InvalidOperationException("SolveErrorPanel: kit theme 'Kit.StatePanel' not found."),
                Shape = StatePanelShape.Error,
                ForceCompact = true,
                Headline = Headline,
                Sentence = Sentence(failure?.Reason ?? SolveFailureReason.Unknown),
                Buttons = row,
            };
        }
    }
}
