using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SaveState.Generator
{
    /// <summary>
    /// An immutable list with <b>value equality</b>.
    ///
    /// <para>Why this exists: an incremental generator's models are compared to decide whether a pipeline step may be
    /// skipped on the next keystroke. <see cref="ImmutableArray{T}"/> compares by <em>reference</em>, so a
    /// <c>Collect()</c> result wrapped in one is "different" on every edit - the cache never hits and the generator
    /// re-runs (and re-emits every source) on each change. Wrapping the collected models in this type restores the
    /// comparison that makes incremental generation incremental.</para>
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
        where T : IEquatable<T>
    {
        /// <summary>The empty list.</summary>
        public static readonly EquatableArray<T> Empty = new EquatableArray<T>(ImmutableArray<T>.Empty);

        private readonly ImmutableArray<T> _items;

        public EquatableArray(ImmutableArray<T> items)
        {
            _items = items.IsDefault ? ImmutableArray<T>.Empty : items;
        }

        public EquatableArray(IEnumerable<T> items)
        {
            _items = items == null ? ImmutableArray<T>.Empty : ImmutableArray.CreateRange(items);
        }

        /// <summary>
        /// The backing array, normalising a <c>default</c> instance.
        ///
        /// <para>An uninitialised <see cref="ImmutableArray{T}"/> throws on almost every member, including
        /// <c>Length</c> - and <c>default(EquatableArray&lt;T&gt;)</c> is exactly what a struct field looks like
        /// before assignment. Normalising here keeps a default value benign.</para>
        /// </summary>
        private ImmutableArray<T> Items
        {
            get { return _items.IsDefault ? ImmutableArray<T>.Empty : _items; }
        }

        public int Count
        {
            get { return Items.Length; }
        }

        public T this[int index]
        {
            get { return Items[index]; }
        }

        public bool Equals(EquatableArray<T> other)
        {
            ImmutableArray<T> left = Items;
            ImmutableArray<T> right = other.Items;

            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is EquatableArray<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                ImmutableArray<T> items = Items;
                int hash = 17;
                for (int i = 0; i < items.Length; i++)
                {
                    hash = (hash * 31) + (items[i] == null ? 0 : items[i]!.GetHashCode());
                }

                return hash;
            }
        }

        /// <summary>Value equality (an <see cref="EquatableArray{T}"/> is a value, not a handle).</summary>
        public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>See <see cref="operator ==(EquatableArray{T}, EquatableArray{T})"/>.</summary>
        public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        {
            return !left.Equals(right);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
