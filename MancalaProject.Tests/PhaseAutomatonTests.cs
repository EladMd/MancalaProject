using MancalaProject;

namespace MancalaProject.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PhaseAutomaton"/> — the finite-state automaton
    /// that classifies a board into one of the five <see cref="GamePhase"/>
    /// values, including its most-specific-wins priority order.
    /// </summary>
    [TestClass]
    public class PhaseAutomatonTests
    {
        [TestMethod]
        public void Detect_OpeningBoard_ReturnsOpening()
        {
            Assert.AreEqual(GamePhase.Opening, PhaseAutomaton.Detect(new GameEngine(), Player.Player1));
        }

        [TestMethod]
        public void Detect_FewStonesOnBoard_ReturnsEndgame()
        {
            // 10 stones in play; the opponent is not starved and there is no
            // capture chain, so plain Endgame applies.
            var engine = new GameEngine(new[] { 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0 }, Player.Player1);
            Assert.AreEqual(GamePhase.Endgame, PhaseAutomaton.Detect(engine, Player.Player1));
        }

        [TestMethod]
        public void Detect_OpponentHasThreeStonesOrFewer_ReturnsStarvationMode()
        {
            var engine = new GameEngine(new[] { 4, 4, 4, 4, 4, 4, 0, 1, 1, 1, 0, 0, 0, 0 }, Player.Player1);
            Assert.AreEqual(GamePhase.StarvationMode, PhaseAutomaton.Detect(engine, Player.Player1));
        }

        [TestMethod]
        public void Detect_TwoOrMoreCaptureThreats_ReturnsCaptureChain()
        {
            // Player 1's empty pits 0 and 1 both face non-empty opposing pits.
            var engine = new GameEngine(new[] { 0, 0, 4, 4, 4, 4, 0, 4, 4, 4, 4, 1, 1, 0 }, Player.Player1);
            Assert.AreEqual(GamePhase.CaptureChain, PhaseAutomaton.Detect(engine, Player.Player1));
        }

        [TestMethod]
        public void Detect_StarvationOutranksEndgame_PriorityOrderHolds()
        {
            // Only 7 stones in play (the Endgame range) but the opponent has 3 —
            // StarvationMode is the more specific phase and must win.
            var engine = new GameEngine(new[] { 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0 }, Player.Player1);
            Assert.AreEqual(GamePhase.StarvationMode, PhaseAutomaton.Detect(engine, Player.Player1));
        }
    }
}
