using System;
using System.Collections.Generic;

// NOTE: this file deliberately uses our own MinHeap (see MinHeap.cs) rather
// than System.Collections.Generic.PriorityQueue so that the data structure
// implementing the search frontier is written by the student, not pulled
// from the .NET runtime.

namespace MancalaProject
{
    /// <summary>
    /// Best-First Beam Search engine over the board-state automaton. The
    /// agent's strategic decision is delegated to this class. The engine
    /// is deliberately stateless — all working data (frontier, transposition
    /// table, best-so-far node) lives on the call stack of
    /// <see cref="FindBestMove"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Algorithm in one paragraph: give every legal root move its own equal
    /// slice of the node budget and search its subtree separately. Within a
    /// subtree, maintain a priority queue ("frontier") of board states
    /// ordered by heuristic value; repeatedly pop the most promising node,
    /// generate its children, beam-prune to the top K children at branching
    /// points (my turn), or generate a single greedy child at non-branching
    /// points (opponent's turn). Expansion stops at a fixed lookahead horizon
    /// (see the difficulty parameters) so the search never outruns the greedy
    /// opponent model. Once every subtree is searched, a backup pass folds
    /// each subtree into a single value per root move, and the move with the
    /// highest backed-up value is returned.
    /// </para>
    /// <para>
    /// Why this is structurally not Minimax:
    /// <list type="number">
    /// <item><b>No min layer</b>: opponent moves are deterministic single-track
    ///       transitions (their greedy best move) — never a min over their options.</item>
    /// <item><b>One-sided backup</b>: the backup pass takes a max over my
    ///       children but never a min over the opponent's — opponent-turn
    ///       nodes have a single greedy child whose value is forwarded
    ///       unchanged. Minimax alternates max and min layers; this does not.</item>
    /// <item><b>Best-first, not depth-first</b>: order of expansion is by
    ///       priority queue, not by recursive descent.</item>
    /// <item><b>DAG via transposition table</b>: positions reached by
    ///       different move orders share an h-cache entry — the search graph
    ///       is a DAG, not a tree.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class BeamSearch
    {
        // ============================================================
        //  Difficulty parameters
        //
        //  Three knobs scale together with difficulty:
        //    MaxDepth  — how many plies ahead the search looks (its lookahead
        //                horizon). The PRIMARY strength knob.
        //    Budget    — ceiling on node expansions per search. A safety bound;
        //                with the depth horizon in place it rarely binds.
        //    BeamWidth — number of children kept after each branching expansion.
        //
        //  Why a depth horizon exists: the opponent is modelled as a single
        //  greedy move (see SimulateGreedyOpponentMove). That model is only
        //  trustworthy a few plies out — a real opponent will not keep playing
        //  the greedy move forever. Searching far past the horizon lets the
        //  max-over-my-moves backup "discover" lines that win ONLY because the
        //  greedy model keeps blundering, and then pick a move on the strength
        //  of that fantasy. Bounding the depth keeps every backed-up value
        //  anchored to a horizon where the opponent model is still sound.
        // ============================================================

        /// <summary>Easy: 2-ply horizon (my move + the opponent's reply).</summary>
        private const int EasyMaxDepth   = 2;
        /// <summary>Medium: 4-ply horizon.</summary>
        private const int MediumMaxDepth = 4;
        /// <summary>Hard: 6-ply horizon — strongest play.</summary>
        private const int HardMaxDepth   = 6;

        /// <summary>Easy: small node-expansion ceiling.</summary>
        private const int EasyBudget   = 100;
        /// <summary>Medium: moderate node-expansion ceiling.</summary>
        private const int MediumBudget = 500;
        /// <summary>Hard: large node-expansion ceiling.</summary>
        private const int HardBudget   = 2000;

        /// <summary>Easy: narrow beam — explores fewer alternatives.</summary>
        private const int EasyBeamWidth   = 4;
        /// <summary>Medium: full beam (≥ max branching factor of 6) so no children are dropped at top level.</summary>
        private const int MediumBeamWidth = 8;
        /// <summary>Hard: full beam — strength comes from the deeper horizon.</summary>
        private const int HardBeamWidth   = 8;

        // ============================================================
        //  UI-coupled timing
        //  The search will return early if it runs out of wall-clock time
        //  even before its node budget is exhausted, so the UI never blocks
        //  beyond this duration.
        // ============================================================

        /// <summary>
        /// Hard upper bound on search wall-clock time. The agent returns
        /// the best move it has found so far if this is reached before the
        /// node budget is exhausted. Raised from 4000 ms to 5000 ms when
        /// the chain-greedy picker switched from the cheap
        /// "score-immediate" rule to the full heuristic evaluation; each
        /// chain step is now noticeably more expensive, so we extend the
        /// wall-clock budget to preserve search depth.
        /// </summary>
        private const int MaxThinkTimeMs = 5000;

        // ============================================================
        //  Public entry point
        // ============================================================

        /// <summary>
        /// Selects the best move for <paramref name="myPlayer"/> at the given
        /// state, by running a Best-First Beam Search bounded by difficulty.
        /// </summary>
        /// <param name="engine">The current game state. Not mutated.</param>
        /// <param name="myPlayer">The player on whose behalf the search runs.</param>
        /// <param name="difficulty">Selects the lookahead horizon, node budget and beam width.</param>
        /// <returns>The absolute board index of the best move found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no legal moves exist.</exception>
        public static int FindBestMove(GameEngine engine, Player myPlayer, Difficulty difficulty)
        {
            List<int> validMoves = engine.GetValidMoves();
            if (validMoves.Count == 0)
                throw new InvalidOperationException("No valid moves available.");
            if (validMoves.Count == 1)
                return validMoves[0];

            int beamWidth = BeamWidthFor(difficulty);
            int maxDepth  = MaxDepthFor(difficulty);

            // The node budget is partitioned EQUALLY among the root moves:
            // every candidate move's subtree gets its own best-first search
            // with the same expansion budget. A single shared frontier would
            // let best-first pour almost the whole budget into whichever move
            // looked best at ply 1, leaving its siblings expanded barely past
            // depth 1. The backup pass would then compare one deeply searched
            // move against shallow siblings whose value is still the raw,
            // over-optimistic h of a near-unexplored position. Equal budget
            // per root ⇒ comparable search depth ⇒ a fair root comparison.
            int perRootBudget = BudgetFor(difficulty) / validMoves.Count;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(MaxThinkTimeMs);

            // Transposition table, shared across every root subtree: state
            // hash → cached h. h is a pure function of the position (myPlayer
            // is fixed for the whole search; phase is derived from the board),
            // so a value cached while searching one root move is valid for an
            // identical position reached under another — no need to re-evaluate.
            Dictionary<long, double> transposition = new Dictionary<long, double>();

            // One root node per legal move. Each node is the entry point of —
            // and, through its Children list, retains — its own searched
            // subtree. The final answer is selected from THIS list by
            // backed-up value, not by raw h.
            List<SearchNode> rootChildren = new List<SearchNode>(validMoves.Count);

            // Search each root move's subtree in turn, every one under its own
            // equal slice of the budget. Each root child carries its own move
            // as RootMove, propagated unchanged to every descendant, so the
            // node finally chosen maps back to a concrete move to play.
            foreach (int move in validMoves)
            {
                SearchNode rootChild = CreateChildNode(
                    parentState: engine,
                    move: move,
                    rootMove: move,
                    parentDepth: 0,
                    transposition: transposition,
                    myPlayer: myPlayer);

                rootChildren.Add(rootChild);
                SearchRootSubtree(rootChild, perRootBudget, deadline,
                                  transposition, myPlayer, beamWidth, maxDepth);
            }

            // Backup pass: compute the realistic value of each root move by
            // propagating leaf h-values back to the root according to the play
            // model — max over my responses, single greedy child for the
            // opponent's. This replaces the naïve "best h ever seen anywhere"
            // selection that previously over-rewarded paths passing through
            // catastrophic intermediate states.
            return SelectBestRootMove(rootChildren, myPlayer);
        }

        // ============================================================
        //  Per-root subtree search
        // ============================================================

        // Runs a bounded Best-First Beam Search confined to a SINGLE root
        // move's subtree. The frontier — our own MinHeap (see MinHeap.cs) —
        // is seeded with just that root child and pushed/popped with
        // priority = -h, so the node popped first is always the highest-h
        // (most promising) state discovered within this subtree. Expansion
        // continues until a node reaches the lookahead horizon (maxDepth),
        // the per-root node budget is spent, the wall-clock deadline passes,
        // or the subtree is exhausted. The tree it builds hangs off
        // rootChild.Children for the later backup pass.
        private static void SearchRootSubtree(SearchNode rootChild,
                                              int budget,
                                              DateTime deadline,
                                              Dictionary<long, double> transposition,
                                              Player myPlayer,
                                              int beamWidth,
                                              int maxDepth)
        {
            MinHeap<SearchNode, double> frontier = new MinHeap<SearchNode, double>();
            frontier.Push(rootChild, -rootChild.H);

            int expanded = 0;
            while (expanded < budget
                   && DateTime.UtcNow < deadline
                   && frontier.Count > 0)
            {
                SearchNode current = frontier.Pop();

                // Expand only nodes that have successors worth exploring:
                //   - not terminal (a finished game has no successors), and
                //   - not yet at the lookahead horizon (a node at maxDepth is
                //     a leaf, evaluated by its heuristic value).
                // Nodes skipped here are not counted against the budget, so
                // the budget measures real expansion work only.
                if (!current.State.IsGameOver() && current.Depth < maxDepth)
                {
                    ExpandNode(current, frontier, transposition, myPlayer, beamWidth);
                    expanded++;
                }
            }
        }

        // ============================================================
        //  Backup pass: turn the explored search graph into a single
        //  value per root move and pick the best one. Recursive, runs
        //  in O(N) total where N is the number of expanded nodes.
        // ============================================================

        private static int SelectBestRootMove(List<SearchNode> rootChildren, Player myPlayer)
        {
            SearchNode best = rootChildren[0];
            double bestValue = BackupValue(best, myPlayer);

            for (int i = 1; i < rootChildren.Count; i++)
            {
                double v = BackupValue(rootChildren[i], myPlayer);
                if (v > bestValue)
                {
                    bestValue = v;
                    best = rootChildren[i];
                }
            }
            return best.RootMove;
        }

        // Recursively propagate values from descendants to ancestors.
        //
        //   leaf (never expanded) → V = h(state)
        //   my-turn node          → V = max over children's V         (I'll pick the best response)
        //   opponent-turn node    → V = the single greedy child's V   (deterministic prediction)
        //
        // This is NOT minimax: opponent-turn nodes never enumerate the
        // opponent's options and take a min. There is only one child per
        // opponent node (the greedy move our model predicted) and we just
        // forward its value upward.
        private static double BackupValue(SearchNode node, Player myPlayer)
        {
            if (node.Children.Count == 0)
                return node.H;

            if (node.State.CurrentPlayer == myPlayer)
            {
                double best = double.NegativeInfinity;
                foreach (SearchNode child in node.Children)
                {
                    double v = BackupValue(child, myPlayer);
                    if (v > best) best = v;
                }
                return best;
            }

            // Opponent's turn at this state — single deterministic transition.
            return BackupValue(node.Children[0], myPlayer);
        }

        // ============================================================
        //  Difficulty → parameter mapping
        // ============================================================

        private static int MaxDepthFor(Difficulty d) =>
            d switch
            {
                Difficulty.Easy   => EasyMaxDepth,
                Difficulty.Medium => MediumMaxDepth,
                _                 => HardMaxDepth
            };

        private static int BudgetFor(Difficulty d) =>
            d switch
            {
                Difficulty.Easy   => EasyBudget,
                Difficulty.Medium => MediumBudget,
                _                 => HardBudget
            };

        private static int BeamWidthFor(Difficulty d) =>
            d switch
            {
                Difficulty.Easy   => EasyBeamWidth,
                Difficulty.Medium => MediumBeamWidth,
                _                 => HardBeamWidth
            };

        // ============================================================
        //  Node expansion (asymmetric — branching only on my turn)
        // ============================================================

        private static void ExpandNode(SearchNode current,
                                       MinHeap<SearchNode, double> frontier,
                                       Dictionary<long, double> transposition,
                                       Player myPlayer,
                                       int beamWidth)
        {
            if (current.State.CurrentPlayer == myPlayer)
                ExpandMyNode(current, frontier, transposition, myPlayer, beamWidth);
            else
                ExpandOpponentNode(current, frontier, transposition, myPlayer);
        }

        // My turn: branch — generate every legal move and keep the top K
        // children by h. This is the source of search depth in our algorithm.
        private static void ExpandMyNode(SearchNode current,
                                         MinHeap<SearchNode, double> frontier,
                                         Dictionary<long, double> transposition,
                                         Player myPlayer,
                                         int beamWidth)
        {
            List<int> myMoves = current.State.GetValidMoves();
            List<SearchNode> children = new List<SearchNode>(myMoves.Count);

            foreach (int m in myMoves)
            {
                SearchNode child = CreateChildNode(
                    parentState: current.State,
                    move: m,
                    rootMove: current.RootMove,
                    parentDepth: current.Depth,
                    transposition: transposition,
                    myPlayer: myPlayer);
                children.Add(child);
            }

            // Sort by h descending — SearchNode.CompareTo defines that ordering.
            children.Sort();

            int take = Math.Min(beamWidth, children.Count);
            for (int i = 0; i < take; i++)
            {
                frontier.Push(children[i], -children[i].H);
                current.Children.Add(children[i]);
            }
        }

        // Opponent's turn: pretend they play their greedy best move, produce
        // exactly ONE child. This is the explicit non-Minimax design choice —
        // we do NOT enumerate the opponent's moves and we do NOT take a min.
        private static void ExpandOpponentNode(SearchNode current,
                                               MinHeap<SearchNode, double> frontier,
                                               Dictionary<long, double> transposition,
                                               Player myPlayer)
        {
            int oppMove = SimulateGreedyOpponentMove(current.State);
            SearchNode child = CreateChildNode(
                parentState: current.State,
                move: oppMove,
                rootMove: current.RootMove,
                parentDepth: current.Depth,
                transposition: transposition,
                myPlayer: myPlayer);
            frontier.Push(child, -child.H);
            current.Children.Add(child);
        }

        // ============================================================
        //  Child-node creation (used by both root seeding and expansion)
        // ============================================================

        private static SearchNode CreateChildNode(GameEngine parentState,
                                                  int move,
                                                  int rootMove,
                                                  int parentDepth,
                                                  Dictionary<long, double> transposition,
                                                  Player myPlayer)
        {
            // Capture WHO is moving BEFORE applying. The engine flips
            // CurrentPlayer when the move ends without an extra turn —
            // we need the pre-move identity to drive RunChainGreedy.
            Player movingPlayer = parentState.CurrentPlayer;

            GameEngine childState = parentState.Clone();
            MoveResult result = childState.ApplyMove(move);

            // If the move granted an extra turn, the same player keeps
            // playing greedy moves until control passes (or game ends).
            // We aggregate the entire chain into a single search node so
            // one "transition" in our state automaton corresponds to one
            // FULL turn, not one stone-sowing step.
            if (result.ExtraTurn)
                RunChainGreedy(childState, movingPlayer);

            GamePhase phase = PhaseAutomaton.Detect(childState, myPlayer);
            long hash = HashState(childState);
            double h = LookupOrEvaluate(childState, hash, phase, myPlayer, transposition);

            return new SearchNode(childState, rootMove, parentDepth + 1, h, phase);
        }

        private static double LookupOrEvaluate(GameEngine state,
                                               long hash,
                                               GamePhase phase,
                                               Player myPlayer,
                                               Dictionary<long, double> transposition)
        {
            if (transposition.TryGetValue(hash, out double cached))
                return cached;

            double h = Heuristic.Evaluate(state, myPlayer, phase);
            transposition[hash] = h;
            return h;
        }

        // ============================================================
        //  Greedy chain: keep playing the best move (by full heuristic
        //  evaluation) while the same player's extra turns continue.
        //  Mutates the engine in place.
        //
        //  Why full heuristic instead of just immediate-score: a simple
        //  "ΔS + extra-turn bonus + capture bonus" picker is blind to
        //  defensive value. It cannot see that emptying a vulnerable pit
        //  defuses a 6-stone opponent capture next turn, because that
        //  defensive move scores 0 immediately. The full heuristic — which
        //  already accounts for both sides' capture threats asymmetrically
        //  by turn — exposes that information, so a chain played by the
        //  full heuristic prefers defensive sweeps over modest gains when
        //  the gain is shadowed by a larger opponent threat.
        // ============================================================

        private static void RunChainGreedy(GameEngine state, Player movingPlayer)
        {
            while (CanContinueChain(state, movingPlayer))
            {
                int bestMove = PickBestChainMove(state, movingPlayer);
                state.ApplyMove(bestMove);
            }
        }

        private static bool CanContinueChain(GameEngine state, Player movingPlayer) =>
            state.CurrentPlayer == movingPlayer
            && !state.IsGameOver()
            && state.GetValidMoves().Count > 0;

        // Selects the chain move whose resulting position has the highest
        // full-heuristic value from <paramref name="forPlayer"/>'s perspective.
        // Each candidate is tried on a cloned engine; chains that continue
        // (extra turn) are handled by the outer RunChainGreedy loop, which
        // calls this picker again from the new position.
        private static int PickBestChainMove(GameEngine state, Player forPlayer)
        {
            List<int> moves = state.GetValidMoves();
            int best = moves[0];
            double bestH = double.NegativeInfinity;

            foreach (int m in moves)
            {
                GameEngine sim = state.Clone();
                sim.ApplyMove(m);
                GamePhase phase = PhaseAutomaton.Detect(sim, forPlayer);
                double h = Heuristic.Evaluate(sim, forPlayer, phase);

                if (h > bestH)
                {
                    bestH = h;
                    best = m;
                }
            }
            return best;
        }

        // ============================================================
        //  Greedy opponent transition (single-track, NOT a min layer)
        // ============================================================

        // The opponent picks the move that maximizes THEIR own heuristic value
        // (not the move that minimizes ours). This is the explicit modeling
        // choice that makes our algorithm distinct from Minimax: we assume the
        // opponent plays greedily for themselves, not adversarially against us.
        private static int SimulateGreedyOpponentMove(GameEngine state)
        {
            Player opponent = state.CurrentPlayer;
            List<int> moves = state.GetValidMoves();

            int bestMove = moves[0];
            double bestH = double.NegativeInfinity;

            foreach (int m in moves)
            {
                GameEngine sim = state.Clone();
                sim.ApplyMove(m);
                RunChainGreedy(sim, opponent);   // play out opponent's chain too

                GamePhase oppPhase = PhaseAutomaton.Detect(sim, opponent);
                double oppH = Heuristic.Evaluate(sim, opponent, oppPhase);

                if (oppH > bestH)
                {
                    bestH = oppH;
                    bestMove = m;
                }
            }
            return bestMove;
        }

        // ============================================================
        //  State hashing for the transposition table
        // ============================================================

        // FNV-1a 64-bit. Cryptographic strength is not required — only a
        // good distribution over Mancala's reachable states. Combines the
        // 14 pit counts and the current player into a single long.
        private static long HashState(GameEngine state)
        {
            const long FnvOffsetBasis = unchecked((long)0xcbf29ce484222325);
            const long FnvPrime       = 0x100000001b3;

            long hash = FnvOffsetBasis;
            for (int i = 0; i < GameEngine.BoardSize; i++)
            {
                hash ^= state.GetPitCount(i);
                hash *= FnvPrime;
            }
            hash ^= (int)state.CurrentPlayer;
            hash *= FnvPrime;
            return hash;
        }
    }
}
