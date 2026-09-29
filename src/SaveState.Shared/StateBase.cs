using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaveState
{
    /// <summary>
    /// Base class for state objects (ViewModels): plain serializable data plus <see cref="INotifyPropertyChanged"/>.
    ///
    /// <para>State is "runtime data derived from static data or level definitions", held by a service and read and
    /// written by the save framework. It is <b>not</b> responsible for writing to disk and does <b>not</b> know where the
    /// file is - those two jobs belong to <c>SaveFileSession</c> and <see cref="ISaveStore"/> respectively.</para>
    ///
    /// <para><see cref="OnPropertyChanged"/> is public: when a collection field is added to or removed from in place
    /// (<c>List.Add</c> raises no notification), the host has to raise one explicitly before anything is left for UI
    /// bindings to listen to.</para>
    /// </summary>
    public abstract class StateBase : INotifyPropertyChanged
    {
        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Assigns the value and raises a notification only when the value **really changed**; returns whether it changed (handy for short-circuiting follow-up logic).</summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>Raises a property-changed notification by hand (for use after modifying a collection field in place).</summary>
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
