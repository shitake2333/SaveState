using System;

namespace SaveState
{
    /// <summary>
    /// Declares a "state service": some of its members take part in saving.
    ///
    /// <para>The service instance is created by the host and registered in <see cref="SaveRegistry"/> (the library does
    /// not assume a singleton: only a registry plus explicit registration can support DI containers, manual <c>new</c>
    /// and lazily created singletons all at once).</para>
    ///
    /// <para>The members that take part in saving are marked with <see cref="SavedStateAttribute"/>, which also names
    /// the save file they belong to.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SaveServiceAttribute : Attribute
    {
    }
}
