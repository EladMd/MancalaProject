using System.Collections.Generic;
using System.Linq;

// NOTE: the memo table below uses the .NET Dictionary purely as a hash-map
// lookup cache — exactly the role BeamSearch's transposition table already
// uses it for. The project's hand-written data structure is MinHeap (the
// search frontier, see MinHeap.cs). A hash map is not an algorithmic engine
// of the search, only a cache, so a built-in one is used here as well.

namespace MancalaProject
{
    /// <summary>
    /// Exact endgame solver. Once a position has few enough stones left in
    /// play, the game tree down to its terminal positions is small enough to
    /// search EXHAUSTIVELY — with no heuristic, no beam pruning and no depth
    /// horizon. Every leaf is a finished game whose score is KNOWN, so the
    /// value backed up to the root is the true final score difference, not an
    /// estimate. The agent therefore plays the endgame perfectly (with respect
    /// to the opponent model below) instead of approximately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Relation to <see cref="BeamSearch"/>. The beam search is an APPROXIMATE
    /// engine: it stops at a depth horizon and scores the frontier with a
    /// heuristic. This solver is an EXACT engine for the part of the game
    /// where exactness is affordable. <see cref="BeamSearch.FindBestMove"/>
    /// consults the solver first (Hard difficulty only); when the position is
    /// within reach the solver's move is returned directly and the beam search
    /// — and the capture-safety guard — are skipped, because an exact answer
    /// needs neither an approximation nor a safety net.
    /// </para>
    /// <para>
    /// Why this is still NOT Minimax. The solver searches all the way to
    /// terminal positions, which might look like a full game-tree search — but
    /// it keeps the project's one-sided model. At the agent's nodes it branches
    /// over every legal move and takes the maximum; at the opponent's nodes it
    /// does NOT enumerate the opponent's options and take a minimum. The
    /// opponent makes a single deterministic greedy move — exactly the model
    /// used throughout <see cref="BeamSearch"/> (see
    /// <see cref="BeamSearch.SimulateGreedyOpponentMove"/>). With no min layer
    /// this is not Minimax; it is an exhaustive one-sided search.
    /// </para>
    /// <para>
    /// Memoization. Positions reached by different move orders are identical,
    /// and the Kalah position graph is a DAG — stones only ever flow forward
    /// into the stores. A memo table keyed on the full board folds that DAG so
    /// every distinct position is solved once. The key is the complete 14-cell
    /// board plus the side to move, encoded as a string so it is collision
    /// free: the solver caches EXACT outcomes, so — unlike the beam search's
    /// hash-based transposition table, which caches estimates and tolerates a
    /// rare hash collision — a collision here would corrupt a value the agent
    /// treats as certain.
    /// </para>
    /// <para>
    /// Determinism and termination. The solver uses no wall-clock time and no
    /// randomness, so its move is a pure function of the position. A budget on
    /// the number of distinct states caps the work: if a position is somehow
    /// larger than expected the solver abandons the attempt and
    /// <see cref="TrySolve"/> returns <c>false</c>, so the caller falls back to
    /// the beam search and the UI never stalls. Because every visit of a state
    /// spends from that budget, the budget also guarantees termination on its
    /// own, independently of the DAG argument.
    /// </para>
    /// </remarks>
    internal static class EndgameSolver
    {
        // ============================================================
        //  Activation and safety parameters
        // ============================================================

        /// <summary>
        /// The solver runs only when the number of stones still in the 12
        /// pits is strictly below this value. It is the same threshold the
        /// <see cref="PhaseAutomaton"/> uses for its Endgame phase, so the
        /// solver activates exactly when the game enters its endgame.
        /// </summary>
        private const int MaxStonesInPlayToSolve = 12;

        /// <summary>
        /// Upper bound on the number of distinct positions the solver will
        /// evaluate in a single call. Reaching it means the position is larger
        /// than expected; the solver then abandons the attempt deterministically
        /// rather than risk a long stall. Endgame positions below the
        /// activation threshold sit comfortably under this bound.
        /// </summary>
        private const int SolverStateBudget = 250_000;

        // ============================================================
        //  Public entry point
        // ============================================================

        /// <summary>
        /// Attempts to solve the position exactly for <paramref name="myPlayer"/>.
        /// </summary>
        /// <param name="engine">The current game state. Not mutated.</param>
        /// <param name="myPlayer">The player the solver chooses a move for.</param>
        /// <param name="bestMove">
        /// When the method returns <c>true</c>, the absolute board index of the
        /// move leading to the best guaranteed final score difference. Set to
        /// -1 when the method returns <c>false</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the position was inside the solver's reach and was
        /// solved exactly; <c>false</c> if the solver declined — the game is
        /// already over, too many stones remain, or the state budget was
        /// exceeded — in which case the caller should fall back to the beam
        /// search.
        /// </returns>
        public static bool TrySolve(GameEngine engine, Player myPlayer, out int bestMove)
        {
            bestMove = -1;

            // Decline outside the endgame window, and never solve a game that
            // has already finished.
            if (engine.IsGameOver() || StonesInPlay(engine) >= MaxStonesInPlayToSolve)
                return false;

            List<int> rootMoves = engine.GetValidMoves();
            if (rootMoves.Count == 0)
                return false;

            SolveContext context = new SolveContext();
            int bestValue = int.MinValue;

            // Solve each legal move's resulting position exactly and keep the
            // move with the highest true final score difference.
            foreach (int move in rootMoves)
            {
                GameEngine afterMove = engine.Clone();
                afterMove.ApplyMove(move);

                int value = SolveRecursive(afterMove, myPlayer, context);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestMove = move;
                }
            }

