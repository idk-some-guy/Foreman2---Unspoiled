using Avalonia.Controls;
using Foreman.Mac.Kit.Composites;
using System;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public enum SheetActivityKind { InProgress, Failed }

    public sealed record SheetActivity(SheetActivityKind Kind, string Sentence, double? Progress = null, bool OffersRetry = true);

    public interface ISheetActivitySource {
        SheetActivity? Activity { get; }
        event EventHandler? ActivityChanged;
    }

    public interface ISheetContent<T> {
        SheetWidthClass WidthClass { get; }
        double? SheetHeight { get; }
        string Title { get; }
        ButtonRow? Buttons { get; }
        Button? DefaultButton { get; }
        SheetActivity? Activity { get; }
        Task<T?> Result { get; }
        void Cancel();
    }

    public interface IAlertContent {
        AlertFrame FrameControl { get; }
    }

    public abstract class SheetContent<T> : UserControl, ISheetContent<T>, ISheetActivitySource {
        private readonly TaskCompletionSource<T?> completion = new();

        public SheetWidthClass WidthClass { get; protected set; } = SheetWidthClass.Standard;
        public double? SheetHeight { get; protected set; }
        public string Title { get; protected set; } = "";
        public ButtonRow? Buttons { get; protected set; }
        public Button? DefaultButton { get; protected set; }
        public SheetActivity? Activity { get; private set; }
        public Task<T?> Result => completion.Task;

        public event EventHandler? ActivityChanged;
        public event EventHandler? TitleChanged;

        protected void SetTitle(string title) {
            Title = title;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        protected void Complete(T? value) => completion.TrySetResult(value);

        protected void ReportProgress(string sentence, double? progress = null) =>
            SetActivity(new SheetActivity(SheetActivityKind.InProgress, sentence, progress));

        protected void ReportFailure(string sentence, bool offersRetry = true) =>
            SetActivity(new SheetActivity(SheetActivityKind.Failed, sentence, OffersRetry: offersRetry));

        protected void ClearActivity() => SetActivity(null);

        private void SetActivity(SheetActivity? activity) {
            Activity = activity;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        public virtual void Cancel() => completion.TrySetResult(default);
    }
}
