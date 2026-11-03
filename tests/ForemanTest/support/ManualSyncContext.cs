using System.Collections.Generic;
using System.Threading;

namespace ForemanTest.support {
    public sealed class ManualSyncContext : SynchronizationContext {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

        public int Pending => queue.Count;

        public void RunAll() {
            while (queue.Count > 0) {
                (SendOrPostCallback callback, object? state) = queue.Dequeue();
                callback(state);
            }
        }

        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue((d, state));
    }
}
