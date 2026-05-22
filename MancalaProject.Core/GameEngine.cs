using System;
using System.Collections.Generic;
using System.Linq;

namespace MancalaProject
{
    /// <summary>
    /// Identifies one of the two players in a Mancala game.
    /// </summary>
    public enum Player
    {
        /// <summary>The bottom-row player whose store is at index 6.</summary>
        Player1 = 0,
        /// <summary>The top-row player whose store is at index 13.</summary>
        Player2 = 1
    }

    /// <summary>
    /// Describes the outcome of a single move applied to the board.
    /// </summary>
    /// <param name="LastPitIndex">Board index of the pit where the last sown stone landed.</param>
    /// <param name="ExtraTurn"><c>true</c> if the move ended in the moving player's own store, granting another turn.</param>
    /// <param name="CaptureOccurred"><c>true</c> if a capture was triggered by the move.</param>
    /// <param name="CapturedFromPit">Board index of the opposing pit whose stones were captured, or -1 if no capture occurred.</param>
    /// <param name="CapturedStoneCount">Number of stones taken from the opposing pit (excluding the landing stone), or 0 if no capture occurred.</param>
    public record MoveResult(
        int LastPitIndex,
        bool ExtraTurn,
        bool CaptureOccurred,
        int CapturedFromPit,
        int CapturedStoneCount
    );

    /// <summary>
    /// Encapsulates the rules and state of a Mancala (Kalah) game: board representation,
    /// move validation, sowing, captures, extra turns, and end-of-game finalization.
    /// The engine is independent of any UI and can be cloned for use in agent simulations.
    /// </summary>
    public class GameEngine
    {
        /// <summary>Total number of slots on the board (12 pits + 2 stores).</summary>
        public const int BoardSize = 14;
        /// <summary>Board index of Player 1's store.</summary>
        public const int Player1Store = 6;
        /// <summary>Board index of Player 2's store.</summary>
        public const int Player2Store = 13;
        /// <summary>Number of playable pits per player (excluding the store).</summary>
        public const int PitsPerPlayer = 6;
        /// <summary>Initial number of stones placed in each playable pit at the start of a game.</summary>
        public const int InitialStones = 4;

        // Sum of indices of two facing pits: pit i and pit (OppositePitIndexSum - i)
        // are across the board from each other. Equals Player1Store + PitsPerPlayer.
        private const int OppositePitIndexSum = Player1Store + PitsPerPlayer;

        /// <summary>
        /// Read-only view of the board. Indices 0-5 are Player 1's pits,
        /// 6 is Player 1's store, 7-12 are Player 2's pits, and 13 is Player 2's store.
        /// </summary>
        public IReadOnlyList<int> Board => _board;
        private readonly int[] _board;

        /// <summary>Gets the player whose turn it currently is.</summary>
        public Player CurrentPlayer { get; private set; }

        /// <summary>
        /// Indicates whether <see cref="FinalizeBoard"/> has been invoked.
        /// Once finalized, the board no longer changes.
        /// </summary>
        public bool IsFinalized => _isFinalized;
        private bool _isFinalized = false;

        /// <summary>Raised whenever the board state changes (after a move or finalization).</summary>
        public event Action? BoardChanged;

        /// <summary>Raised after a move completes, carrying its <see cref="MoveResult"/>.</summary>
        public event Action<MoveResult>? MoveMade;

        /// <summary>Raised once the game has ended and the board has been finalized.</summary>
        public event Action? GameEnded;

        /// <summary>
        /// Initializes a new game with the standard starting position
        /// (every pit holds <see cref="InitialStones"/>).
        /// </summary>
        /// <param name="startingPlayer">The player to take the first move. Defaults to <see cref="Player.Player1"/>.</param>
        public GameEngine(Player startingPlayer = Player.Player1)
        {
            _board = new int[BoardSize];
            InitializeBoard();
            CurrentPlayer = startingPlayer;
        }

        private GameEngine(GameEngine source)
        {
            _board = (int[])source._board.Clone();
            CurrentPlayer = source.CurrentPlayer;
            _isFinalized = source._isFinalized;
        }

        /// <summary>
        /// Test-support constructor: builds an engine at an arbitrary position.
        /// Exposed as <c>internal</c> so the unit-test project can set up
        /// specific board configurations (captures, endgames, finished games)
        /// directly, instead of replaying long move sequences to reach them.
        /// It is invisible to the WPF and Console front-ends.
        /// </summary>
        /// <param name="board">The 14 cell counts to copy onto the board (indices 0-13).</param>
        /// <param name="currentPlayer">The player whose turn it is.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="board"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="board"/> does not have exactly <see cref="BoardSize"/> entries.</exception>
        internal GameEngine(IReadOnlyList<int> board, Player currentPlayer)
        {
            if (board is null)
                throw new ArgumentNullException(nameof(board));
            if (board.Count != BoardSize)
                throw new ArgumentException($"Board must have exactly {BoardSize} cells.", nameof(board));

            _board = new int[BoardSize];
            for (int i = 0; i < BoardSize; i++)
                _board[i] = board[i];

            CurrentPlayer = currentPlayer;
        }

