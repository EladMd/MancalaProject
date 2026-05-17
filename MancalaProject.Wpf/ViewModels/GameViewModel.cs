using System;
using System.Threading;
using System.Threading.Tasks;

namespace MancalaProject.Wpf.ViewModels
{
    /// <summary>
    /// The ViewModel that drives the main Mancala window. Wraps a <see cref="GameEngine"/>
    /// (the Model) and translates its state into properties and commands the View can bind to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Architectural rules:
    /// <list type="bullet">
    /// <item>The View (XAML) never references <see cref="GameEngine"/> or <see cref="GreedyAgent"/> directly.</item>
    /// <item>The Model never references the ViewModel — it raises plain events; the ViewModel subscribes.</item>
    /// <item>All "wait" delays for the computer's turn live here (in <see cref="MaybeRunComputerTurnAsync"/>),
    ///       not in the agent. The agent stays synchronous and pure.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Threading: the agent's <c>CalculateMove</c> may take noticeable time on Hard difficulty
    /// (full endgame rollout). It is deliberately offloaded to the thread pool via <c>Task.Run</c>
    /// so the UI thread stays responsive while the computer "thinks".
    /// </para>
    /// </remarks>
    public class GameViewModel : ObservableObject
    {
        /// <summary>
        /// Pause (in milliseconds) between announcing "Computer is thinking..." and actually
        /// playing the move, so the human user can perceive the action.
        /// </summary>
        private const int ComputerMoveDelayMs = 2500;

        /// <summary>
        /// Brief pause used to display a move's outcome (capture notice, extra-turn
        /// announcement, "X's turn" text) before the next status message overwrites it.
        /// Used in two places: after the human's move (so the user reads it before the
        /// computer starts thinking) and between consecutive computer extra turns.
        /// </summary>
        private const int MoveResultDisplayMs = 1100;

        // ============================================================
        //  Configuration (immutable for the lifetime of this ViewModel)
        // ============================================================
        private readonly bool _vsComputer;
        private readonly Difficulty _difficulty;

        // ============================================================
        //  Mutable state
        // ============================================================
        private GameEngine _engine = null!;   // initialized by StartNewGame in the constructor
        private GreedyAgent? _agent;
        private string _statusMessage = "";
        private bool _isComputerThinking;

        /// <summary>
        /// Cancels any in-flight computer turn. Replaced (after cancellation) at the
        /// start of every game so each game has its own independent token. Used when
        /// the user clicks "New Game" while the computer is still thinking — without
        /// it, the running task would resume after the await and try to apply a move
        /// to the already-replaced engine.
        /// </summary>
        private CancellationTokenSource? _gameCts;

        // ============================================================
        //  Construction
        // ============================================================

        /// <summary>
        /// Creates a new game ViewModel. Defaults to Player-vs-Player so that the View
        /// can be tested in isolation; in normal startup the setup screen passes the
        /// user's real choice for <paramref name="vsComputer"/> and <paramref name="difficulty"/>.
        /// </summary>
        /// <param name="vsComputer">If <c>true</c>, Player 2 is controlled by a <see cref="GreedyAgent"/>.</param>
        /// <param name="difficulty">Strength of the computer agent. Ignored when <paramref name="vsComputer"/> is <c>false</c>.</param>
        public GameViewModel(bool vsComputer = false, Difficulty difficulty = Difficulty.Hard)
        {
            _vsComputer = vsComputer;
            _difficulty = difficulty;

            PlayPitCommand = new RelayCommand<int>(OnPlayPit, CanPlayPit);
            NewGameCommand = new RelayCommand(StartNewGame);

            StartNewGame();
        }

        // ============================================================
        //  Commands (bound from XAML buttons)
        // ============================================================

        /// <summary>
        /// Plays a stone from the pit whose absolute board index is supplied as <c>CommandParameter</c>.
        /// Disabled automatically when the move is not legal, the game is over, or the computer is thinking.
        /// </summary>
        public RelayCommand<int> PlayPitCommand { get; }

        /// <summary>Resets the engine and starts a fresh game using the same mode and difficulty.</summary>
        public RelayCommand NewGameCommand { get; }

        /// <summary>
        /// Raised once when the engine reaches a terminal state. The argument is the
        /// human-readable game-over message. The View subscribes to this to show a
        /// modal dialog without the ViewModel knowing what a MessageBox is.
        /// </summary>
        public event Action<string>? GameOver;

