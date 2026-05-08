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

        // ============================================================
        //  Heuristic weights:  h(n) = W1·ΔS + W2·C + W3·D
        //
        //  The three weights form a deliberate certainty hierarchy:
        //      W1 (realized) > W2 (potential) > W3 (positional)
        //  Each subsequent component represents an advantage that is
        //  one step further from being banked, so it earns proportionally
        //  less weight. W1 is the anchor: 1 stone in the store = 1.0 unit
        //  of evaluation, and every other weight is expressed relative
        //  to that natural unit.
        // ============================================================

        /// <summary>
        /// Weight on the realized score difference (my store minus opponent's).
        /// </summary>
        /// <remarks>
        /// Set to 1.0 deliberately as the heuristic's "natural unit". Stones
        /// already in the store are the only fully-realized advantage in
        /// Mancala — they cannot be undone by the opponent. Anchoring the
        /// scale here makes every other constant interpretable as "fraction
        /// of one banked stone". A higher W1 would make the agent purely
        /// score-greedy and blind to positional play; a lower W1 would
        /// undervalue the only certain quantity on the board.
        /// </remarks>
        private const double W1 = 1.0;

        /// <summary>
        /// Weight on capture potential (empty pits with opposing stones across).
        /// </summary>
        /// <remarks>
        /// Set below W1 because capture potential is hypothetical — it pays
        /// off only if the configuration survives the opponent's next move.
        /// Set above 0.5 because capture is the highest-leverage tactical
        /// element in Mancala (a single capture often swings 4–8 stones).
        /// 0.6 is a calibrated middle: an opportunity is worth roughly
        /// "60% of one banked stone" of evaluation pressure.
        /// </remarks>
        private const double W2 = 0.6;

        /// <summary>
        /// Weight on board control — total stones on my side of the board.
        /// </summary>
        /// <remarks>
        /// Smallest of the three weights because stones on your side carry
        /// only positional meaning: they preserve flexibility for future
        /// turns but have no committed value. 0.3 prevents the agent from
        /// over-hoarding (which would defer scoring indefinitely) while
        /// still rewarding "having ammunition". Note that this weight is
        /// adjusted dynamically by <see cref="ComputeDynamicW3"/> based on
        /// the current score gap.
        /// </remarks>
        private const double W3 = 0.3;

        // ============================================================
        //  Endgame switch
        // ============================================================

        /// <summary>
        /// Stones-on-board threshold below which the agent switches from
        /// the heuristic evaluator to a deterministic greedy rollout to terminal.
        /// </summary>
        /// <remarks>
        /// 12 = one quarter of the 48 stones the game starts with (6 pits ×
        /// 4 stones × 2 players). Below this point the branching factor has
        /// collapsed enough that one full forward simulation per candidate
        /// move is computationally cheap, and the heuristic's approximations
        /// matter less than the actual terminal score. Switching too early
        /// (e.g. at 24) explodes the work; too late (e.g. at 6) misses the
        /// window where rollout still has decision power.
        /// </remarks>
        private const int EndgameThreshold = 12;

        // ============================================================
        //  Difficulty noise
        //  Noise is uniform in [0, range) and added to every candidate
        //  move's score, so larger ranges can flip the agent's choice
        //  between two close moves and produce visible "mistakes".
        // ============================================================

        /// <summary>
        /// Maximum random noise added per move on Easy difficulty.
        /// </summary>
        /// <remarks>
        /// 3.0 is intentionally larger than W1 (1.0) and W2 (0.6) combined.
        /// At this magnitude noise can override the agent's preference even
        /// when one move looks clearly better, producing the kind of
        /// "obvious blunder" a beginner opponent should make. Without enough
        /// noise, Easy would still play a strong-ish heuristic and feel
        /// indistinguishable from Hard to a casual user.
        /// </remarks>
        private const double EasyNoiseRange = 3.0;

        /// <summary>
        /// Maximum random noise added per move on Medium difficulty.
        /// </summary>
        /// <remarks>
        /// Equal to W1 (1.0). Calibrated so noise can perturb the agent's
        /// choice between two roughly-equal moves but cannot override a
        /// clear preference (where the gap exceeds one banked stone).
        /// Net effect: Medium plays "reasonable but slightly inconsistent",
        /// which feels human-like.
        /// </remarks>
        private const double MediumNoiseRange = 1.0;

        // ============================================================
        //  Immediate-move bonuses
        //  Used by ScoreImmediateMove during chain selection and the
        //  endgame rollout — i.e. when picking the best NEXT move from
        //  a given state, without lookahead.
        // ============================================================

        /// <summary>
        /// Bonus added to a candidate move's immediate score if it grants an extra turn.
        /// </summary>
        /// <remarks>
        /// 5.0 reflects three compounding benefits of an extra turn:
        ///   (1) the player gets to play another move (worth ~1–2 expected score points),
        ///   (2) tempo is preserved (the opponent does not get a turn at all), and
        ///   (3) chained extra turns become possible (rare but very high-value).
        /// 5.0 is intentionally conservative — empirically the true expected
        /// value of an extra turn is often higher in mid-game, but this
        /// bonus prevents the agent from ignoring scoring moves in favor
        /// of every minor "land in the store" opportunity.
        /// </remarks>
        private const double ExtraTurnScoreBonus = 5.0;

        /// <summary>
        /// Bonus added to a candidate move's immediate score per stone captured.
        /// </summary>
        /// <remarks>
        /// 2.0 because every captured stone produces a TWO-point swing:
        ///   +1 for our store, –1 to opponent's potential pool.
        /// Equivalently: a 4-stone capture moves the score-gap by 8.
        /// Setting this below 2.0 would underweight captures relative to
        /// their actual impact on ΔS in the next turn.
        /// </remarks>
        private const double CapturePerStoneBonus = 2.0;

        // ============================================================
        //  Capture-potential sub-weights (used inside W2's computation)
        // ============================================================

        /// <summary>
        /// Weight applied when an empty pit on our side has stones across,
        /// but we cannot reach it this turn.
        /// </summary>
        /// <remarks>
        /// 0.5 = "half-confidence" in the threat. The opportunity exists
        /// in principle, but the opponent has a turn before we can act and
        /// can defuse it (by emptying the opposite pit, or filling our
        /// empty target). Discounting by 50% reflects roughly the chance
        /// the configuration survives one opponent move.
        /// </remarks>
        private const double PassiveCaptureWeight = 0.5;

        /// <summary>
        /// Weight applied when we can both create AND execute the capture
        /// in the current turn (some pit on our side can sow exactly into
        /// the empty target).
        /// </summary>
        /// <remarks>
        /// 1.0 = full weight. The threat is immediately actionable, so it
        /// is worth as much as the equivalent realized score, modulo W2's
        /// outer discount. Pairing 0.5 (passive) with 1.0 (active) gives
        /// the agent a clean "executable threats are twice as valuable as
        /// future ones" rule.
        /// </remarks>
        private const double ActiveCaptureWeight = 1.0;

        // ============================================================
        //  Dynamic W3 thresholds — adapt hoarding behavior to the
        //  current score gap so the agent shifts strategy when the
        //  game's trajectory clearly favors one side.
        // ============================================================

        /// <summary>
        /// Score-difference magnitude (in stones) that qualifies as "big lead" or "big deficit".
        /// </summary>
        /// <remarks>
        /// 8 = one third of the 24 stones each player starts with on the
        /// board. At this gap the trailing player is no longer in a
        /// statistically-balanced game and must shift to aggressive,
        /// opportunity-creating play; the leading player should rush to
        /// finish (since extending the game gives the opponent more chances
        /// to capture). Gaps below 8 stay in "balanced" territory where
        /// the default W3 applies.
        /// </remarks>
        private const int LargeScoreGap = 8;

        /// <summary>
        /// Multiplier applied to W3 when the agent is trailing by at least <see cref="LargeScoreGap"/>.
        /// </summary>
        /// <remarks>
        /// 1.5 boosts board-control weight by 50% when behind, which makes
        /// the agent value keeping stones on its side (preserving capture
        /// opportunities and chain-move material). Not 2.0 (would hoard
        /// excessively and stop scoring); not 1.2 (too subtle to change
        /// behavior visibly). Note: when LEADING by the same gap the
        /// dynamic formula instead returns –W3, flipping hoarding into
        /// rushing — see <see cref="ComputeDynamicW3"/>.
        /// </remarks>
        private const double TrailingBigW3Multiplier = 1.5;

        // ============================================================
        //  Starvation strategy — discourage handing free stones to a
        //  nearly-empty opponent. In Mancala an opponent with 0 stones
        //  on their side cannot move; if the player on turn cannot
        //  legally feed them either, the game ends and the leftover
        //  stones go to the side that has them. Feeding a starving
        //  opponent keeps the game alive and gives them ammunition.
        // ============================================================

        /// <summary>
        /// Opponent stone count at or below which starvation logic kicks in.
        /// </summary>
        /// <remarks>
        /// 3 because three is the smallest count that can still produce a
        /// "reaching" move — a 3-stone pit can sow 3 cells forward and
        /// potentially threaten capture. At 0–3 stones the opponent is
        /// effectively stuck or nearly so, and any move we make that
        /// transfers stones across the board re-opens their options.
        /// </remarks>
        private const int StarvationOpponentStoneThreshold = 3;

        /// <summary>
        /// Penalty (negative score) per stone we send to the opponent's row
        /// while they are starving.
        /// </summary>
        /// <remarks>
        /// 3.0 = three times the value of one banked stone. The penalty has
        /// to dominate W1 in the local decision, otherwise the agent would
        /// happily play a +2-score move that also feeds 2 stones to a
        /// starving opponent. With a per-stone penalty of 3.0, feeding
        /// even 1 stone costs more than an average tactical gain — which
        /// matches the strategic intuition that prolonging the game
        /// against a beaten opponent is almost always wrong.
        /// </remarks>
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

        // The bonus is awarded ONCE per opportunity, not once per source pit
        // that can reach it: only one move is played per turn, so multiple
        // reaching pits are mutually-exclusive options rather than additive
        // value. (Most opponent counter-moves — emptying the opposite pit, or
        // filling our empty target — defuse all paths simultaneously, so the
        // marginal value of a redundant path is small and certainly not linear.)
        private double ActiveCapturePotential(GameEngine engine, Player perspective,
                                              int targetPit, int oppositeStones) =>
            Enumerable.Range(0, GameEngine.PitsPerPlayer)
                .Select(j => engine.GetPitIndex(perspective, j))
                .Where(src => src != targetPit && engine.GetPitCount(src) > 0)
                .Any(src => CanSowExactlyInto(engine, perspective, src, targetPit))
                    ? oppositeStones * ActiveCaptureWeight
                    : 0.0;

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
