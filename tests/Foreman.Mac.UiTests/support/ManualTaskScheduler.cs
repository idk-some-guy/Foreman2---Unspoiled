using Avalonia.Threading;
using Foreman;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.UiTests.support {
    internal sealed class ManualTaskScheduler : TaskScheduler {
        private readonly Queue<Task> queue = new();

        private sealed class InlineContext : SynchronizationContext {
            public override void Post(SendOrPostCallback d, object? state) => d(state);
        }

        public static ManualTaskScheduler InstallInline(ProductionGraph graph) {
            var scheduler = new ManualTaskScheduler();
            graph.Solver.Worker = scheduler;
            graph.SolveContext = new InlineContext();
            return scheduler;
        }

        public static void Defer(ProductionGraph graph) {
            ((ManualTaskScheduler)graph.Solver.Worker).RunInline = false;
            graph.SolveContext = new AvaloniaSynchronizationContext();
        }

        public bool RunInline { get; set; } = true;

        public int Pending => queue.Count;

        public void RunAll() {
            while (queue.Count > 0)
                _ = TryExecuteTask(queue.Dequeue());
        }

        protected override void QueueTask(Task task) {
            if (RunInline)
                _ = TryExecuteTask(task);
            else
                queue.Enqueue(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        protected override IEnumerable<Task> GetScheduledTasks() => queue;
    }
}