        private void InitializeBoard()
        {
            for (int i = 0; i < BoardSize; i++)
            {
                _board[i] = (i == Player1Store || i == Player2Store) ? 0 : InitialStones;
            }
        }

        /// <summary>
        /// Returns an independent copy of this engine with the same board and turn state.
        /// Events are NOT copied, so simulations performed on the clone do not fire
        /// notifications on the original engine.
        /// </summary>
        /// <returns>A new <see cref="GameEngine"/> safe to mutate without affecting the original.</returns>
        public GameEngine Clone() => new GameEngine(this);

        // --- Helper Accessors ---

        /// <summary>Returns the board index of the given player's store.</summary>
        public int GetStoreIndex(Player player) =>
            player == Player.Player1 ? Player1Store : Player2Store;

        /// <summary>
        /// Translates a player-relative pit number (0..5) into its absolute board index.
        /// </summary>
        /// <param name="player">The player whose side the pit belongs to.</param>
        /// <param name="pitNumber">Zero-based pit number on that player's side (0 to <see cref="PitsPerPlayer"/>-1).</param>
        /// <returns>The corresponding absolute board index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pitNumber"/> is outside the valid range.</exception>
        public int GetPitIndex(Player player, int pitNumber)
        {
            if (pitNumber < 0 || pitNumber >= PitsPerPlayer)
                throw new ArgumentOutOfRangeException(nameof(pitNumber), "Pit number must be 0-5.");
            return player == Player.Player1 ? pitNumber : Player1Store + 1 + pitNumber;
        }

        /// <summary>Returns the number of stones currently in the pit at the given board index.</summary>
        public int GetPitCount(int boardIndex) => _board[boardIndex];

        /// <summary>Returns the number of stones currently in the given player's store.</summary>
        public int GetScore(Player player) => _board[GetStoreIndex(player)];

        /// <summary>
        /// Returns the board index of the pit directly across from <paramref name="pitIndex"/>.
        /// Used for capture logic.
        /// </summary>
        public int GetOppositePitIndex(int pitIndex) => OppositePitIndexSum - pitIndex;

        // --- Validation ---

        /// <summary>
        /// Checks whether the current player may legally play from the given pit:
        /// the index must be within range, must not be a store, the pit must be
        /// non-empty, and it must lie on the current player's side.
        /// </summary>
        /// <param name="pitIndex">Absolute board index of the pit to test.</param>
        /// <returns><c>true</c> if the move is legal for the current player.</returns>
        public bool IsValidMove(int pitIndex) =>
            IsBoardIndexInRange(pitIndex)
            && !IsStoreIndex(pitIndex)
            && _board[pitIndex] > 0
            && IsOnCurrentPlayerSide(pitIndex);

        /// <summary>
        /// Returns the list of absolute board indices the current player may legally play from.
        /// </summary>
        public List<int> GetValidMoves() =>
            Enumerable.Range(CurrentSideStart(), PitsPerPlayer)
                .Where(i => _board[i] > 0)
                .ToList();

        private int CurrentSideStart() =>
            CurrentPlayer == Player.Player1 ? 0 : Player1Store + 1;

        private bool IsBoardIndexInRange(int boardIndex) =>
            boardIndex >= 0 && boardIndex < BoardSize;

        private static bool IsStoreIndex(int boardIndex) =>
            boardIndex == Player1Store || boardIndex == Player2Store;

        private bool IsOnCurrentPlayerSide(int boardIndex) =>
            CurrentPlayer == Player.Player1
                ? boardIndex < Player1Store
                : boardIndex > Player1Store && boardIndex < Player2Store;

        private bool IsOpponentStore(int boardIndex) =>
            CurrentPlayer == Player.Player1
                ? boardIndex == Player2Store
                : boardIndex == Player1Store;

        private bool LandedInOwnStore(int boardIndex) =>
            CurrentPlayer == Player.Player1
                ? boardIndex == Player1Store
                : boardIndex == Player2Store;

        // --- Core Game Logic ---

