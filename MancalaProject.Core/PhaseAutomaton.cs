using System.Linq;

namespace MancalaProject
{
    /// <summary>
    /// The Phase Automaton: a deterministic finite-state automaton (FSA) over
    /// the five <see cref="GamePhase"/> values. Given a board state and a
    /// perspective player, returns the active phase. The agent uses this
    /// classification to pick a phase-specific weight vector for the
    /// heuristic — making the evaluation a piecewise function of the board
    /// rather than a single static formula.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Formal description:
    /// <list type="bullet">
    /// <item><b>States Q</b>: the five values of <see cref="GamePhase"/>.</item>
    /// <item><b>Initial state q₀</b>: <see cref="GamePhase.Opening"/> (always
    ///       holds at game start where every pit has 4 stones).</item>
    /// <item><b>Transition function δ</b>: implemented by <see cref="Detect"/>.
    ///       δ(state, board) is computed from the board features each move;
    ///       the FSA is therefore stateless between moves — its "current state"
    ///       is recomputed on demand.</item>
    /// <item><b>Accepting states</b>: not used here. Accepting states belong
    ///       to the inner board-state automaton (the search space) and
    ///       correspond to terminal positions where the perspective player
    ///       has the higher store count.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Transition priority (most specific wins): StarvationMode →
    /// CaptureChain → Endgame → Midgame → Opening. Two phases that could
    /// both apply to the same board (e.g. Endgame + StarvationMode) are
    /// resolved by this priority — the more specific phase wins, because
    /// its weight vector encodes the more pressing strategic concern.
    /// </para>
    /// </remarks>
    public static class PhaseAutomaton
    {
        /// <summary>
        /// Number of stones-on-board strictly below which the game is treated
        /// as endgame. Equals one quarter of the initial 48 stones.
        /// </summary>
        private const int EndgameStoneThreshold = 12;

        /// <summary>
        /// Number of stones-on-board at or above which the game is treated
        /// as opening. Equals three quarters of the initial 48 stones.
        /// </summary>
        private const int OpeningStoneThreshold = 36;

        /// <summary>
        /// Opponent stone count at or below which starvation strategy kicks in.
        /// Three is the smallest count that can still threaten any reaching move.
        /// </summary>
        private const int StarvationOpponentThreshold = 3;

        /// <summary>
        /// Minimum number of simultaneous capture threats required to enter
        /// CaptureChain phase. Two is the minimum that justifies the special
        /// "boost capture weight" treatment — a single threat is already
        /// covered by the standard phase's W2.
        /// </summary>
        private const int CaptureChainMinThreats = 2;

        /// <summary>
        /// Returns the active game phase for the given board, evaluated from
        /// <paramref name="perspective"/>'s point of view.
        /// </summary>
        /// <param name="engine">The board state to classify. Not mutated.</param>
        /// <param name="perspective">The player whose strategic situation is being assessed.</param>
        /// <returns>The most specific applicable phase per the FSA's priority order.</returns>
        public static GamePhase Detect(GameEngine engine, Player perspective)
        {
            // Priority order: StarvationMode ≻ CaptureChain ≻ Endgame ≻ Midgame ≻ Opening.
            // The most pressing strategic concern wins.
            if (IsStarvation(engine, perspective))     return GamePhase.StarvationMode;
            if (IsCaptureChain(engine, perspective))   return GamePhase.CaptureChain;

            int stones = CountStonesOnBoard(engine);
            if (stones <  EndgameStoneThreshold)       return GamePhase.Endgame;
            if (stones >= OpeningStoneThreshold)       return GamePhase.Opening;
            return GamePhase.Midgame;
        }

        private static bool IsStarvation(GameEngine engine, Player perspective) =>
            CountPlayerStones(engine, OtherPlayer(perspective)) <= StarvationOpponentThreshold;

        private static bool IsCaptureChain(GameEngine engine, Player perspective) =>
            CountCaptureThreats(engine, perspective) >= CaptureChainMinThreats;

        private static int CountStonesOnBoard(GameEngine engine) =>
            Enumerable.Range(0, GameEngine.BoardSize)
                .Where(i => i != GameEngine.Player1Store && i != GameEngine.Player2Store)
                .Sum(i => engine.GetPitCount(i));

        private static int CountPlayerStones(GameEngine engine, Player player) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(player, i)));

        // A "capture threat" = an empty pit on my side facing a non-empty
        // opposing pit. Whether I can actually reach it this turn is irrelevant
        // for phase classification — the phase asks "is the BOARD capture-rich",
        // not "can I capture right now".
        private static int CountCaptureThreats(GameEngine engine, Player perspective) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(i => engine.GetPitIndex(perspective, i))
                .Count(pit => engine.GetPitCount(pit) == 0
                           && engine.GetPitCount(engine.GetOppositePitIndex(pit)) > 0);

        private static Player OtherPlayer(Player p) =>
            p == Player.Player1 ? Player.Player2 : Player.Player1;
    }
}
