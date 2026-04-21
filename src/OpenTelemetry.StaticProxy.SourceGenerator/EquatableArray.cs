using System.Collections;
using System.Collections.Immutable;

namespace OpenTelemetry.StaticProxy;

/// <summary>
/// An immutable array wrapper that implements structural equality for use in Incremental Generator caching.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array)
    : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public ImmutableArray<T> Array { get; } = array;

    public int Length => Array.IsDefault ? 0 : Array.Length;

    public T this[int index] => Array[index];

    public bool IsDefault => Array.IsDefault;

    public bool IsDefaultOrEmpty => Array.IsDefaultOrEmpty;

    public static EquatableArray<T> Empty { get; } = new([]);

    public bool Equals(EquatableArray<T> other)
    {
        if (Array.IsDefault && other.Array.IsDefault) return true;
        if (Array.IsDefault || other.Array.IsDefault) return false;
        if (Array.Length != other.Array.Length) return false;

        for (var i = 0; i < Array.Length; i++)
        {
            if (!Array[i].Equals(other.Array[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (Array.IsDefault) return 0;

        unchecked
        {
            var hash = 17;

            foreach (var item in Array)
            {
                hash = hash * 31 + item.GetHashCode();
            }

            return hash;
        }
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() =>
        Array.IsDefault ? ImmutableArray<T>.Empty.GetEnumerator() : Array.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)(Array.IsDefault ? [] : Array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)(Array.IsDefault ? [] : Array)).GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    public static implicit operator ImmutableArray<T>(EquatableArray<T> array) =>
        array.Array.IsDefault ? [] : array.Array;
}
