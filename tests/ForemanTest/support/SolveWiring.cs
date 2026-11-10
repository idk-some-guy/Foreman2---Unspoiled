using Foreman;
using System.Collections.Generic;

namespace ForemanTest.support {
    internal static class SolveWiring {
        internal static (ManualTaskScheduler Worker, ManualSyncContext Context, List<string> Events) Wire(ProductionGraph graph) {
            var worker = new ManualTaskScheduler();
            var context = new ManualSyncContext();
            graph.SolveContext = context;
            graph.Solver.Worker = worker;
            List<string> events = [];
            graph.SolveStarted += (_, _) => events.Add("started");
            graph.SolveEnded += (_, e) => events.Add("ended:" + e.Kind);
            return (worker, context, events);
        }
    }
}
