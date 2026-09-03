using Google.OrTools.LinearSolver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ForemanTest {
    [TestClass]
    public class OrToolsSmokeTest {
        [TestMethod]
        public void GlopSolvesTrivialLp() {
            Solver solver = Solver.CreateSolver("GLOP");
            Assert.IsNotNull(solver, "GLOP solver failed to load native library on this platform");
            Variable x = solver.MakeNumVar(0.0, 10.0, "x");
            Objective objective = solver.Objective();
            objective.SetCoefficient(x, 1.0);
            objective.SetMaximization();
            Solver.ResultStatus status = solver.Solve();
            Assert.AreEqual(Solver.ResultStatus.OPTIMAL, status);
            Assert.AreEqual(10.0, x.SolutionValue(), 1e-9);
        }
    }
}
