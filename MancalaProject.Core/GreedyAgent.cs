using System;
using System.Collections.Generic;
using System.Linq;

namespace MancalaProject
{
    /// <summary>
    /// Specifies how strong the computer agent should play. Stronger settings use
    /// more lookahead and less random noise; weaker settings deliberately add noise
    /// and skip parts of the evaluation.
    /// </summary>
    public enum Difficulty
    {
        /// <summary>No opponent lookahead, no endgame rollout, large random noise.</summary>
        Easy,
        /// <summary>Opponent lookahead enabled, no endgame rollout, small random noise.</summary>
        Medium,
        /// <summary>Full algorithm: chain simulation, opponent lookahead, starvation, and endgame rollout.</summary>
        Hard
    }

    /// <summary>
    /// Adversarial Greedy Search agent for Mancala. Evaluates each candidate move by
    /// simulating the player's full chain of extra turns, modeling the opponent as a
    /// fixed greedy player, and combining a heuristic on the resulting position with
    /// a starvation-strategy penalty. In the late game it switches to a deterministic
    /// greedy rollout that plays a single line forward to a terminal state.
    /// </summary>
    public class GreedyAgent
    {
        private readonly Player _myPlayer;
        private readonly Player _opponent;
        private readonly Difficulty _difficulty;
        private readonly Random _rng = new Random();

        // --- Heuristic weights ---
        private const double W1 = 1.0;          // Score-difference weight
        private const double W2 = 0.6;          // Capture-potential weight
        private const double W3 = 0.3;          // Board-control (hoarding) weight

        // --- Endgame ---
        private const int EndgameThreshold = 12;        // Stones-on-board threshold for switching to rollout

        // --- Difficulty noise ranges (random value added to each move's score) ---
        private const double EasyNoiseRange = 3.0;
        private const double MediumNoiseRange = 1.0;

        // --- Immediate-move bonuses (used in chain scoring and rollouts) ---
        private const double ExtraTurnScoreBonus = 5.0;
        private const double CapturePerStoneBonus = 2.0;

        // --- Capture-potential sub-weights (multiply oppositeStones in the heuristic) ---
        private const double PassiveCaptureWeight = 0.5;   // Empty pit, opponent has stones opposite
        private const double ActiveCaptureWeight = 1.0;    // We can also reach the empty pit this turn

        // --- Dynamic W3 thresholds (adjust hoarding behavior by score gap) ---
        private const int LargeScoreGap = 8;               // Absolute lead/deficit considered "big"
        private const double TrailingBigW3Multiplier = 1.5;

        // --- Starvation strategy ---
        private const int StarvationOpponentStoneThreshold = 3;  // Trigger when opponent has ≤ this many stones
        private const double StarvationPenaltyPerStone = 3.0;

