using System;

namespace SaveState
{
    /// <summary>
    /// The exception thrown by the framework. Its meaning: a **wiring/contract error** (a service is not registered, an
    /// illegal file name, a section that cannot be written...), not "the save data is wrong" (data problems always go
    /// through <see cref="LoadResult"/>'s <see cref="LoadStatus.Failed"/>).
    ///
    /// <para>That line matters: data problems must be able to degrade gracefully (keep playing with defaults), whereas
    /// wiring errors must be exposed immediately.</para>
    /// </summary>
    public class SaveStateException : Exception
    {
        public SaveStateException(string message) : base(message)
        {
        }

        public SaveStateException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
