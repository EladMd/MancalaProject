using System;
using System.Windows.Input;

namespace MancalaProject.Wpf.ViewModels
{
    /// <summary>
    /// A parameterless <see cref="ICommand"/> implementation that delegates
    /// <see cref="Execute"/> and <see cref="CanExecute"/> to caller-supplied
    /// delegates. Use this for buttons that perform a single action without
    /// needing data from the View — e.g. "New Game" or "Exit".
    /// </summary>
    /// <remarks>
    /// <see cref="CanExecuteChanged"/> is wired to WPF's <see cref="CommandManager"/>
    /// so command-bound buttons automatically re-query their enabled state after
    /// every UI event. To force a re-query manually (e.g. from a model event that
    /// the CommandManager does not see), call <see cref="RaiseCanExecuteChanged"/>.
    /// </remarks>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        /// <summary>
        /// Creates a new command that runs <paramref name="execute"/> when invoked
        /// and is enabled iff <paramref name="canExecute"/> returns <c>true</c>
        /// (always enabled when <paramref name="canExecute"/> is <c>null</c>).
        /// </summary>
        /// <param name="execute">The action to run when the command is invoked. Required.</param>
        /// <param name="canExecute">Optional predicate controlling whether the command is currently enabled.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="execute"/> is <c>null</c>.</exception>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        /// <inheritdoc/>
        public void Execute(object? parameter) => _execute();

        /// <inheritdoc/>
        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Forces every bound control to re-evaluate <see cref="CanExecute"/>.
        /// Call this from the ViewModel after a state change that affects whether
        /// the command should be enabled (e.g. after a move is applied).
        /// </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// A typed <see cref="ICommand"/> implementation. Use this when the View needs
    /// to pass data to the ViewModel via <c>CommandParameter</c> — for example, a
    /// pit index when a pit button is clicked: <c>CommandParameter="3"</c>.
    /// </summary>
    /// <typeparam name="T">
    /// The expected parameter type. Values arriving from XAML are coerced via
    /// <see cref="Convert.ChangeType(object, Type)"/>, so <c>CommandParameter="3"</c>
    /// works correctly for <c>RelayCommand&lt;int&gt;</c>.
    /// </typeparam>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        /// <summary>
        /// Creates a new typed command. See <see cref="RelayCommand"/> for semantics.
        /// </summary>
        /// <param name="execute">The action to run when the command is invoked. Required.</param>
        /// <param name="canExecute">Optional predicate controlling whether the command is currently enabled for a given parameter.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="execute"/> is <c>null</c>.</exception>
        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter) =>
            _canExecute?.Invoke(ConvertParameter(parameter)) ?? true;

        /// <inheritdoc/>
        public void Execute(object? parameter) =>
            _execute(ConvertParameter(parameter));

        /// <inheritdoc/>
        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Forces every bound control to re-evaluate <see cref="CanExecute"/>.
        /// </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

        /// <summary>
        /// Coerces a raw <c>object?</c> from <see cref="ICommand"/> into <typeparamref name="T"/>.
        /// Handles three cases: already the right type, <c>null</c>, and string-from-XAML
        /// (e.g. <c>CommandParameter="5"</c> arrives as <see cref="string"/> and must be
        /// converted to <see cref="int"/>).
        /// </summary>
        private static T? ConvertParameter(object? parameter)
        {
            if (parameter is T typed) return typed;
            if (parameter is null)    return default;

            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                return (T)Convert.ChangeType(parameter, targetType);
            }
            catch
            {
                return default;
            }
        }
    }
}
