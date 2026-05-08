using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MancalaProject.Wpf.ViewModels
{
    /// <summary>
    /// Base class for any ViewModel that needs to notify the View when one of its
    /// properties changes. Provides a small, allocation-free wrapper around
    /// <see cref="INotifyPropertyChanged"/> using <see cref="CallerMemberNameAttribute"/>
    /// so derived classes never have to repeat property names as strings.
    /// </summary>
    /// <remarks>
    /// Two derived classes are anticipated: <c>GameViewModel</c> (the live game) and
    /// <c>SetupViewModel</c> (the start-up dialog for choosing mode and difficulty).
    /// Sharing this base keeps the INPC plumbing in one place and consistent.
    /// </remarks>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises <see cref="PropertyChanged"/> for the given property. The argument
        /// is filled in automatically by the compiler from the calling property's
        /// name, so callers normally write <c>OnPropertyChanged()</c> with no arguments.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed. Auto-supplied.</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
        /// <see cref="PropertyChanged"/> — but only if the value actually differs.
        /// This avoids redundant UI re-renders when a property is reassigned to its
        /// current value.
        /// </summary>
        /// <typeparam name="T">The field's type.</typeparam>
        /// <param name="field">A reference to the backing field.</param>
        /// <param name="value">The proposed new value.</param>
        /// <param name="propertyName">The name of the property. Auto-supplied.</param>
        /// <returns><c>true</c> if the field changed, <c>false</c> if the value was already equal.</returns>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
