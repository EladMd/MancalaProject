using System.Windows;
using MancalaProject.Wpf.ViewModels;
using MancalaProject.Wpf.Views;

namespace MancalaProject.Wpf
{
    /// <summary>
    /// Application entry point. Overrides <see cref="Application.OnStartup"/> so the
    /// user is shown a setup dialog before the main game window is created. This is
    /// why <c>StartupUri</c> has been removed from <c>App.xaml</c> — we control the
    /// startup flow ourselves rather than letting the framework auto-launch
    /// <c>MainWindow</c>.
    /// </summary>
    public partial class App : Application
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Shows the setup dialog before the main window is created, then launches
        /// <see cref="MainWindow"/> with the user's chosen difficulty and starting player.
        /// Manages <see cref="Application.ShutdownMode"/> manually to avoid the
        /// race where WPF would otherwise auto-shutdown between dialog close and
        /// main-window show.
        /// </remarks>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // The default ShutdownMode is OnLastWindowClose. With it, when the
            // setup dialog closes (and it is briefly the application's only
            // window) WPF schedules an app-wide shutdown — which then races
            // against our attempt to show the main window. Switch to explicit
            // shutdown for the setup phase, and restore normal behavior once
            // the main window is up.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var setupVm = new SetupViewModel();
            var setupWindow = new SetupWindow { DataContext = setupVm };

            // ShowDialog blocks until the user clicks Start (DialogResult=true)
            // or Cancel / closes the window (DialogResult=false or null).
            if (setupWindow.ShowDialog() == true)
            {
                var mainWindow = new MainWindow(setupVm.SelectedDifficulty, setupVm.ComputerStarts);
                MainWindow = mainWindow;   // tell the framework which window is "the" main window
                mainWindow.Show();

                // Main window is now alive — re-enable automatic shutdown when
                // it closes.
                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
            else
            {
                // User cancelled — exit cleanly.
                Shutdown();
            }
        }
    }
}
