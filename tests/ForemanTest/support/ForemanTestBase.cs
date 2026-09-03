namespace ForemanTest.support {
    // Note: upstream guards each test with UserMessages.TestHandler so a stray MessageBox call fails
    // loudly instead of blocking on a real dialog. UserMessages.cs is UI-only and excluded from
    // Foreman.Core (see docs/upstream-divergences.md); Core's own callers already log instead of
    // popping a dialog, so there's nothing left to guard. Kept as an empty base for the test classes
    // that still derive from it.
    public abstract class ForemanTestBase {
    }
}
