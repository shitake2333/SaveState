using System.Collections.Immutable;
using SaveState.Generator;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// The value-comparable list that makes the incremental generator's cache work: an
/// <see cref="ImmutableArray{T}"/> compares by reference, which would make every edit look like a change.
/// </summary>
public class EquatableArrayTests
{
    [Fact]
    public void TwoArraysWithTheSameContent_AreEqual()
    {
        var left = new EquatableArray<Item>(ImmutableArray.Create(new Item("a"), new Item("b")));
        var right = new EquatableArray<Item>(ImmutableArray.Create(new Item("a"), new Item("b")));

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(2, left.Count);
    }

    [Fact]
    public void DifferentContentOrOrder_IsNotEqual()
    {
        var ab = new EquatableArray<Item>(ImmutableArray.Create(new Item("a"), new Item("b")));
        var ba = new EquatableArray<Item>(ImmutableArray.Create(new Item("b"), new Item("a")));
        var onlyA = new EquatableArray<Item>(ImmutableArray.Create(new Item("a")));

        Assert.False(ab.Equals(ba));
        Assert.False(ab.Equals(onlyA));
        Assert.False(ab.Equals(EquatableArray<Item>.Empty));
    }

    [Fact]
    public void DefaultAndEmpty_BehaveLikeAnEmptyList()
    {
        EquatableArray<Item> fromDefaultInstance = default;

        Assert.Empty(fromDefaultInstance);
        Assert.True(fromDefaultInstance.Equals(EquatableArray<Item>.Empty));
        Assert.True(fromDefaultInstance == EquatableArray<Item>.Empty);
    }

    [Fact]
    public void CanBeBuiltFromAnyEnumerable_AndEnumerated()
    {
        var array = new EquatableArray<int>(new[] { 3, 4, 5 });

        Assert.Equal(3, array.Count);
        Assert.Equal(4, array[1]);

        int sum = 0;
        foreach (int value in array)
        {
            sum += value;
        }

        Assert.Equal(12, sum);
    }

    private sealed class Item : System.IEquatable<Item>
    {
        private readonly string _value;

        public Item(string value)
        {
            _value = value;
        }

        public bool Equals(Item? other)
        {
            return other != null && string.Equals(_value, other._value, System.StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as Item);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }
    }
}
