namespace MancalaProject.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel backing the startup dialog. Lets the user pick a game mode
    /// (PvP / PvE) and, when applicable, a difficulty for the computer agent.
    /// </summary>
    /// <remarks>
    /// Each radio button in the View is bound two-way to a <c>bool</c> property here.
    /// Mutual exclusivity within a group is enforced by WPF's <c>RadioButton.GroupName</c>,
    /// so when the user selects one option the others auto-clear without extra code.
    /// </remarks>
    public class SetupViewModel : ObservableObject
    {
        private bool _isVsComputer = true;
        private bool _isEasy;
        private bool _isMedium;
        private bool _isHard = true;

        // ============================================================
        //  Mode (PvP / PvE) — exposed as two mutually-exclusive bools
        //  so each RadioButton can bind to its own IsChecked source.
        // ============================================================

        /// <summary>The user has chosen Player vs Computer.</summary>
        public bool IsVsComputer
        {
            get => _isVsComputer;
            set
            {
                if (!SetField(ref _isVsComputer, value)) return;
                OnPropertyChanged(nameof(IsVsPlayer));
                OnPropertyChanged(nameof(IsDifficultyEnabled));
            }
        }

        /// <summary>
        /// The user has chosen Player vs Player. Implemented as the inverse of
        /// <see cref="IsVsComputer"/> — flipping one always flips the other.
        /// </summary>
        public bool IsVsPlayer
        {
            get => !_isVsComputer;
            set
            {
                // RadioButton fires this setter both when checked (true) and when
                // unchecked (false). We only need to act on the "becoming true"
                // edge; the "becoming false" path is handled implicitly by
                // IsVsComputer becoming true.
                if (value) IsVsComputer = false;
            }
        }

        /// <summary>
        /// Whether the difficulty group should be active. Greys out the difficulty
        /// radios in PvP mode without removing them, so the user always sees what
        /// would be available.
        /// </summary>
        public bool IsDifficultyEnabled => _isVsComputer;

        // ============================================================
        //  Difficulty — three mutually-exclusive bools.
        //  Defaults to Hard (matches the Console app's behavior).
        // ============================================================

        /// <summary>The user has selected Easy difficulty (large noise, no opponent lookahead, no endgame rollout).</summary>
        public bool IsEasy
        {
            get => _isEasy;
            set => SetField(ref _isEasy, value);
        }

        /// <summary>The user has selected Medium difficulty (small noise, opponent lookahead enabled, no endgame rollout).</summary>
        public bool IsMedium
        {
            get => _isMedium;
            set => SetField(ref _isMedium, value);
        }

        /// <summary>The user has selected Hard difficulty (no noise, full opponent lookahead, endgame rollout enabled). Default.</summary>
        public bool IsHard
        {
            get => _isHard;
            set => SetField(ref _isHard, value);
        }

        // ============================================================
        //  Output — read by App.xaml.cs after the dialog closes
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
