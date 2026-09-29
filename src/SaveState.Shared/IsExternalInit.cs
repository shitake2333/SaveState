#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// netstandard2.0 lacks the marker type that <c>init</c> accessors and <c>record</c>s need.
    ///
    /// <para>This is a compile-time polyfill: it is <c>internal</c> and is only compiled for the netstandard2.0 target,
    /// so it does not clash with the same-named polyfill of the host or of other libraries (each assembly gets its own
    /// internal copy of the type).</para>
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
