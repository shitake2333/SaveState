using Godot;

namespace SaveState.Godot
{
    /// <summary>
    /// Tells apart a <b>real game run</b> from the processes that must not touch save files.
    ///
    /// <para>Three kinds of process are easy to confuse, and all three run the same gameplay code:</para>
    /// <list type="bullet">
    ///   <item>a real run (editor play, or an exported build) — should save;</item>
    ///   <item>the editor itself / a headless test or tool run — must <b>not</b> write the player's saves
    ///         (it would overwrite real progress with synthetic data);</item>
    ///   <item>the Godot editor running with <c>--headless</c> (CI, gdUnit-style runners) — same as above.</item>
    /// </list>
    ///
    /// <para><see cref="IsEditor"/> uses both <c>Engine.IsEditorHint()</c> and the engine's
    /// <c>editor</c> feature tag: a script started from the editor with <c>--script</c> is not
    /// "editor hint" but still is not a game run.</para>
    /// </summary>
    public static class GodotRuntime
    {
        /// <summary>True inside the editor or any editor-launched process (including <c>--script</c> tools).</summary>
        public static bool IsEditor
        {
            get { return Engine.IsEditorHint() || OS.HasFeature("editor"); }
        }

        /// <summary>True when Godot runs without a display server (<c>--headless</c>): CI and test runs.</summary>
        public static bool IsHeadless
        {
            get { return string.Equals(DisplayServer.GetName(), "headless", System.StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>A real game run: not the editor, not headless. The predicate to feed <see cref="SaveGate"/>.</summary>
        public static bool IsRuntimeProcess
        {
            get { return !IsEditor && !IsHeadless; }
        }
    }
}
