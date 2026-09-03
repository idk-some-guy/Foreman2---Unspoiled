using Foreman.DataCaching;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman {
    //Central sink for the fire-and-forget "_ = SomeAsync()" call sites scattered across Foreman.Mac's
    //toolbar/menu/window handlers: none of them observed a faulted or canceled task, so an exception (or a
    //silent cancellation) thrown deep inside one just vanished instead of reaching errorlog.txt.
    //ExecuteSynchronously matters for tests - it lets a caller that completes the task itself (e.g. a
    //TaskCompletionSource) observe the log write without a race against a separate thread-pool continuation.
    public static class Async {
        public static void Fire(Task task, string context) =>
            task.ContinueWith(
                t => {
                    try {
                        if (t.IsFaulted)
                            ErrorLogging.LogException(t.Exception!.GetBaseException(), context);
                        else if (t.IsCanceled)
                            ErrorLogging.LogLine(context + ": task canceled");
                    } catch (Exception loggingFailure) {
                        //Logging itself must never throw back into a ContinueWith and re-vanish - this is
                        //the last line of defense if ErrorLogging's own catch (LogLine) is ever bypassed.
                        Trace.WriteLine("Async.Fire logging failed for " + context + ": " + loggingFailure);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
