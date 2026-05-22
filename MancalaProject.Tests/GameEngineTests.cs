using MancalaProject;

namespace MancalaProject.Tests
{
    /// <summary>
    /// Unit tests for <see cref="GameEngine"/> — the Mancala rule engine:
    /// board setup, move validation, sowing, the opponent-store skip,
    /// captures, extra turns, end-of-game detection, finalization and the
    /// winner calculation.
    /// </summary>
    [TestClass]
    public class GameEngineTests
    {
        // ---- Board setup -----------------------------------------------

        [TestMethod]
        public void NewGame_EveryPlayablePitHasFourStones_StoresEmpty()
        {
            var engine = new GameEngine();

            for (int pit = 0; pit < GameEngine.PitsPerPlayer; pit++)
            {
                Assert.AreEqual(GameEngine.InitialStones, engine.GetPitCount(engine.GetPitIndex(Player.Player1, pit)));
                Assert.AreEqual(GameEngine.InitialStones, engine.GetPitCount(engine.GetPitIndex(Player.Player2, pit)));
            }
            Assert.AreEqual(0, engine.GetScore(Player.Player1));
            Assert.AreEqual(0, engine.GetScore(Player.Player2));
        }

        [TestMethod]
        public void NewGame_DefaultStartingPlayer_IsPlayer1()
        {
            Assert.AreEqual(Player.Player1, new GameEngine().CurrentPlayer);
        }

        [TestMethod]
        public void NewGame_WithPlayer2AsStarter_CurrentPlayerIsPlayer2()
        {
            Assert.AreEqual(Player.Player2, new GameEngine(Player.Player2).CurrentPlayer);
        }

        // ---- Move validation -------------------------------------------

        [TestMethod]
        public void GetValidMoves_OnNewBoard_ReturnsPlayer1SixPits()
        {
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, new GameEngine().GetValidMoves());
        }

        [TestMethod]
        public void IsValidMove_StoreIndex_IsRejected()
        {
            Assert.IsFalse(new GameEngine().IsValidMove(GameEngine.Player1Store));
        }

        [TestMethod]
        public void IsValidMove_OpponentSidePit_IsRejected()
        {
            // It is Player 1's turn; pit 8 lies on Player 2's side.
            Assert.IsFalse(new GameEngine().IsValidMove(8));
        }

        [TestMethod]
        public void IsValidMove_EmptyPit_IsRejected()
        {
            // Pit 2 is empty on Player 1's side.
            var engine = new GameEngine(new[] { 4, 4, 0, 4, 4, 4, 0, 4, 4, 4, 4, 4, 4, 0 }, Player.Player1);
            Assert.IsFalse(engine.IsValidMove(2));
        }

        [TestMethod]
        public void IsValidMove_OutOfRangeIndex_IsRejected()
        {
            var engine = new GameEngine();
            Assert.IsFalse(engine.IsValidMove(-1));
            Assert.IsFalse(engine.IsValidMove(GameEngine.BoardSize));
        }

        // ---- Sowing ----------------------------------------------------

        [TestMethod]
        public void ApplyMove_SimpleSow_DistributesStonesCounterClockwise()
        {
            var engine = new GameEngine();
            MoveResult result = engine.ApplyMove(0);

            Assert.AreEqual(0, engine.GetPitCount(0));   // source emptied
            Assert.AreEqual(5, engine.GetPitCount(1));
            Assert.AreEqual(5, engine.GetPitCount(2));
            Assert.AreEqual(5, engine.GetPitCount(3));
            Assert.AreEqual(5, engine.GetPitCount(4));
            Assert.AreEqual(4, engine.GetPitCount(5));   // untouched
            Assert.AreEqual(4, result.LastPitIndex);
            Assert.IsFalse(result.ExtraTurn);
            Assert.IsFalse(result.CaptureOccurred);
            Assert.AreEqual(Player.Player2, engine.CurrentPlayer);   // turn passed
        }

        [TestMethod]
        public void ApplyMove_SkipsOpponentStore_WhenSowingWrapsPastIt()
        {
            // Player 1 sows 8 stones from pit 5; the path wraps past Player 2's
            // store (index 13), which must receive nothing. Pit 0 starts at 1
            // so the final stone landing there does not trigger a capture.
            var engine = new GameEngine(new[] { 1, 0, 0, 0, 0, 8, 0, 1, 0, 0, 0, 0, 0, 0 }, Player.Player1);
            MoveResult result = engine.ApplyMove(5);

            Assert.AreEqual(0, engine.GetPitCount(GameEngine.Player2Store));   // opponent store skipped
            Assert.AreEqual(1, engine.GetPitCount(GameEngine.Player1Store));   // own store received one
            Assert.AreEqual(0, result.LastPitIndex);
            Assert.IsFalse(result.CaptureOccurred);
        }

        [TestMethod]
        public void ApplyMove_LandingInOwnStore_GrantsExtraTurn()
        {
            // From the opening, pit 2 holds 4 stones and reaches Player 1's store.
            var engine = new GameEngine();
            MoveResult result = engine.ApplyMove(2);

            Assert.IsTrue(result.ExtraTurn);
            Assert.AreEqual(GameEngine.Player1Store, result.LastPitIndex);
            Assert.AreEqual(Player.Player1, engine.CurrentPlayer);   // same player keeps the turn
            Assert.AreEqual(1, engine.GetScore(Player.Player1));
        }

