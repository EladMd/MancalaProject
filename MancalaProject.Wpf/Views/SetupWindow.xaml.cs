using System.Windows;

namespace MancalaProject.Wpf.Views
{
    /// <summary>
    /// Code-behind for the setup dialog.
    /// Deliberately minimal: its only job is to set <see cref="Window.DialogResult"/>
    /// when the user confirms. The Cancel button uses <c>IsCancel="True"</c> in XAML,
    /// which sets <see cref="Window.DialogResult"/> to <c>false</c> automatically.
    /// </summary>
    public partial class SetupWindow : Window
    {
        /// <summary>
        /// Initializes the setup dialog. The dialog has no constructor parameters because
        /// its state lives on the <c>SetupViewModel</c> that callers assign as
        /// <see cref="FrameworkElement.DataContext"/> before showing it.
        /// </summary>
        public SetupWindow()
        {
            InitializeComponent();
        }

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;   // closes the dialog and signals the caller to launch the game
        }
    }
}