        /// <summary>
        /// Creates a new greedy agent that plays as the given player at the given difficulty.
        /// </summary>
        /// <param name="player">The side this agent will play.</param>
        /// <param name="difficulty">Strength setting; defaults to <see cref="Difficulty.Hard"/>.</param>
        public GreedyAgent(Player player, Difficulty difficulty = Difficulty.Hard)
        {
            _myPlayer = player;
            _opponent = OtherPlayer(player);
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
        public int CalculateMove(GameEngine engine)
        {
            List<int> moves = engine.GetValidMoves();
            return moves.Count switch
            {
                0 => throw new InvalidOperationException("No valid moves available."),
                1 => moves[0],
                _ => SelectBestMove(engine, moves)
            };
        }

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

        // =====================================================================
        //  Move selection
        // =====================================================================

        private int SelectBestMove(GameEngine engine, List<int> moves) =>
            ShouldUseEndgameSolver(engine)
                ? SelectEndgameMove(engine, moves)
                : SelectGreedyMove(engine, moves);

        private bool ShouldUseEndgameSolver(GameEngine engine) =>
            _difficulty == Difficulty.Hard && IsEndgame(engine);

        private int SelectEndgameMove(GameEngine engine, List<int> moves) =>
            moves.OrderByDescending(m => SimulateEndgameRollout(engine, m)).First();

        private int SelectGreedyMove(GameEngine engine, List<int> moves) =>
            moves.OrderByDescending(m => EvaluateGreedyMove(engine, m)).First();

        private double EvaluateGreedyMove(GameEngine engine, int move)
        {
            GameEngine afterMyTurn = SimulateChain(engine, move, _myPlayer);
            double positionScore = EvaluatePositionByDifficulty(afterMyTurn);
            double starvation = ComputeStarvationPenalty(engine, afterMyTurn);
            double noise = GetDifficultyNoise();
            return positionScore + starvation + noise;
        }

        private double EvaluatePositionByDifficulty(GameEngine afterMyTurn) =>
            _difficulty switch
            {
                Difficulty.Easy => ComputeHeuristic(afterMyTurn, _myPlayer),
                _               => ComputeHeuristic(SimulateOpponentGreedyResponse(afterMyTurn), _myPlayer)
            };

        private double GetDifficultyNoise() =>
            _difficulty switch
            {
                Difficulty.Easy   => _rng.NextDouble() * EasyNoiseRange,
                Difficulty.Medium => _rng.NextDouble() * MediumNoiseRange,
                _                 => 0.0
            };

        // =====================================================================
        //  Chain simulation
        // =====================================================================

        private double ScoreImmediateMove(GameEngine engine, int move, Player forPlayer)
        {
            GameEngine sim = engine.Clone();
            int scoreBefore = sim.GetScore(forPlayer);
            MoveResult result = sim.ApplyMove(move);
            double scoreDelta = sim.GetScore(forPlayer) - scoreBefore;
            return scoreDelta + ExtraTurnBonus(result) + CaptureBonus(result);
        }

        private double ExtraTurnBonus(MoveResult r) =>
            r.ExtraTurn ? ExtraTurnScoreBonus : 0.0;

        private double CaptureBonus(MoveResult r) =>
            r.CaptureOccurred ? r.CapturedStoneCount * CapturePerStoneBonus : 0.0;

        private GameEngine SimulateChain(GameEngine engine, int firstMove, Player forPlayer)
        {
            GameEngine sim = engine.Clone();
            MoveResult result = sim.ApplyMove(firstMove);
            return ContinueChainWhilePossible(sim, result, forPlayer);
        }

        private GameEngine ContinueChainWhilePossible(GameEngine sim, MoveResult lastResult, Player forPlayer)
        {
            MoveResult current = lastResult;
            while (CanContinueChain(sim, current))
            {
                current = PlayBestChainMove(sim, forPlayer);
            }
            return sim;
        }

        private bool CanContinueChain(GameEngine sim, MoveResult lastResult) =>
            lastResult.ExtraTurn && !sim.IsGameOver() && sim.GetValidMoves().Count > 0;

        private MoveResult PlayBestChainMove(GameEngine sim, Player forPlayer)
        {
            int bestMove = sim.GetValidMoves()
                .OrderByDescending(m => ScoreImmediateMove(sim, m, forPlayer))
                .First();
            return sim.ApplyMove(bestMove);
        }

        // =====================================================================
        //  Opponent lookahead
        // =====================================================================

        // The opponent is modeled as a fixed greedy player: they pick the
        // move that maximizes THEIR own heuristic — not the move that
        // minimizes ours. This is what distinguishes Adversarial Greedy
        // Search from a min layer: we don't assume the opponent has access
        // to our evaluation function, only that they play greedily for
        // themselves. The result is then re-evaluated from our perspective.
        private GameEngine SimulateOpponentGreedyResponse(GameEngine state) =>
            CanOpponentRespond(state)
                ? PickOpponentGreedyMove(state)
                : state;

        private bool CanOpponentRespond(GameEngine state) =>
            !state.IsGameOver() && state.GetValidMoves().Count > 0;

        private GameEngine PickOpponentGreedyMove(GameEngine state) =>
            state.GetValidMoves()
                .Select(m => SimulateChain(state, m, _opponent))
                .OrderByDescending(after => ComputeHeuristic(after, _opponent))
                .First();

        // =====================================================================
        //  Heuristic:  h(n) = w1*ΔS + w2*C + w3_dynamic*D
        // =====================================================================

        private double ComputeHeuristic(GameEngine engine, Player perspective)
        {
            double deltaS = ScoreDifference(engine, perspective);
            double capturePotential = ComputeCapturePotential(engine, perspective);
            double boardControl = ComputeBoardControl(engine, perspective);
            double w3 = ComputeDynamicW3(engine, perspective);
            return W1 * deltaS + W2 * capturePotential + w3 * boardControl;
        }

        private double ScoreDifference(GameEngine engine, Player perspective) =>
            engine.GetScore(perspective) - engine.GetScore(OtherPlayer(perspective));

        private double ComputeCapturePotential(GameEngine engine, Player perspective) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(i => engine.GetPitIndex(perspective, i))
                .Where(pit => engine.GetPitCount(pit) == 0)
                .Sum(pit => ComputePitCapturePotential(engine, perspective, pit));

        private double ComputePitCapturePotential(GameEngine engine, Player perspective, int emptyPit)
        {
            int oppositeStones = engine.GetPitCount(engine.GetOppositePitIndex(emptyPit));
            if (oppositeStones == 0) return 0.0;

            return PassiveCapturePotential(oppositeStones)
                 + ActiveCapturePotential(engine, perspective, emptyPit, oppositeStones);
        }

        private double PassiveCapturePotential(int oppositeStones) =>
            oppositeStones * PassiveCaptureWeight;

        private double ActiveCapturePotential(GameEngine engine, Player perspective,
                                              int targetPit, int oppositeStones) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(j => engine.GetPitIndex(perspective, j))
                .Where(src => src != targetPit && engine.GetPitCount(src) > 0)
                .Where(src => CanSowExactlyInto(engine, perspective, src, targetPit))
                .Sum(_ => oppositeStones * ActiveCaptureWeight);

        private bool CanSowExactlyInto(GameEngine engine, Player perspective, int sourcePit, int targetPit)
        {
            int sourceStones = engine.GetPitCount(sourcePit);
            int rawDistance = (targetPit - sourcePit + GameEngine.BoardSize) % GameEngine.BoardSize;
            int sowDistance = AdjustDistanceForSkippedStore(sourcePit, rawDistance, GetSkippedStore(perspective));
            return sourceStones == sowDistance;
        }

        private int AdjustDistanceForSkippedStore(int sourcePit, int rawDistance, int skippedStore) =>
            PathCrossesStore(sourcePit, rawDistance, skippedStore) ? rawDistance - 1 : rawDistance;

        private bool PathCrossesStore(int sourcePit, int distance, int store) =>
            Enumerable.Range(1, distance)
                .Any(step => (sourcePit + step) % GameEngine.BoardSize == store);

        private int GetSkippedStore(Player perspective) =>
            perspective == Player.Player1 ? GameEngine.Player2Store : GameEngine.Player1Store;

        private double ComputeBoardControl(GameEngine engine, Player perspective) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(perspective, i)));

