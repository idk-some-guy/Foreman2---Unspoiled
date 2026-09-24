using Avalonia.Controls;
using Foreman.Mac.Kit.Composites;
using System;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public enum SheetActivityKind { InProgress, Failed }

    public sealed record SheetActivity(SheetActivityKind Kind, string Sentence, double? Progress = null);

    public interface ISheetActivitySource {
        SheetActivity? Activity { get; }
        event EventHandler? ActivityChanged;
    }

    public interface ISheetContent<T> {
        SheetWidthClass WidthClass { get; }
        //A width outside the three named ones; 0 means WidthClass decides.
        double SheetWidth { get; }
        double? SheetHeight { get; }
        string Title { get; }
        ButtonRow? Buttons { get; }
        Button? DefaultButton { get; }
        SheetActivity? Activity { get; }
        Task<T?> Result { get; }
        void Cancel();
    }

    //Complete finishes Result with a value; Cancel (Esc or otherwise) finishes it with null.
    public abstract class SheetContent<T> : UserControl, ISheetContent<T>, ISheetActivitySource {
        private readonly TaskCompletionSource<T?> completion = new();

        public SheetWidthClass WidthClass { get; protected set; } = SheetWidthClass.Standard;
        public double SheetWidth { get; protected set; }
        public double? SheetHeight { get; protected set; }
        public string Title { get; protected set; } = "";
        public ButtonRow? Buttons { get; protected set; }
        public Button? DefaultButton { get; protected set; }
        public SheetActivity? Activity { get; private set; }
        public Task<T?> Result => completion.Task;

        public event EventHandler? ActivityChanged;

        protected void Complete(T? value) => completion.TrySetResult(value);

        protected void ReportProgress(string sentence, double? progress = null) =>
            SetActivity(new SheetActivity(SheetActivityKind.InProgress, sentence, progress));

        //The sheet stays open with its fields; its default becomes Try Again and re-runs the same action.
        protected void ReportFailure(string sentence) => SetActivity(new SheetActivity(SheetActivityKind.Failed, sentence));

        protected void ClearActivity() => SetActivity(null);

        private void SetActivity(SheetActivity? activity) {
            Activity = activity;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        //virtual so Escape and the Cancel button share one override
        public virtual void Cancel() => completion.TrySetResult(default);
    }
}
