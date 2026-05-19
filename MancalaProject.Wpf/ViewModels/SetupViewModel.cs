namespace MancalaProject.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel backing the startup dialog. Lets the user pick a difficulty
    /// for the computer agent and choose which side takes the first move.
    /// </summary>
    /// <remarks>
    /// Each radio button in the View is bound two-way to a <c>bool</c> property here.
    /// Mutual exclusivity within a group is enforced by WPF's <c>RadioButton.GroupName</c>,
    /// so when the user selects one option the others auto-clear without extra code.
    /// </remarks>
    public class SetupViewModel : ObservableObject
    {
        private bool _isEasy;
        private bool _isMedium;
        private bool _isHard;
        private bool _playerStarts;

        /// <summary>
        /// Creates the setup ViewModel, pre-selecting the given difficulty and
        /// starting-player choice. With no arguments it defaults to Hard, with
        /// the human player moving first.
        /// </summary>
        /// <param name="difficulty">Difficulty to pre-select.</param>
        /// <param name="computerStarts">If <c>true</c>, "computer plays first" is pre-selected.</param>
        public SetupViewModel(Difficulty difficulty = Difficulty.Hard, bool computerStarts = false)
        {
            _isEasy   = difficulty == Difficulty.Easy;
            _isMedium = difficulty == Difficulty.Medium;
            _isHard   = difficulty == Difficulty.Hard;
            _playerStarts = !computerStarts;
        }

        // ============================================================
        //  Difficulty — three mutually-exclusive bools.
        // ============================================================

        /// <summary>The user has selected Easy difficulty (2-ply lookahead — weakest play).</summary>
        public bool IsEasy
        {
            get => _isEasy;
            set => SetField(ref _isEasy, value);
        }

        /// <summary>The user has selected Medium difficulty (3-ply lookahead).</summary>
        public bool IsMedium
        {
            get => _isMedium;
            set => SetField(ref _isMedium, value);
        }

        /// <summary>The user has selected Hard difficulty (4-ply search plus a capture-safety guard — strongest play).</summary>
        public bool IsHard
        {
            get => _isHard;
            set => SetField(ref _isHard, value);
        }

        // ============================================================
        //  Who moves first — two mutually-exclusive bools, exposed as the
        //  inverse of one another so each RadioButton binds to its own source.
        // ============================================================

        /// <summary>The human player takes the first move. The default.</summary>
        public bool PlayerStarts
        {
            get => _playerStarts;
            set
            {
                if (SetField(ref _playerStarts, value))
                    OnPropertyChanged(nameof(ComputerStarts));
            }
        }

        /// <summary>
        /// The computer agent takes the first move. Implemented as the inverse of
        /// <see cref="PlayerStarts"/> — flipping one always flips the other.
        /// </summary>
        public bool ComputerStarts
        {
            get => !_playerStarts;
            set
            {
                // A RadioButton fires this setter both when checked (true) and
                // when unchecked (false). Only the "becoming true" edge needs
                // handling; the other path follows from PlayerStarts becoming true.
                if (value) PlayerStarts = false;
            }
        }

        // ============================================================
        //  Output — read by the View after the dialog closes
        // ============================================================

        /// <summary>
        /// The <see cref="Difficulty"/> the user selected. Falls back to
        /// <see cref="Difficulty.Hard"/> if somehow none of the radios are
        /// checked (should not happen in practice).
        /// </summary>
        public Difficulty SelectedDifficulty =>
            _isEasy   ? Difficulty.Easy   :
            _isMedium ? Difficulty.Medium :
                        Difficulty.Hard;
    }
}