            // A state-budget overflow makes the values above partial and
            // untrustworthy — discard the whole result and let the caller
            // fall back to the approximate search.
            if (context.BudgetExhausted)
            {
                bestMove = -1;
                return false;
            }
            return true;
        }

        // ============================================================
        //  Recursive exact solve
        //
        //  SolveRecursive returns the TRUE final score difference (my final score minus
        //  the opponent's) reached by optimal agent play and greedy opponent
        //  play from `state`. "Final score" counts the stones a side will
        //  sweep into its store when the game ends, so a terminal position's
        //  value is exact, not estimated.
        // ============================================================

        private static int SolveRecursive(GameEngine state, Player myPlayer, SolveContext context)
        {
            // Once the budget is spent every further call returns immediately;
            // the unwinding values are discarded by TrySolve.
            if (context.BudgetExhausted)
                return 0;

            // A finished game is the base case — its outcome is known exactly.
            if (state.IsGameOver())
                return ExactScoreDifference(state, myPlayer);

            // Fold the DAG: a position already solved is reused, not re-searched.
            string key = BuildKey(state);
            if (context.TryGetCached(key, out int cached))
                return cached;

            context.CountNewState();

            int value = state.CurrentPlayer == myPlayer
                ? SolveMyTurn(state, myPlayer, context)
                : SolveOpponentTurn(state, myPlayer, context);

            context.Cache(key, value);
            return value;
        }

        // My turn: branch over EVERY legal move and take the maximum — the
        // agent will pick its best continuation. A move that earns an extra
        // turn leaves the turn with the agent, so the recursive call lands
        // back here and branches again; whole extra-turn chains are explored
        // with no special-case code.
        private static int SolveMyTurn(GameEngine state, Player myPlayer, SolveContext context)
        {
            List<int> moves = state.GetValidMoves();
            int best = int.MinValue;

            foreach (int move in moves)
            {
                GameEngine next = state.Clone();
                next.ApplyMove(move);

                int value = SolveRecursive(next, myPlayer, context);
                if (value > best)
                    best = value;
            }
            return best;
        }

        // Opponent's turn: a single deterministic transition — the opponent's
        // greedy move, the very model BeamSearch uses. This is the explicit
        // non-Minimax choice: the opponent's options are NOT enumerated and no
        // minimum is taken. An opponent extra turn lands back here and plays
        // another greedy move, so the opponent's chains unfold naturally too.
        private static int SolveOpponentTurn(GameEngine state, Player myPlayer, SolveContext context)
        {
            int opponentMove = BeamSearch.SimulateGreedyOpponentMove(state);

            GameEngine next = state.Clone();
            next.ApplyMove(opponentMove);
            return SolveRecursive(next, myPlayer, context);
        }

        // ============================================================
        //  Exact terminal value
        // ============================================================

        // The true score difference of a finished game. ApplyMove never sweeps
        // leftover stones into the stores (FinalizeBoard does), so a finished
        // board still has stones stranded in pits; each side's final score is
        // its store plus the stones still sitting on its own side.
        private static int ExactScoreDifference(GameEngine engine, Player myPlayer)
        {
            Player opponent = Opponent(myPlayer);
            int myFinal  = engine.GetScore(myPlayer)  + SideStoneCount(engine, myPlayer);
            int oppFinal = engine.GetScore(opponent) + SideStoneCount(engine, opponent);
            return myFinal - oppFinal;
        }

        // ============================================================
        //  Board measurements and state key
        // ============================================================

        // Total stones across the 12 playable pits (the two stores excluded).
        private static int StonesInPlay(GameEngine engine) =>
            Enumerable.Range(0, GameEngine.BoardSize)
                .Where(i => i != GameEngine.Player1Store && i != GameEngine.Player2Store)
                .Sum(i => engine.GetPitCount(i));

        // Stones sitting in one player's six pits (their store excluded).
        private static int SideStoneCount(GameEngine engine, Player player) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(player, i)));

        // A collision-free key for the memo table: the 14 cell counts followed
        // by the side to move. Two positions share a key only if they are the
        // identical position — essential because the memo caches exact values.
        private static string BuildKey(GameEngine engine)
        {
            string cells = string.Join(",", engine.Board);
            string turn = engine.CurrentPlayer == Player.Player1 ? "|P1" : "|P2";
            return cells + turn;
        }

        private static Player Opponent(Player player) =>
            player == Player.Player1 ? Player.Player2 : Player.Player1;

        // ============================================================
        //  Solve context — per-call working state
        //
        //  Holds the memo table and the remaining state budget for one
        //  TrySolve call, and is passed down the recursion. The solver class
        //  itself therefore stays stateless between calls, exactly like
        //  BeamSearch.
        // ============================================================

        private sealed class SolveContext
        {
            // Position key -> exact final score difference.
            private readonly Dictionary<string, int> _solved = new Dictionary<string, int>();

            // Counts DOWN from the budget; charged once per distinct state.
            private int _remainingStateBudget = SolverStateBudget;

            /// <summary><c>true</c> once more states have been visited than the budget allows.</summary>
            public bool BudgetExhausted => _remainingStateBudget < 0;

            /// <summary>Looks up the exact value cached for a position, if one exists.</summary>
            public bool TryGetCached(string key, out int value) =>
                _solved.TryGetValue(key, out value);

            /// <summary>Stores the exact value computed for a position.</summary>
            public void Cache(string key, int value) =>
                _solved[key] = value;

            /// <summary>Charges one distinct state against the budget.</summary>
            public void CountNewState() =>
                _remainingStateBudget--;
        }
    }
}