        /// <summary>
        /// Plays the given move for the current player: empties the source pit, sows the
        /// stones counter-clockwise (skipping the opponent's store), resolves any capture,
        /// switches turn unless an extra turn was earned, and notifies subscribers.
        /// </summary>
        /// <param name="pitIndex">Absolute board index of the pit to play from.</param>
        /// <returns>A <see cref="MoveResult"/> describing the outcome of the move.</returns>
        /// <exception cref="ArgumentException">Thrown when the move is illegal for the current player.</exception>
        public MoveResult ApplyMove(int pitIndex)
        {
            ValidateMove(pitIndex);

            int landingIndex = SowStonesFrom(pitIndex);
            bool extraTurn = LandedInOwnStore(landingIndex);
            (int capturedFrom, int capturedCount) = TryCapture(landingIndex, extraTurn);

            MoveResult result = new MoveResult(
                landingIndex,
                extraTurn,
                capturedCount > 0,
                capturedFrom,
                capturedCount);

            AdvanceTurn(extraTurn);
            NotifyMoveCompleted(result);
            return result;
        }

        private void ValidateMove(int pitIndex)
        {
            if (!IsValidMove(pitIndex))
                throw new ArgumentException($"Illegal move: pit {pitIndex} for {CurrentPlayer}.");
        }

        private int SowStonesFrom(int sourcePit)
        {
            int stones = _board[sourcePit];
            _board[sourcePit] = 0;
            int currentIndex = sourcePit;

            while (stones > 0)
            {
                currentIndex = NextSowingIndex(currentIndex);
                _board[currentIndex]++;
                stones--;
            }
            return currentIndex;
        }

        // Returns the next pit index to sow into, automatically stepping past
        // the opponent's store so the caller never deposits a stone there.
        private int NextSowingIndex(int fromIndex)
        {
            int next = (fromIndex + 1) % BoardSize;
            return IsOpponentStore(next) ? (next + 1) % BoardSize : next;
        }

        private (int FromPit, int StoneCount) TryCapture(int landingIndex, bool wasExtraTurn) =>
            CanCapture(landingIndex, wasExtraTurn)
                ? PerformCapture(landingIndex)
                : (-1, 0);

        private bool CanCapture(int landingIndex, bool wasExtraTurn) =>
            !wasExtraTurn
            && !IsStoreIndex(landingIndex)
            && IsOnCurrentPlayerSide(landingIndex)
            && _board[landingIndex] == 1
            && _board[GetOppositePitIndex(landingIndex)] > 0;

        private (int FromPit, int StoneCount) PerformCapture(int landingIndex)
        {
            int oppositeIndex = GetOppositePitIndex(landingIndex);
            int capturedStones = _board[oppositeIndex];

            _board[landingIndex] = 0;
            _board[oppositeIndex] = 0;
            _board[GetStoreIndex(CurrentPlayer)] += capturedStones + 1;

            return (oppositeIndex, capturedStones);
        }

        private void AdvanceTurn(bool extraTurn)
        {
            if (!extraTurn)
                CurrentPlayer = (Player)(1 - (int)CurrentPlayer);
        }

        private void NotifyMoveCompleted(MoveResult result)
        {
            BoardChanged?.Invoke();
            MoveMade?.Invoke(result);
        }

        /// <summary>
        /// Determines whether the game has ended: a side has finished its turn with
        /// every one of its playable pits empty.
        /// </summary>
        public bool IsGameOver() =>
            PitsAreEmpty(0, Player1Store) || PitsAreEmpty(Player1Store + 1, Player2Store);

        private bool PitsAreEmpty(int firstInclusive, int lastExclusive) =>
            Enumerable.Range(firstInclusive, lastExclusive - firstInclusive)
                .All(i => _board[i] == 0);

        /// <summary>
        /// Sweeps every remaining stone on the board into its owner's store and
        /// marks the engine as finalized. Has no effect if already finalized.
        /// Fires <see cref="BoardChanged"/> and <see cref="GameEnded"/>.
        /// </summary>
        public void FinalizeBoard()
        {
            if (_isFinalized) return;
            _isFinalized = true;

            for (int i = 0; i < Player1Store; i++)
            {
                _board[Player1Store] += _board[i];
                _board[i] = 0;
            }
            for (int i = Player1Store + 1; i < Player2Store; i++)
            {
                _board[Player2Store] += _board[i];
                _board[i] = 0;
            }

            BoardChanged?.Invoke();
            GameEnded?.Invoke();
        }

        /// <summary>
        /// Returns the winner of the finalized game.
        /// </summary>
        /// <returns>
        /// 0 if Player 1 wins, 1 if Player 2 wins, -1 if the game is a tie.
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown if the board has not been finalized.</exception>
        public int GetWinner()
        {
            if (!_isFinalized)
                throw new InvalidOperationException("GetWinner() called before FinalizeBoard().");

            return _board[Player1Store].CompareTo(_board[Player2Store]) switch
            {
                > 0 => 0,
                < 0 => 1,
                _   => -1
            };
        }
    }
}
