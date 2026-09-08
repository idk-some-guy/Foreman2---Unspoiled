using Avalonia.Controls;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public interface ISheetContent<T> {
        double SheetWidth { get; }
        double? SheetHeight { get; }
        string Title { get; }
        Button? DefaultButton { get; }
        Task<T?> Result { get; }
        void Cancel();
    }

    //Complete finishes Result with a value; Cancel (Esc or otherwise) finishes it with null.
    public abstract class SheetContent<T> : UserControl, ISheetContent<T> {
        private readonly TaskCompletionSource<T?> completion = new();

        public double SheetWidth { get; protected set; }
        public double? SheetHeight { get; protected set; }
        public string Title { get; protected set; } = "";
        public Button? DefaultButton { get; protected set; }
        public Task<T?> Result => completion.Task;

        protected void Complete(T? value) => completion.TrySetResult(value);
        public void Cancel() => completion.TrySetResult(default);
    }
}
