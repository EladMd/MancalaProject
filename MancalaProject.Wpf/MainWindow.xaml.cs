using System.Windows;
using MancalaProject.Wpf.ViewModels;

namespace MancalaProject.Wpf
{
    /// <summary>
    /// Code-behind for the main game window.
    /// Deliberately minimal: instantiates a <see cref="GameViewModel"/> using the
    /// settings the user chose in the setup dialog, assigns it as the window's
    /// <see cref="FrameworkElement.DataContext"/>, and bridges its
    /// <see cref="GameViewModel.GameOver"/> event to a native modal dialog.
    /// All actual logic — binding, commands, game flow — lives in the ViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Parameterless constructor kept for the XAML designer. Should not be called
        /// at runtime; <see cref="App.OnStartup"/> always uses the parameterized overload.
        /// Defaults to Player vs Player so the designer / hot-reload preview is sane.
        /// </summary>
        public MainWindow() : this(vsComputer: false, difficulty: Difficulty.Hard)
        {
        }

        /// <summary>
        /// Real runtime entry point. Creates the game with the mode and difficulty
        /// chosen on the setup dialog and wires the game-over dialog.
        /// </summary>
        public MainWindow(bool vsComputer, Difficulty difficulty)
        {
            InitializeComponent();

            var vm = new GameViewModel(vsComputer, difficulty);

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
    }
}
