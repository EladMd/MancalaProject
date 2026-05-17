using System.Linq;

namespace MancalaProject
{
    /// <summary>
    /// Phase-conditioned position heuristic for Mancala. Evaluates a board
    /// state from a given perspective using a weighted combination of three
    /// features — realized score, capture potential, and board control — plus
    /// a phase-dependent bonus and an unconditional starvation penalty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Formula:
    /// <code>
    /// h(state) = w1(phase) · ΔS  +  w2(phase) · (myCapture − oppCapture)  +  w3(phase) · D
    ///          + ChainBonus(phase, state)
    ///          - StarvationPenalty(state)         // unconditional, fires when opp ≤ 3
    /// </code>
    /// </para>
    /// <para>
    /// Terminal short-circuit: when the game is already over,
    /// <see cref="Evaluate"/> bypasses the formula above and returns the
    /// TRUE finalized score difference (scaled large). A decided game is
    /// known, not estimated.
    /// </para>
    /// <para>
    /// The three weights are functions of <see cref="GamePhase"/>: every
    /// phase has its own (w1, w2, w3) triple. This is the "dynamic weights"
    /// requirement — constants are not fixed across the game but selected
    /// based on the current strategic phase.
    /// </para>
    /// <para>
    /// Capture potential is computed for BOTH sides and netted. Furthermore,
    /// it is computed <b>asymmetrically</b> by turn: the side whose turn it
    /// is next gets credit for both passive (latent) and active (immediately
    /// executable) capture opportunities; the side that just moved gets
    /// credit only for passive opportunities. Rationale: an active capture
    /// can only be realized on your turn, so a threat that you can execute
    /// "right now" is worth strictly more than the same threat that you'll
    /// have to wait a full round to execute. Including <c>oppCapture</c>
    /// with the asymmetric weighting is what lets the agent see "my move
    /// looks good locally, BUT the opponent can capture N stones from me
    /// next turn" — a class of mistake the original symmetric heuristic
    /// missed entirely.
    /// </para>
    /// </remarks>
    public static class Heuristic
    {
        // ============================================================
        //  Phase-specific weight vectors
        //
        //  Each phase has a (w1, w2, w3) triple. The numerical values were
        //  chosen so that w1 = 1.0 acts as the natural unit (one banked
        //  stone) — every other weight is interpretable as "fraction of
        //  one banked stone of evaluation pressure".
        // ============================================================

        // --- Opening: many stones in play, capture and control matter most ---
        private const double OpeningW1 = 1.0;
        private const double OpeningW2 = 0.7;   // capture potential is high in early branching
        private const double OpeningW3 = 0.4;   // having ammunition matters

        // --- Midgame: balanced ---
        private const double MidgameW1 = 1.0;
        private const double MidgameW2 = 0.6;
        private const double MidgameW3 = 0.3;

        // --- Endgame: realized score dominates, positional play barely matters ---
        private const double EndgameW1 = 1.5;   // each banked stone is decisive
        private const double EndgameW2 = 0.4;
        private const double EndgameW3 = 0.1;

        // --- StarvationMode: keep the opponent starved, do NOT hoard stones ---
        private const double StarvationW1 =  1.0;
        private const double StarvationW2 =  0.6;
        private const double StarvationW3 = -0.2;   // negative: hoarding prolongs the opponent's recovery

        // --- CaptureChain: multiple capture threats — boost capture weight ---
        private const double CaptureChainW1 = 1.0;
        private const double CaptureChainW2 = 1.2;   // boosted! capture threats dominate this phase
        private const double CaptureChainW3 = 0.3;

        // ============================================================
        //  Sub-weights inside W2's "capture potential" term
        // ============================================================

        /// <summary>
        /// Weight applied to a capture opportunity that requires a future move
        /// to execute (passive / latent threat). Stays at 0.5 because passive
        /// threats are uncertain — they need additional setup moves and can
        /// often be defused by the opponent before they materialize.
        /// </summary>
        private const double PassiveCaptureWeight = 0.5;

        /// <summary>
        /// Weight applied to a capture opportunity that the perspective player
        /// could execute in their current turn (active / immediate threat).
        /// </summary>
        /// <remarks>
        /// Set to 2.0 to match the realistic impact of an executed capture.
        /// When a capture of N stones is realized, the actual h-impact is:
        /// <list type="bullet">
        /// <item><b>ΔS:</b> the capturing side gains N+1 stones (the landed
        ///       stone plus the N stones from the opposite pit) → contributes
        ///       (N+1) × w1 = N+1 to the heuristic.</item>
        /// <item><b>D (board control):</b> the captured side loses N stones
        ///       from their playable pits → contributes N × w3 = 0.4N.</item>
        /// </list>
        /// Total realistic impact ≈ 1.4N + 1, or roughly 2N for typical
        /// captures of N=4–6 stones. The heuristic credits an active threat
        /// as N × ActiveCaptureWeight × w2 = N × 2.0 × 0.7 = 1.4N — which
        /// closely matches that realistic impact. The previous setting of
        /// 1.0 systematically under-credited active threats by ~50%, causing
        /// the chain greedy to overlook defensive moves whose value was
        /// "merely" preventing the opponent from realizing such a threat.
        /// </remarks>
        private const double ActiveCaptureWeight = 2.0;

