using System.Runtime.CompilerServices;

// Grants the unit-test project (MancalaProject.Tests) white-box access to this
// assembly's internal types — MinHeap, SearchNode, BeamSearch and
// EndgameSolver — and to the internal test-support constructor of GameEngine.
// Those types are internal so the assembly's public API stays minimal; the
// test project alone is given visibility, so the search internals can still be
// covered by tests. No code outside the test project is affected.
[assembly: InternalsVisibleTo("MancalaProject.Tests")]
