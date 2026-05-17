namespace MancalaProject
{
    /// <summary>
    /// Identifies the strategic phase of a Mancala game. The agent's heuristic
    /// uses different weight vectors per phase so that, e.g., capture potential
    /// dominates in mid-game while score-difference dominates in the endgame.
    /// </summary>
    /// <remarks>
    /// The set of phases is the state set of the Phase Automaton (FSA). The
    /// initial state is always <see cref="Opening"/>; transitions are
    /// deterministic and triggered by board features (stone counts, capture
    /// configurations). See <see cref="PhaseAutomaton"/> for the transition
    /// function.
    /// </remarks>
    public enum GamePhase
    {
        /// <summary>
        /// Many stones still in play (≥36 of the original 48). The opening
        /// phase favors capture potential and board control because
        /// committed score is too small to dominate evaluation yet.
        /// </summary>
        Opening,

        /// <summary>
        /// 12–36 stones on the board (excluding stores). The "default"
        /// strategic phase: balanced weights between realized score,
        /// capture potential, and board control.
        /// </summary>
        Midgame,

        /// <summary>
        /// Fewer than 12 stones on the board. Realized score dominates
        /// because few moves remain to swing the balance. Capture potential
        /// and board control are heavily de-emphasized.
        /// </summary>
        Endgame,

        /// <summary>
        /// Opponent has ≤3 stones total on their side. Triggers the
        /// starvation strategy: avoid feeding stones across the board,
        /// even at the cost of small immediate gains. Negative weight on
        /// board control to discourage hoarding (which would prolong the
        /// opponent's recovery).
        /// </summary>
        StarvationMode,

        /// <summary>
        /// At least two empty pits on our side face non-empty opposing pits
        /// — multiple simultaneous capture threats. Capture potential weight
        /// is boosted, encouraging the search to prefer move sequences that
        /// realize the chained captures before the opponent can defuse them.
        /// </summary>
        CaptureChain
    }
}
