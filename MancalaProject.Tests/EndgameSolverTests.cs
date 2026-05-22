using MancalaProject;

namespace MancalaProject.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EndgameSolver"/> — the exact endgame search.
    /// Verifies its activation window, its determinism, and that it returns a
    /// legal and provably best move.
    /// </summary>
    [TestClass]
    public class EndgameSolverTests
    {
        [TestMethod]
        public void TrySolve_FullOpeningBoard_DeclinesAsTooManyStones()
        {
            bool solved = EndgameSolver.TrySolve(new GameEngine(), Player.Player1, out int move);

            Assert.IsFalse(solved);
            Assert.AreEqual(-1, move);
        }

        [TestMethod]
        public void TrySolve_FinishedGame_Declines()
        {
            // Player 1's side is empty — the game is already over.
            var engine = new GameEngine(new[] { 0, 0, 0, 0, 0, 0, 20, 1, 1, 0, 0, 0, 0, 25 }, Player.Player1);

            Assert.IsFalse(EndgameSolver.TrySolve(engine, Player.Player2, out _));
        }

        [TestMethod]
        public void TrySolve_EndgamePosition_SolvesAndReturnsALegalMove()
        {
            // 10 stones in play — inside the solver's window.
            var engine = new GameEngine(new[] { 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0 }, Player.Player1);

            bool solved = EndgameSolver.TrySolve(engine, Player.Player1, out int move);

            Assert.IsTrue(solved);
            Assert.IsTrue(engine.GetValidMoves().Contains(move), "the solved move must be legal");
        }

        [TestMethod]
        public void TrySolve_SamePositionTwice_IsDeterministic()
        {
            var first  = new GameEngine(new[] { 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0 }, Player.Player1);
            var second = new GameEngine(new[] { 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0 }, Player.Player1);

            EndgameSolver.TrySolve(first,  Player.Player1, out int moveA);
            EndgameSolver.TrySolve(second, Player.Player1, out int moveB);

            Assert.AreEqual(moveA, moveB);
        }

        [TestMethod]
        public void TrySolve_ChoosesTheWinningCaptureOverTheIdleMove()
        {
            // Player 1 may play pit 0 (idle) or pit 1. Pit 1's stone lands in
            // the empty pit 2 and captures the 6 stones in the opposing pit 10,
            // which decides the game. The exact solver must pick pit 1.
            var engine = new GameEngine(new[] { 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 6, 0, 0, 0 }, Player.Player1);

            bool solved = EndgameSolver.TrySolve(engine, Player.Player1, out int move);

            Assert.IsTrue(solved);
            Assert.AreEqual(1, move);
        }
    }
}
