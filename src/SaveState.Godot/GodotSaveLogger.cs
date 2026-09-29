using System;
using Godot;

namespace SaveState.Godot
{
    /// <summary>
    /// An <see cref="ISaveLogger"/> that writes to Godot's own output:
    /// <c>GD.PushError</c> for errors, <c>GD.PushWarning</c> for warnings, <c>GD.Print</c> otherwise.
    ///
    /// <para>Errors and warnings use Godot's push functions so they show up as engine errors/warnings
    /// (they are also the ones a CI run can grep for), and log the real file path — a save that could
    /// not be written is otherwise very hard to diagnose.</para>
    ///
    /// <para>If the host has its own logging pipeline, forward to it instead: the framework only needs
    /// <see cref="ISaveLogger"/>.</para>
    /// </summary>
    public sealed class GodotSaveLogger : ISaveLogger
    {
        private const string Prefix = "[SaveState] ";

        /// <summary>Shared instance (the type is stateless).</summary>
        public static readonly GodotSaveLogger Instance = new GodotSaveLogger();

        /// <inheritdoc />
        public void Log(SaveLogLevel level, string message, Exception? exception = null)
        {
            string text = Prefix + message;

            switch (level)
            {
                case SaveLogLevel.Error:
                    GD.PushError(exception == null ? text : text + " | " + exception);
                    break;

                case SaveLogLevel.Warn:
                    GD.PushWarning(text);
                    break;

                default:
                    GD.Print(text);
                    break;
            }
        }
    }
}