        private double ComputeDynamicW3(GameEngine engine, Player perspective)
        {
            double scoreDiff = ScoreDifference(engine, perspective);
            if (scoreDiff >=  LargeScoreGap) return -W3;                          // Leading big — rush to finish
            if (scoreDiff <= -LargeScoreGap) return W3 * TrailingBigW3Multiplier; // Trailing big — hoard stones
            return W3;
        }

        // =====================================================================
        //  Starvation strategy — discourage feeding a nearly-empty opponent
        // =====================================================================

        private double ComputeStarvationPenalty(GameEngine before, GameEngine after)
        {
            int stonesBefore = CountOpponentStones(before);
            if (stonesBefore > StarvationOpponentStoneThreshold) return 0.0;   // Not in starvation zone

            return StarvationPenaltyFromDelta(stonesBefore, CountOpponentStones(after));
        }

        private double StarvationPenaltyFromDelta(int before, int after)
        {
            int stonesGiven = after - before;
            return stonesGiven > 0 ? -stonesGiven * StarvationPenaltyPerStone : 0.0;
        }

        private int CountOpponentStones(GameEngine engine) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Sum(i => engine.GetPitCount(engine.GetPitIndex(_opponent, i)));

        // =====================================================================
        //  Endgame finishing — deterministic greedy rollout to terminal state
        //
        //  When few stones remain, we evaluate each candidate move by playing
        //  one full game line forward to game-over: at every step (ours and
        //  the opponent's) the side to move chooses its immediate-greedy best
        //  move under the same heuristic used by the main agent. The terminal
        //  score difference is the move's value.
        //
        //  This is a single-line forward simulation (one playout per candidate
        //  first move). It deliberately avoids:
        //    - alternating max/min layers,
        //    - any α/β bookkeeping or cutoff,
        //    - random sampling.
        //  It is the natural extension of Adversarial Greedy Search down to
        //  the terminal node, with the opponent modeled as a fixed greedy
        //  player rather than as a worst-case adversary.
        // =====================================================================

        private bool IsEndgame(GameEngine engine) =>
            Enumerable.Range(0, GameEngine.BoardSize)
                .Where(i => i != GameEngine.Player1Store && i != GameEngine.Player2Store)
                .Sum(i => engine.Board[i]) <= EndgameThreshold;

        private double SimulateEndgameRollout(GameEngine engine, int firstMove)
        {
            GameEngine sim = ApplyToClone(engine, firstMove);
            PlayGreedyToTermination(sim);
            return FinalScoreDifference(sim);
        }

        private void PlayGreedyToTermination(GameEngine sim)
        {
            while (!sim.IsGameOver())
            {
                sim.ApplyMove(GreedyMoveForCurrentPlayer(sim));
            }
        }

        private int GreedyMoveForCurrentPlayer(GameEngine sim) =>
            sim.GetValidMoves()
                .OrderByDescending(m => ScoreImmediateMove(sim, m, sim.CurrentPlayer))
                .First();

        private double FinalScoreDifference(GameEngine sim)
        {
            GameEngine fin = sim.Clone();
            fin.FinalizeBoard();
            return fin.GetScore(_myPlayer) - fin.GetScore(_opponent);
        }

        // =====================================================================
        //  Utilities
        // =====================================================================

        private GameEngine ApplyToClone(GameEngine engine, int move)
        {
            GameEngine sim = engine.Clone();
            sim.ApplyMove(move);
            return sim;
        }

        private static Player OtherPlayer(Player p) =>
            p == Player.Player1 ? Player.Player2 : Player.Player1;
    }
}
