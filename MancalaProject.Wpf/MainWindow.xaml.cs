using System.Windows;
using MancalaProject.Wpf.ViewModels;
using MancalaProject.Wpf.Views;

namespace MancalaProject.Wpf
{
    /// <summary>
    /// Code-behind for the main game window.
    /// Deliberately minimal: instantiates a <see cref="GameViewModel"/> using the
    /// settings the user chose in the setup dialog, assigns it as the window's
    /// <see cref="FrameworkElement.DataContext"/>, bridges its
    /// <see cref="GameViewModel.GameOver"/> event to a native modal dialog, and
    /// re-opens the setup dialog when the "Setup" button is pressed.
    /// All actual logic — binding, commands, game flow — lives in the ViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Parameterless constructor kept for the XAML designer. Should not be called
        /// at runtime; <see cref="App.OnStartup"/> always uses the parameterized overload.
        /// </summary>
        public MainWindow() : this(Difficulty.Hard, computerStarts: false)
        {
        }

        /// <summary>
        /// Real runtime entry point. Creates the game with the difficulty and
        /// starting player chosen on the setup dialog and wires the game-over dialog.
        /// </summary>
        /// <param name="difficulty">Strength of the computer agent.</param>
        /// <param name="computerStarts">If <c>true</c>, the computer takes the opening move.</param>
        public MainWindow(Difficulty difficulty, bool computerStarts)
        {
            InitializeComponent();

            var vm = new GameViewModel(difficulty, computerStarts);

            // Show a modal dialog when the game ends. We intentionally subscribe in
            // the View (not in the ViewModel) so the ViewModel never references a
            // WPF type — keeping the MVVM separation clean.
            vm.GameOver += ShowGameOverDialog;

            DataContext = vm;
        }

        private void ShowGameOverDialog(string message)
        {
            MessageBox.Show(this, message, "Mancala", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Handles the header "Setup" button: re-opens the setup dialog so the
        /// player can change the difficulty and who moves first without restarting
        /// the application. On confirmation, the running game is reconfigured and a
        /// fresh game begins; on cancel, the current game is left untouched.
        /// Showing a dialog is a View-level concern, so it lives in the code-behind.
        /// </summary>
        private void OnSetupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GameViewModel vm)
                return;

            // Pre-select the settings of the game currently in progress.
            var setupVm = new SetupViewModel(vm.CurrentDifficulty, vm.ComputerStarts);
            var setupWindow = new SetupWindow { DataContext = setupVm, Owner = this };

            if (setupWindow.ShowDialog() == true)
                vm.Reconfigure(setupVm.SelectedDifficulty, setupVm.ComputerStarts);
        }
    }
}
