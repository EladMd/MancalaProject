using System;
using System.Collections.Generic;

namespace MancalaProject
{
    /// <summary>
    /// A single node in the Beam Search frontier. Each node owns a snapshot
    /// of a board state reached during exploration, the heuristic value of
    /// that state from the perspective of the player to whom the agent
    /// belongs, and a reference to the original root-level move that led
    /// here (so the agent can recover its decision without traversing a
    /// parent chain).
    /// </summary>
    /// <remarks>
    /// <para>
    /// No parent pointer is stored — only a downward <see cref="Children"/>
    /// list and the <see cref="RootMove"/>. <see cref="RootMove"/> propagates
    /// unchanged from each child to its descendants, so once the backup pass
    /// picks the best root node the agent reads its move directly, with no
    /// upward path reconstruction. The <see cref="Children"/> list is what
    /// that backup pass walks to value each node from its descendants.
    /// </para>
    /// <para>
    /// Internal access modifier: this is an implementation detail of the
    /// search engine. Outside callers (including the WPF UI and the
    /// Console front-end) should not see it.
    /// </para>
    /// </remarks>
    internal sealed class SearchNode : IComparable<SearchNode>
    {
        /// <summary>The board state at this node.</summary>
        public GameEngine State { get; }

        /// <summary>
        /// The first move (from the real game state at the search root) on
        /// the path that reached this node. Used to answer the question
        /// "if I want to end up at this state, what move should I play now?"
        /// </summary>
        public int RootMove { get; }

        /// <summary>Plies traversed from the search root.</summary>
        public int Depth { get; }

        /// <summary>
        /// Heuristic value of <see cref="State"/> from the agent's perspective.
        /// Higher means better for the agent.
        /// </summary>
        public double H { get; }

        /// <summary>The classified game phase at this state.</summary>
        public GamePhase Phase { get; }

        /// <summary>
        /// Child nodes generated when this node was expanded. Empty if the
        /// node was never popped from the frontier (still a leaf in the
        /// search tree). Used by the backup pass at the end of the search
        /// to compute the value of this node from its descendants:
        /// my-turn nodes take the maximum value over their children, and
        /// opponent-turn nodes propagate their single greedy child's value.
        /// </summary>
        public List<SearchNode> Children { get; } = new List<SearchNode>();

        /// <summary>Constructs an immutable node.</summary>
        public SearchNode(GameEngine state, int rootMove, int depth, double h, GamePhase phase)
        {
            State    = state;
            RootMove = rootMove;
            Depth    = depth;
            H        = h;
            Phase    = phase;
        }

        /// <summary>
        /// Defines a "higher H = sorts first" ordering. Used by
        /// <see cref="System.Collections.Generic.List{T}.Sort()"/> in the
        /// beam-pruning step where children are ranked by heuristic value and
        /// only the top-K are kept on the frontier.
        /// </summary>
        public int CompareTo(SearchNode? other) =>
            other is null ? 1 : other.H.CompareTo(H);
    }
}
