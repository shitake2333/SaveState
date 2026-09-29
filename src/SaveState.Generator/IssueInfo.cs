using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SaveState.Generator
{
    /// <summary>
    /// A reported problem, stored in a model so the generator can emit it later (a transform cannot report).
    ///
    /// <para><b>Why not store <see cref="Diagnostic"/> directly</b>: a model must be comparable for incremental
    /// caching, and <see cref="Diagnostic"/> / <see cref="Location"/> have no value equality - one instance per run
    /// would make every model "different" and defeat the cache. This type keeps the descriptor + arguments and
    /// compares by a stable location key (file, line, column) instead.</para>
    /// </summary>
    internal readonly struct IssueInfo : IEquatable<IssueInfo>
    {
        private readonly object[] _args;
        private readonly string _locationKey;

        public IssueInfo(DiagnosticDescriptor descriptor, Location? location, params object[] args)
        {
            Descriptor = descriptor;
            Location = location;
            _args = args ?? new object[0];
            _locationKey = LocationKey(location);
        }

        public DiagnosticDescriptor Descriptor { get; }

        /// <summary>The syntax location to point at (excluded from equality; <see cref="_locationKey"/> stands in for it).</summary>
        public Location? Location { get; }

        public DiagnosticSeverity Severity
        {
            get { return Descriptor.DefaultSeverity; }
        }

        public Diagnostic ToDiagnostic()
        {
            return Diagnostic.Create(Descriptor, Location, _args);
        }

        public bool Equals(IssueInfo other)
        {
            return string.Equals(Descriptor.Id, other.Descriptor.Id, StringComparison.Ordinal)
                   && string.Equals(_locationKey, other._locationKey, StringComparison.Ordinal)
                   && ArgsEqual(_args, other._args);
        }

        public override bool Equals(object? obj)
        {
            return obj is IssueInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Descriptor.Id.GetHashCode();
                hash = (hash * 31) + _locationKey.GetHashCode();
                for (int i = 0; i < _args.Length; i++)
                {
                    hash = (hash * 31) + (_args[i] == null ? 0 : _args[i]!.ToString()!.GetHashCode());
                }

                return hash;
            }
        }

        private static bool ArgsEqual(object[] left, object[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                string a = left[i] == null ? string.Empty : left[i]!.ToString()!;
                string b = right[i] == null ? string.Empty : right[i]!.ToString()!;
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string LocationKey(Location? location)
        {
            if (location == null || location.SourceTree == null)
            {
                return "<none>";
            }

            FileLinePositionSpan span = location.GetLineSpan();
            return span.Path + ":" + span.StartLinePosition.Line + ":" + span.StartLinePosition.Character;
        }
    }
}