        // ---- Captures --------------------------------------------------

        [TestMethod]
        public void ApplyMove_LastStoneInEmptyOwnPit_CapturesOppositePit()
        {
            // Player 1 plays pit 1 (1 stone); it lands in the empty pit 2,
            // whose opposite pit 10 holds 5 stones.
            var engine = new GameEngine(new[] { 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0 }, Player.Player1);
            MoveResult result = engine.ApplyMove(1);

            Assert.IsTrue(result.CaptureOccurred);
            Assert.AreEqual(5, result.CapturedStoneCount);
            Assert.AreEqual(10, result.CapturedFromPit);
            Assert.AreEqual(2, result.LastPitIndex);
            Assert.AreEqual(6, engine.GetScore(Player.Player1));   // 5 captured + the landing stone
            Assert.AreEqual(0, engine.GetPitCount(2));
            Assert.AreEqual(0, engine.GetPitCount(10));
        }

        [TestMethod]
        public void ApplyMove_LastStoneInOccupiedPit_DoesNotCapture()
        {
            // Pit 2 already holds a stone, so the last stone makes it 2 — not
            // the empty-pit landing a capture requires.
            var engine = new GameEngine(new[] { 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0 }, Player.Player1);
            MoveResult result = engine.ApplyMove(1);

            Assert.IsFalse(result.CaptureOccurred);
            Assert.AreEqual(5, engine.GetPitCount(10));   // opposite pit untouched
        }

        // ---- Illegal moves ---------------------------------------------

        [TestMethod]
        public void ApplyMove_IllegalMove_ThrowsArgumentException()
        {
            var engine = new GameEngine();   // Player 1's turn
            Assert.ThrowsException<ArgumentException>(() => engine.ApplyMove(8));   // pit 8 is Player 2's
        }

        // ---- End of game -----------------------------------------------

        [TestMethod]
        public void IsGameOver_NewBoard_ReturnsFalse()
        {
            Assert.IsFalse(new GameEngine().IsGameOver());
        }

        [TestMethod]
        public void IsGameOver_OneSideEmpty_ReturnsTrue()
        {
            // Player 1's six pits are all empty.
            var engine = new GameEngine(new[] { 0, 0, 0, 0, 0, 0, 10, 1, 1, 1, 0, 0, 0, 5 }, Player.Player1);
            Assert.IsTrue(engine.IsGameOver());
        }

        [TestMethod]
        public void FinalizeBoard_SweepsRemainingStonesIntoEachOwnerStore()
        {
            // Player 1's side is empty (game over); Player 2 still has 5 stones
            // in pits that must be swept into Player 2's store.
            var engine = new GameEngine(new[] { 0, 0, 0, 0, 0, 0, 20, 0, 0, 0, 0, 2, 3, 23 }, Player.Player1);
            engine.FinalizeBoard();

            Assert.IsTrue(engine.IsFinalized);
            Assert.AreEqual(20, engine.GetScore(Player.Player1));
            Assert.AreEqual(28, engine.GetScore(Player.Player2));   // 23 + 5 swept

            for (int pit = 0; pit < GameEngine.PitsPerPlayer; pit++)
            {
                Assert.AreEqual(0, engine.GetPitCount(engine.GetPitIndex(Player.Player1, pit)));
                Assert.AreEqual(0, engine.GetPitCount(engine.GetPitIndex(Player.Player2, pit)));
            }
        }

        [TestMethod]
        public void GetWinner_BeforeFinalize_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new GameEngine().GetWinner());
        }

        [TestMethod]
        public void GetWinner_Player1HasMoreStones_ReturnsZero()
        {
            var engine = new GameEngine(new[] { 0, 0, 0, 0, 0, 0, 30, 0, 0, 0, 0, 0, 0, 18 }, Player.Player1);
            engine.FinalizeBoard();
            Assert.AreEqual(0, engine.GetWinner());
        }

        [TestMethod]
        public void GetWinner_EqualStores_ReturnsMinusOneForTie()
        {
            var engine = new GameEngine(new[] { 0, 0, 0, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 24 }, Player.Player1);
            engine.FinalizeBoard();
            Assert.AreEqual(-1, engine.GetWinner());
        }

        // ---- Cloning & the test-support constructor --------------------

        [TestMethod]
        public void Clone_IsIndependent_MutatingTheCloneLeavesTheOriginalUnchanged()
        {
            var original = new GameEngine();
            GameEngine clone = original.Clone();

            clone.ApplyMove(0);

            Assert.AreEqual(4, original.GetPitCount(0));   // original intact
            Assert.AreEqual(0, clone.GetPitCount(0));      // clone mutated
        }

        [TestMethod]
        public void TestConstructor_WrongBoardLength_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => new GameEngine(new[] { 1, 2, 3 }, Player.Player1));
        }
    }
}