        // ============================================================
        //  Phase-specific bonuses
        // ============================================================

        /// <summary>
        /// Per-threat additive bonus during <see cref="GamePhase.CaptureChain"/>.
        /// Encourages move sequences that realize the chained captures rather
        /// than letting the opponent defuse them.
        /// </summary>
        private const double ChainBonusPerThreat = 1.5;

        // ============================================================
        //  Starvation strategy (unconditional — fires whenever opponent
        //  has few stones, regardless of phase. The phase only controls
        //  the weight vector; the penalty is its own term.)
        // ============================================================

        /// <summary>Opponent stone count at or below which the penalty activates.</summary>
        private const int StarvationOpponentThreshold = 3;

        /// <summary>
        /// Penalty per opponent stone (so feeding the opponent costs more
        /// than typical move-level gains).
        /// </summary>
        private const double StarvationPenaltyPerStone = 3.0;

        // ============================================================
        //  Terminal-outcome scaling
        // ============================================================

        /// <summary>
        /// Multiplier applied to the finalized score difference of a finished
        /// game. Set far above the magnitude any non-terminal heuristic value
        /// can reach, so a decided win always outranks — and a decided loss
        /// always falls below — every heuristic estimate during the search's
        /// backup pass. The finalized margin is preserved inside the scaled
        /// value, so the agent still prefers winning by more and losing by less.
        /// </summary>
        private const double TerminalOutcomeScale = 10000.0;

        // ============================================================
        //  Public entry point
        // ============================================================

        /// <summary>
        /// Evaluates the given state from <paramref name="perspective"/>'s
        /// point of view, given the active <paramref name="phase"/>.
        /// </summary>
        /// <param name="engine">The board to evaluate. Not mutated.</param>
        /// <param name="perspective">The player whose advantage is being measured.</param>
        /// <param name="phase">The active game phase (selects the weight vector).</param>
        /// <returns>A real number; higher = better for <paramref name="perspective"/>.</returns>
        public static double Evaluate(GameEngine engine, Player perspective, GamePhase phase)
        {
            // A finished game is known, not estimated. GameEngine.ApplyMove
            // never sweeps leftover stones into the stores — FinalizeBoard
            // does — so a game-over board still has stones stranded in pits.
            // Running the feature formula here would miss them entirely;
            // instead return the true finalized outcome.
            if (engine.IsGameOver())
                return TerminalValue(engine, perspective);

            (double w1, double w2, double w3) = WeightsFor(phase);

            Player opponent = OtherPlayer(perspective);
            // "Side to move" = whose turn it is on this state. Active capture
            // potential is only credited to this side, because an active
            // capture is a move you can make NEXT — the other side has to
            // wait a round first.
            bool perspectiveToMove = engine.CurrentPlayer == perspective;

            double deltaS       = ScoreDifference(engine, perspective);
            double myCapture    = ComputeCapturePotential(engine, perspective, perspectiveToMove);
            double oppCapture   = ComputeCapturePotential(engine, opponent,   !perspectiveToMove);
            double captureNet   = myCapture - oppCapture;
            double boardControl = ComputeBoardControl(engine, perspective);

            double core = w1 * deltaS + w2 * captureNet + w3 * boardControl;
            double bonus = PhaseBonus(engine, perspective, phase);
            double penalty = StarvationPenalty(engine, perspective);

            return core + bonus - penalty;
        }

        // ============================================================
        //  Phase → weights mapping
        // ============================================================

        private static (double W1, double W2, double W3) WeightsFor(GamePhase phase) =>
            phase switch
            {
                GamePhase.Opening        => (OpeningW1,        OpeningW2,        OpeningW3),
                GamePhase.Midgame        => (MidgameW1,        MidgameW2,        MidgameW3),
                GamePhase.Endgame        => (EndgameW1,        EndgameW2,        EndgameW3),
                GamePhase.StarvationMode => (StarvationW1,     StarvationW2,     StarvationW3),
                GamePhase.CaptureChain   => (CaptureChainW1,   CaptureChainW2,   CaptureChainW3),
                _                        => (MidgameW1,        MidgameW2,        MidgameW3)
            };

        // ============================================================
        //  Feature 1: Score difference (ΔS)
        // ============================================================

        private static double ScoreDifference(GameEngine engine, Player perspective) =>
            engine.GetScore(perspective) - engine.GetScore(OtherPlayer(perspective));

        // ============================================================
        //  Feature 2: Capture potential (C)
        //  Sum over all empty pits on the given side: passive value (opposing
        //  stones × PassiveCaptureWeight = 0.5) plus active value (opposing
        //  stones × ActiveCaptureWeight = 2.0) if some pit on that side can
        //  sow exactly into the empty target this turn — AND that side is
        //  actually about to move (parameter sideToMove). When sideToMove is
        //  false (the given side just moved), only passive value is credited.
        // ============================================================

