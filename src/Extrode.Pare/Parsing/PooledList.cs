using System.Buffers;

namespace Extrode.Pare.Parsing;

/// <summary>
/// Growable list over <see cref="ArrayPool{T}"/>. Speculative parses roll back with
/// <see cref="Truncate"/>. Call <see cref="Dispose"/> to return the array.
/// </summary>
internal struct PooledList<T> : IDisposable
{
    private T[]? _items;
    private int _count;

    public readonly int Count => _count;

    public readonly ReadOnlySpan<T> Items => _items is null ? default : _items.AsSpan(0, _count);

    public readonly Span<T> Mutable => _items is null ? default : _items.AsSpan(0, _count);

    public void Add(in T item)
    {
        if (_items is null || _count == _items.Length)
            Grow();
        _items![_count++] = item;
    }

    public void Truncate(int count)
    {
        if (count < _count)
            _count = count;
    }

    private void Grow()
    {
        var bigger = ArrayPool<T>.Shared.Rent(_items is null ? 64 : _items.Length * 2);
        if (_items is not null)
        {
            _items.AsSpan(0, _count).CopyTo(bigger);
            ArrayPool<T>.Shared.Return(_items);
        }
        _items = bigger;
    }

    public void Dispose()
    {
        if (_items is not null)
            ArrayPool<T>.Shared.Return(_items);
        _items = null;
        _count = 0;
    }
}
