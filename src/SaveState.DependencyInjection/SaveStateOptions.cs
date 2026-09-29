using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SaveState.DependencyInjection
{
    /// <summary>
    /// Everything <see cref="SaveStateServiceCollectionExtensions.AddSaveState"/> needs to know.
    ///
    /// <para><see cref="Store"/> is required: only the host knows where saves may live
    /// (<c>user://</c>, <c>persistentDataPath</c>, a server directory, memory in tests).
    /// Everything else has a sensible default.</para>
    /// </summary>
    public sealed class SaveStateOptions
    {
        /// <summary>Where the save bytes go. Required.</summary>
        public ISaveStore? Store { get; set; }

        /// <summary>Where non-fatal problems (failed writes, repaired sections) are reported.</summary>
        public ISaveLogger? Logger { get; set; }

        /// <summary>Migrations applied while loading older files.</summary>
        public List<ISaveMigration> Migrations { get; } = new List<ISaveMigration>();

        /// <summary>
        /// Optional JSON policy override; <c>null</c> means <see cref="SaveJson.DefaultOptions"/>.
        /// </summary>
        public JsonSerializerOptions? JsonOptions { get; set; }

        /// <summary>
        /// Optional: fill the <see cref="SaveRegistry"/> from the container.
        ///
        /// <para>Recommended value: <c>(registry, resolve) =&gt; registry.AddGeneratedServices(resolve)</c> —
        /// the generator emits that helper next to your services, so no service can be forgotten.
        /// Manual alternative: <c>(registry, _) =&gt; registry.Add(myService)</c>.</para>
        /// </summary>
        public Action<SaveRegistry, Func<Type, object>>? RegisterServices { get; set; }
    }
}