        private static double ComputeCapturePotential(GameEngine engine, Player perspective, bool sideToMove) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(i => engine.GetPitIndex(perspective, i))
                .Where(pit => engine.GetPitCount(pit) == 0)
                .Sum(pit => PitCapturePotential(engine, perspective, pit, sideToMove));

        private static double PitCapturePotential(GameEngine engine, Player perspective, int emptyPit, bool sideToMove)
        {
            int oppositeStones = engine.GetPitCount(engine.GetOppositePitIndex(emptyPit));
            if (oppositeStones == 0) return 0.0;

            double passive = PassiveValue(oppositeStones);
            double active = sideToMove
                ? ActiveValue(engine, perspective, emptyPit, oppositeStones)
                : 0.0;
            return passive + active;
        }

        private static double PassiveValue(int oppositeStones) =>
            oppositeStones * PassiveCaptureWeight;

        // Awarded ONCE per opportunity (Any), not once per source pit (would be Sum).
        // See README: multiple reaching pits are mutually-exclusive options, not
        // additive value. Most opponent counter-moves defuse all paths simultaneously,
        // so the marginal value of additional paths is small and not linear.
        private static double ActiveValue(GameEngine engine, Player perspective,
                                          int targetPit, int oppositeStones) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(j => engine.GetPitIndex(perspective, j))
                .Where(src => src != targetPit && engine.GetPitCount(src) > 0)
                .Any(src => CanSowExactlyInto(engine, perspective, src, targetPit))
                    ? oppositeStones * ActiveCaptureWeight
                    : 0.0;

        private static bool CanSowExactlyInto(GameEngine engine, Player perspective,
                                              int sourcePit, int targetPit)
        {
            int sourceStones = engine.GetPitCount(sourcePit);
            int rawDistance = (targetPit - sourcePit + GameEngine.BoardSize) % GameEngine.BoardSize;
            int sowDistance = AdjustDistanceForSkippedStore(sourcePit, rawDistance, SkippedStore(perspective));
            return sourceStones == sowDistance;
        }

        private static int AdjustDistanceForSkippedStore(int sourcePit, int rawDistance, int skippedStore) =>
            PathCrossesStore(sourcePit, rawDistance, skippedStore) ? rawDistance - 1 : rawDistance;

        private static bool PathCrossesStore(int sourcePit, int distance, int store) =>
            Enumerable.Range(1, distance)
                .Any(step => (sourcePit + step) % GameEngine.BoardSize == store);

        private static int SkippedStore(Player perspective) =>
            perspective == Player.Player1 ? GameEngine.Player2Store : GameEngine.Player1Store;

        // ============================================================
        //  Feature 3: Board control (D) — total stones on my side
        // ============================================================

        private static double ComputeBoardControl(GameEngine engine, Player perspective) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(perspective, i)));

        // ============================================================
        //  Phase bonuses
        // ============================================================

        private static double PhaseBonus(GameEngine engine, Player perspective, GamePhase phase) =>
            phase == GamePhase.CaptureChain
                ? CountCaptureThreats(engine, perspective) * ChainBonusPerThreat
                : 0.0;

        private static int CountCaptureThreats(GameEngine engine, Player perspective) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(i => engine.GetPitIndex(perspective, i))
                .Count(pit => engine.GetPitCount(pit) == 0
                           && engine.GetPitCount(engine.GetOppositePitIndex(pit)) > 0);

        // ============================================================
        //  Starvation penalty
        //
        //  Fires unconditionally (any phase) if the opponent has few
        //  stones — the strategic concern of feeding a starving opponent
        //  outweighs phase-specific tactical considerations.
        // ============================================================

        private static double StarvationPenalty(GameEngine engine, Player perspective)
        {
            int opponentStones = CountPlayerStones(engine, OtherPlayer(perspective));
            return opponentStones > StarvationOpponentThreshold
                ? 0.0
                : opponentStones * StarvationPenaltyPerStone;
        }

        // ============================================================
        //  Terminal value
        //
        //  The value of a position where the game has already ended: the
        //  finalized score difference, scaled. "Finalized" means each side's
        //  leftover pit stones are counted into that side's score — exactly
        //  the sweep GameEngine.FinalizeBoard performs — computed here
        //  WITHOUT mutating the engine, since Evaluate promises not to.
        // ============================================================

        private static double TerminalValue(GameEngine engine, Player perspective)
        {
            int myFinal  = FinalScore(engine, perspective);
            int oppFinal = FinalScore(engine, OtherPlayer(perspective));
            return (myFinal - oppFinal) * TerminalOutcomeScale;
        }

        // A player's finalized score = stones already banked in their store
        // plus every stone still sitting in their own playable pits.
        private static int FinalScore(GameEngine engine, Player player) =>
            engine.GetScore(player) + CountPlayerStones(engine, player);

        // ============================================================
        //  Utilities
        // ============================================================

        private static int CountPlayerStones(GameEngine engine, Player player) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(player, i)));

        private static Player OtherPlayer(Player p) =>
            p == Player.Player1 ? Player.Player2 : Player.Player1;
    }
}