        // ============================================================
        //  Board state — exposed as 14 individual properties so every
        //  pit and store can be bound by name from XAML. Indices match
        //  the engine's board layout: 0–5 = Player 1's pits, 6 = Player 1's
        //  store, 7–12 = Player 2's pits, 13 = Player 2's store. Each
        //  property is a thin wrapper around the underlying engine board;
        //  all 14 are re-broadcast together whenever the engine fires
        //  BoardChanged (see RaiseAllBoardProperties).
        // ============================================================

        /// <summary>Stones currently in Player 1's pit at board index 0 (left-most on the bottom row).</summary>
        public int Pit0   => _engine.Board[0];
        /// <summary>Stones currently in Player 1's pit at board index 1.</summary>
        public int Pit1   => _engine.Board[1];
        /// <summary>Stones currently in Player 1's pit at board index 2.</summary>
        public int Pit2   => _engine.Board[2];
        /// <summary>Stones currently in Player 1's pit at board index 3.</summary>
        public int Pit3   => _engine.Board[3];
        /// <summary>Stones currently in Player 1's pit at board index 4.</summary>
        public int Pit4   => _engine.Board[4];
        /// <summary>Stones currently in Player 1's pit at board index 5 (right-most on the bottom row).</summary>
        public int Pit5   => _engine.Board[5];
        /// <summary>Player 1's store (cumulative score). Board index 6.</summary>
        public int Store1 => _engine.Board[GameEngine.Player1Store];
        /// <summary>Stones currently in Player 2's pit at board index 7 (right-most on the top row, sown into first by Player 1).</summary>
        public int Pit7   => _engine.Board[7];
        /// <summary>Stones currently in Player 2's pit at board index 8.</summary>
        public int Pit8   => _engine.Board[8];
        /// <summary>Stones currently in Player 2's pit at board index 9.</summary>
        public int Pit9   => _engine.Board[9];
        /// <summary>Stones currently in Player 2's pit at board index 10.</summary>
        public int Pit10  => _engine.Board[10];
        /// <summary>Stones currently in Player 2's pit at board index 11.</summary>
        public int Pit11  => _engine.Board[11];
        /// <summary>Stones currently in Player 2's pit at board index 12 (left-most on the top row).</summary>
        public int Pit12  => _engine.Board[12];
        /// <summary>Player 2's store (cumulative score). Board index 13.</summary>
        public int Store2 => _engine.Board[GameEngine.Player2Store];

        // ============================================================
        //  Status flags — for status bar text and active-row highlighting
        // ============================================================

