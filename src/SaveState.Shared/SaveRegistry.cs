using System;
using System.Collections.Generic;

namespace SaveState
{
    /// <summary>
    /// Registry of the service instances that take part in saving.
    ///
    /// <para><b>Why not <c>Xxx.Instance</c></b>: if the generated code grabbed singletons directly, it would force every
    /// host onto the same singleton base class (DI containers, manual <c>new</c> and multi-instance tests would all get
    /// stuck). The registry hands the "where does the service live" question back to the host.</para>
    ///
    /// <para><see cref="Get{T}"/> **throws** instead of returning null when nothing is registered: that is a wiring error
    /// (a line missing at startup), and failing early beats silently getting a null state while loading a save.</para>
    /// </summary>
    public sealed class SaveRegistry
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>Registers a service instance (registering the same type twice throws <see cref="SaveStateException"/>).</summary>
        public SaveRegistry Add<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Type type = typeof(T);
            if (_services.ContainsKey(type))
            {
                throw new SaveStateException(
                    "Service " + type.FullName + " is already registered - a save service can only have a single instance.");
            }

            _services[type] = service;
            return this;
        }

        /// <summary>Gets a service instance; throws <see cref="SaveStateException"/> when it is not registered (carrying the type name and a hint on how to fix it).</summary>
        public T Get<T>() where T : class
        {
            T? service;
            if (TryGet(out service))
            {
                return service!;
            }

            throw new SaveStateException(
                "Service " + typeof(T).FullName + " is not registered in the SaveRegistry. "
                + "Saving and loading need it: call registry.Add(...) at startup, or check for a missing [SaveService] attribute.");
        }

        /// <summary>Gets a service instance (returns false instead of throwing when it does not exist).</summary>
        public bool TryGet<T>(out T? service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object? found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>Number of registered services (for diagnostics).</summary>
        public int Count
        {
            get { return _services.Count; }
        }
    }
}
