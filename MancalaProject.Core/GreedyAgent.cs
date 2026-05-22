using System;

namespace MancalaProject
{
    /// <summary>
    /// Specifies how strong the computer agent should play. The three levels
    /// search progressively deeper (Easy 2, Medium 3, Hard 4 plies); Hard
    /// additionally solves the endgame exactly once few stones remain and
    /// screens its chosen move through a capture-safety guard.
    /// The agent is fully deterministic at every difficulty: no randomness is
    /// ever injected into the evaluation or the search.
    /// </summary>
    public enum Difficulty
    {
        /// <summary>Looks 2 plies ahead (my move + the opponent's reply), with the smallest node budget and a narrow beam. Weakest play.</summary>
        Easy,
        /// <summary>Looks 3 plies ahead, with a moderate node budget and the full beam.</summary>
        Medium,
        /// <summary>Searches 4 plies ahead — one ply deeper than Medium — additionally solves the endgame exactly once few stones remain, and screens its chosen move through a capture-safety guard. Strongest play.</summary>
        Hard
    }

    /// <summary>
    /// Phase-Aware Best-First Beam Search agent for Mancala. The agent treats
    /// the game as a state-graph automaton: positions are states, legal moves
    /// are transitions. It explores this automaton using a priority queue
    /// ordered by a phase-conditioned heuristic, beam-pruning at each
    /// branching expansion. A separate <see cref="PhaseAutomaton"/> classifies
    /// every position into one of five game phases and the
    /// <see cref="Heuristic"/> uses the phase to select a phase-specific
    /// weight vector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is a thin facade. Strategic logic lives in three
    /// collaborating components in the same project:
    /// <list type="bullet">
    /// <item><see cref="PhaseAutomaton"/> — the FSA over game phases.</item>
    /// <item><see cref="Heuristic"/> — the phase-conditioned position evaluator.</item>
    /// <item><see cref="BeamSearch"/> — the best-first beam-pruning search engine.</item>
    /// </list>
    /// The class itself stores only the player identity and difficulty;
    /// every call to <see cref="CalculateMove"/> delegates to a stateless
    /// <c>BeamSearch.FindBestMove</c> with no shared state between calls.
    /// </para>
    /// <para>
    /// The algorithm is structurally distinct from Minimax: there is no
    /// alternating min/max layer (the opponent is modeled as a deterministic
    /// greedy transition rather than a worst-case adversary), the value
    /// backup is one-sided (my-turn nodes take the max over their children
    /// while opponent-turn nodes simply forward their single greedy child's
    /// value — there is never a min over the opponent's options), and the
    /// search graph is a DAG (positions reached by different move orders
    /// share an h-cache entry through a transposition table).
    /// </para>
    /// </remarks>
    public class GreedyAgent
    {
        private readonly Player _myPlayer;
        private readonly Difficulty _difficulty;

        /// <summary>
        /// Creates a new agent that plays as the given player at the given difficulty.
        /// </summary>
        /// <param name="player">The side this agent will play.</param>
        /// <param name="difficulty">Strength setting; defaults to <see cref="Difficulty.Hard"/>.</param>
        public GreedyAgent(Player player, Difficulty difficulty = Difficulty.Hard)
        {
            _myPlayer   = player;
            _difficulty = difficulty;
        }

        // =====================================================================
        //  Public API — calculation and execution are deliberately separate
        // =====================================================================

        /// <summary>
        /// Selects the best move for the current position without modifying the engine.
        /// Use this method when you need only the agent's choice (e.g. to display it
        /// before applying it). Call <see cref="ExecuteMove"/> to actually play the move.
        /// </summary>
        /// <param name="engine">The current game state. Not mutated.</param>
        /// <returns>The absolute board index the agent has chosen to play from.</returns>
        /// <exception cref="InvalidOperationException">Thrown if there are no valid moves available.</exception>
        public int CalculateMove(GameEngine engine) =>
            BeamSearch.FindBestMove(engine, _myPlayer, _difficulty);

        /// <summary>
        /// Applies the given move to the engine and returns the resulting move outcome.
        /// This method is intentionally synchronous and immediate; UI layers that wish
        /// to display a "thinking" delay should schedule it themselves (e.g. via
        /// <c>Task.Delay</c>) so the UI thread stays responsive.
        /// </summary>
        /// <param name="engine">The live engine to apply the move on.</param>
        /// <param name="move">The absolute board index to play from (typically the result of <see cref="CalculateMove"/>).</param>
        /// <returns>The <see cref="MoveResult"/> produced by <see cref="GameEngine.ApplyMove"/>.</returns>
        public MoveResult ExecuteMove(GameEngine engine, int move) =>
            engine.ApplyMove(move);
    }
}
