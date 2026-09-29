using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SaveState.DependencyInjection
{
    /// <summary>
    /// Container wiring for SaveState: one <c>AddSaveState</c> call, then resolve sessions on demand.
    ///
    /// <code>
    /// services.AddSingleton&lt;PlayerProfile&gt;();          // your services, registered normally
    ///
    /// services.AddSaveState(options =&gt;
    /// {
    ///     options.Store    = new FileSystemStore(saveDirectory);
    ///     options.Logger   = new DelegateSaveLogger((level, message, ex) =&gt; logger.Log(level, message, ex));
    ///     options.RegisterServices = (registry, resolve) =&gt; registry.AddGeneratedServices(resolve);
    /// });
    ///
    /// // later:
    /// var session = provider.GetSaveFileSession&lt;ProfileSaveFile&gt;();
    /// session.EnsureCreated();
    /// </code>
    ///
    /// <para>Why the registry is filled through a delegate rather than reflection: the framework must not
    /// know or guess how services are constructed. The generated helper gives you "register all
    /// [SaveService] types of this assembly" without a container dependency inside the library.</para>
    /// </summary>
    public static class SaveStateServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISaveStore"/>, <see cref="ISaveLogger"/>,
        /// <see cref="ISaveMigration"/> and <see cref="SaveRegistry"/>, and makes
        /// <see cref="GetSaveFileSession{TFile}"/> work.
        /// </summary>
        /// <exception cref="SaveStateException">When <see cref="SaveStateOptions.Store"/> is not set.</exception>
        public static IServiceCollection AddSaveState(
            this IServiceCollection services,
            Action<SaveStateOptions> configure)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new SaveStateOptions();
            configure(options);

            if (options.Store == null)
            {
                throw new SaveStateException(
                    "AddSaveState requires SaveStateOptions.Store - only the host knows where saves may live.");
            }

            services.TryAddSingleton(options);
            services.TryAddSingleton(options.Store);
            services.TryAddSingleton(options.Logger ?? (ISaveLogger)NullSaveLogger.Instance);

            foreach (ISaveMigration migration in options.Migrations)
            {
                services.AddSingleton(migration);
            }

            // The registry is built lazily: services are resolved after the container is built.
            services.TryAddSingleton(sp =>
            {
                var registry = new SaveRegistry();
                options.RegisterServices?.Invoke(registry, type => sp.GetRequiredService(type));
                return registry;
            });

            return services;
        }

        /// <summary>
        /// Registers a save service as a singleton. Sugar over <c>AddSingleton&lt;TService&gt;()</c> that
        /// documents the intent (and keeps the service alive for as long as the container is).
        /// </summary>
        public static IServiceCollection AddSaveService<TService>(this IServiceCollection services)
            where TService : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return services.AddSingleton<TService>();
        }

        /// <summary>
        /// Creates a session for one save file from the container
        /// (store + registry + logger + migrations + JSON policy).
        /// </summary>
        public static SaveFileSession<TFile> GetSaveFileSession<TFile>(this IServiceProvider provider)
            where TFile : SaveFileDefinition, new()
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            SaveStateOptions options = provider.GetService<SaveStateOptions>() ?? new SaveStateOptions();

            return new SaveFileSession<TFile>(
                provider.GetRequiredService<ISaveStore>(),
                provider.GetRequiredService<SaveRegistry>(),
                provider.GetRequiredService<ISaveLogger>(),
                provider.GetServices<ISaveMigration>(),
                options.JsonOptions);
        }
    }
}
