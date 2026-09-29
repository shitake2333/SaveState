namespace SaveState.Godot
{
    /// <summary>
    /// Convenience composition of the Godot adapter: the gate that keeps auto-saving out of the editor
    /// and out of headless tool runs.
    ///
    /// <code>
    /// var gate = GodotSavePolicy.CreateRuntimeGate(GodotSaveLogger.Instance);
    ///
    /// // event-driven save points (unlock, end of run, …) go through the gate:
    /// gate.TrySave(session.Save);
    ///
    /// // explicit save points (settings screen "Apply", first-launch file creation) call Save() directly:
    /// session.Save();
    /// </code>
    ///
    /// <para>Tests and tools can force the answer with <see cref="SaveGate.AllowOverride"/> without
    /// touching <see cref="GodotRuntime.IsRuntimeProcess"/> (that is how the host project's own tests
    /// verify "unlock writes the file" while still using a throwaway store).</para>
    /// </summary>
    public static class GodotSavePolicy
    {
        /// <summary>Creates a gate that allows writes only in a real game run.</summary>
        /// <param name="logger">Where skipped writes are reported.</param>
        public static SaveGate CreateRuntimeGate(ISaveLogger? logger = null)
        {
            return new SaveGate(() => GodotRuntime.IsRuntimeProcess, logger ?? GodotSaveLogger.Instance);
        }
    }
}
