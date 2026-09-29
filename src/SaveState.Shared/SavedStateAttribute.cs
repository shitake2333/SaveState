using System;

namespace SaveState
{
    /// <summary>
    /// Marks a member (property or field) as taking part in saving.
    ///
    /// <para>The declaring class must be marked with <see cref="SaveServiceAttribute"/>. Member accessibility is
    /// unrestricted: the generator emits the accessors into a partial of the same class, so even a <c>private set</c>
    /// can be read and written.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class SavedStateAttribute : Attribute
    {
        /// <summary>
        /// The key of this section in the save file (which is also the name of the generated DTO property). Defaults to
        /// the member name. It must be a valid C# identifier (the generator validates this at compile time).
        /// </summary>
        public string? Key { get; init; }

        /// <summary>
        /// The save file it belongs to; must match one of the <see cref="SaveFileAttribute.FileName"/> values.
        ///
        /// <para>It may be omitted when the whole assembly has just one save file - the generator assigns it
        /// automatically; when there are several files and it is omitted, a compile-time diagnostic is reported (the
        /// generator will not guess).</para>
        /// </summary>
        public string? File { get; init; }
    }
}