        /// <summary>Human-readable description of what just happened or whose turn it is.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        /// <summary>
        /// <c>true</c> while the agent is calculating or its "thinking" delay is active.
        /// Used by the View to disable input and (optionally) show a thinking indicator.
        /// </summary>
        public bool IsComputerThinking
        {
            get => _isComputerThinking;
            private set
            {
                if (!SetField(ref _isComputerThinking, value)) return;

                // IsHumanTurn is derived from this flag, so it must be re-broadcast,
                // and the pit-button command must re-check its CanExecute since the
                // change is coming from a background flow rather than a UI event.
                OnPropertyChanged(nameof(IsHumanTurn));
                PlayPitCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The game has reached a terminal state.</summary>
        public bool IsGameOver => _engine.IsGameOver();

        /// <summary>Player 1's row should be highlighted as the active player.</summary>
        public bool IsPlayer1Turn => !IsGameOver && _engine.CurrentPlayer == Player.Player1;

        /// <summary>Player 2's row should be highlighted as the active player.</summary>
        public bool IsPlayer2Turn => !IsGameOver && _engine.CurrentPlayer == Player.Player2;

        /// <summary>
        /// <c>true</c> iff a human is allowed to click a pit right now — i.e. game is live,
        /// computer is not thinking, and the current player is not the computer.
        /// </summary>
        public bool IsHumanTurn =>
            !IsGameOver
            && !IsComputerThinking
            && !(_vsComputer && _engine.CurrentPlayer == Player.Player2);

        // ============================================================
        //  Game flow
        // ============================================================

        private void StartNewGame()
        {
            // Cancel any computer turn from the previous game that may still be in
            // its "thinking" delay. Without this, the old task would resume after
            // its await, try to apply a move on the (now replaced) engine, and
            // either crash or corrupt the new game's state. After cancelling, we
            // create a fresh CTS so this new game has its own independent token.
            _gameCts?.Cancel();
            _gameCts = new CancellationTokenSource();

            // Detach from the previous engine, if any, so its events don't leak into the new game.
            if (_engine != null)
                _engine.BoardChanged -= OnEngineBoardChanged;

            _engine = new GameEngine();
            _engine.BoardChanged += OnEngineBoardChanged;
            _agent = _vsComputer ? new GreedyAgent(Player.Player2, _difficulty) : null;

            IsComputerThinking = false;
            StatusMessage = "Player 1's turn";
            RaiseAllBoardProperties();

            // Forward-compat: if a future configuration gives the agent the opening move,
            // this fire-and-forget kicks the computer into action immediately.
            _ = MaybeRunComputerTurnAsync();
        }

        private bool CanPlayPit(int pitIndex) =>
            !IsGameOver
            && IsHumanTurn
            && _engine.IsValidMove(pitIndex);

        private void OnPlayPit(int pitIndex)
        {
            // CanExecute already gates this, but we re-check defensively in case
            // the binding fires from an unexpected state.
            if (!CanPlayPit(pitIndex)) return;

            MoveResult result = _engine.ApplyMove(pitIndex);
            UpdateStatusAfterMove(result);

            // Fire-and-forget: trigger the computer's reply (if any) without
            // blocking this command callback.
            _ = MaybeRunComputerTurnAsync();
        }

        /// <summary>
        /// Plays the agent's move (and any chained extra-turn moves) without
        /// blocking the UI thread. Each move is preceded by a perceptible
        /// "thinking" delay so the user sees what the computer is doing.
        /// </summary>
        private async Task MaybeRunComputerTurnAsync()
        {
            if (_agent == null) return;

            // Bail out immediately if it's not actually the computer's turn — e.g.
            // the human got an extra turn and stays on, or the move ended the game.
            // Doing this BEFORE the first delay keeps the new-game / human-extra-turn
            // paths instant.
            if (_engine.IsGameOver() || _engine.CurrentPlayer != Player.Player2) return;

            // Tie this entire run to the current game's cancellation token. If
            // StartNewGame fires mid-flight it will cancel this token, every
            // await below will throw OperationCanceledException, and the catch
            // block silently exits without touching the new game's state.
            CancellationToken ct = _gameCts!.Token;

            try
            {
                // Hold the previous status message (the result of the HUMAN's move:
                // capture notice or "Computer's turn" announcement) for a beat before
                // we overwrite it with the thinking message. Without this delay, the
                // human's move result is set and overwritten on the same UI dispatch
                // cycle and the user never sees it.
                await Task.Delay(MoveResultDisplayMs, ct);

                // Tracks whether the upcoming "thinking" phase is the second (or later)
                // in a chain of extra turns. Used to vary the status message so the user
                // understands why the computer is taking another 2.5 seconds.
                bool isExtraTurnFollowup = false;

                while (!_engine.IsGameOver() && _engine.CurrentPlayer == Player.Player2)
                {
                    IsComputerThinking = true;
                    StatusMessage = isExtraTurnFollowup
                        ? "Computer got an extra turn — thinking again..."
                        : "Computer is thinking...";

                    await Task.Delay(ComputerMoveDelayMs, ct);

                    // Run the search on the thread pool — Hard difficulty's endgame rollout
                    // can take a few hundred ms and we don't want to freeze the window.
                    int move = await Task.Run(() => _agent!.CalculateMove(_engine), ct);

                    // Re-check cancellation: Task.Run may have completed normally just
                    // before StartNewGame fired. Without this throw, ApplyMove would
                    // run on the (already replaced) new engine.
                    ct.ThrowIfCancellationRequested();

                    MoveResult result = _engine.ApplyMove(move);
                    UpdateStatusAfterMove(result);

                    isExtraTurnFollowup = result.ExtraTurn;

                    // If the computer keeps the turn, briefly hold the result message
                    // ("captured X stones!" or "extra turn!") so the user can read it
                    // before the next "thinking again..." message overwrites it.
                    if (isExtraTurnFollowup && !_engine.IsGameOver())
                        await Task.Delay(MoveResultDisplayMs, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal exit path when StartNewGame interrupts an in-flight computer
                // turn. Nothing to do — the new game has already reset all state.
                return;
            }

            IsComputerThinking = false;
        }

        private void UpdateStatusAfterMove(MoveResult result)
        {
            if (_engine.IsGameOver())
            {
                // Sweep any stones still in the loser's row into their store before
                // declaring a winner. The engine refuses to compute a winner until
                // this finalization step has run; it also fires BoardChanged so the
                // UI repaints with the true final scores.
                _engine.FinalizeBoard();
                string message = BuildGameOverMessage();
                StatusMessage = message;

                // Notify the View so it can show a modal dialog. Raising an event
                // (rather than calling MessageBox here) keeps the ViewModel free
                // of WPF dependencies — this class would still compile if the UI
                // layer were swapped out for, say, a console front-end.
                GameOver?.Invoke(message);
                return;
            }

            // Capture and extra-turn are MUTUALLY EXCLUSIVE in Mancala:
            //   - Extra turn fires only when the last stone lands in your store.
            //   - Capture fires only when the last stone lands in an empty pit
            //     on your side (a regular pit, not a store).
            // The same final-resting-pit can't be both, so exactly one (or neither)
            // of these flags is true on any given move.

            string actor = ActorName(GetPlayerWhoJustMoved(result));

            if (result.ExtraTurn)
            {
                StatusMessage = $"{actor} gets an extra turn!";
            }
            else if (result.CaptureOccurred)
            {
                StatusMessage = $"{actor} captured {result.CapturedStoneCount} stones! {NextTurnText()}";
            }
            else
            {
                StatusMessage = NextTurnText();
            }
        }

        /// <summary>
        /// Returns the player who just made the move, by inverting the engine's
        /// post-move <see cref="GameEngine.CurrentPlayer"/> when the turn passed,
        /// or returning it as-is when an extra turn kept the same player on.
        /// </summary>
        private Player GetPlayerWhoJustMoved(MoveResult result) =>
            result.ExtraTurn
                ? _engine.CurrentPlayer
                : (_engine.CurrentPlayer == Player.Player1 ? Player.Player2 : Player.Player1);

        /// <summary>
        /// User-facing name of a player. Player 2 is shown as "Computer" only when
        /// the agent is actually controlling that side.
        /// </summary>
        private string ActorName(Player p) =>
            p == Player.Player1
                ? "Player 1"
                : (_vsComputer ? "Computer" : "Player 2");

        /// <summary>"Player X's turn" / "Computer's turn", driven by the engine's current player.</summary>
        private string NextTurnText() =>
            _engine.CurrentPlayer == Player.Player1
                ? "Player 1's turn"
                : (_vsComputer ? "Computer's turn" : "Player 2's turn");

        private string BuildGameOverMessage()
        {
            int winner = _engine.GetWinner();
            return winner switch
            {
                0 => "Game over — Player 1 wins!",
                1 => _vsComputer ? "Game over — Computer wins!" : "Game over — Player 2 wins!",
                _ => "Game over — it's a tie."
            };
        }

        // ============================================================
        //  Engine event plumbing
        // ============================================================

        private void OnEngineBoardChanged() =>
            RaiseAllBoardProperties();

        /// <summary>
        /// Names of every property that depends on engine state. Listed once here so
        /// adding a new derived property is a one-line change.
        /// </summary>
        private static readonly string[] BoardDependentProperties =
        {
            nameof(Pit0),  nameof(Pit1),  nameof(Pit2),  nameof(Pit3),  nameof(Pit4),  nameof(Pit5),
            nameof(Store1),
            nameof(Pit7),  nameof(Pit8),  nameof(Pit9),  nameof(Pit10), nameof(Pit11), nameof(Pit12),
            nameof(Store2),
            nameof(IsGameOver),
            nameof(IsPlayer1Turn),
            nameof(IsPlayer2Turn),
            nameof(IsHumanTurn)
        };

        private void RaiseAllBoardProperties()
        {
            foreach (string name in BoardDependentProperties)
                OnPropertyChanged(name);

            // Force every command-bound button (currently the 12 pit buttons) to
            // re-evaluate CanExecute. WPF's CommandManager auto-queries after
            // input events, but state changes coming from background tasks
            // (the agent finishing its turn) may not produce one — so we
            // trigger a re-query explicitly here.
            PlayPitCommand.RaiseCanExecuteChanged();
        }
    }
}
