using System.Collections.Generic;
using System.Threading.Tasks;

namespace ForemanTest.support {
    public sealed class ManualTaskScheduler : TaskScheduler {
        private readonly Queue<Task> queue = new();

        public int Pending => queue.Count;

        public void RunAll() {
            while (queue.Count > 0)
                _ = TryExecuteTask(queue.Dequeue());
        }

        protected override void QueueTask(Task task) => queue.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        protected override IEnumerable<Task> GetScheduledTasks() => queue;
    }
}
