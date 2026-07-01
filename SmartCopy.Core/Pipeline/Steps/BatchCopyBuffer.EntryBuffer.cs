using System.Buffers;

namespace SmartCopy.Core.Pipeline.Steps;

internal sealed partial class BatchCopyBuffer
{
    private sealed class PooledEntryBuffer : IReadOnlyList<Entry>, IDisposable
    {
        private readonly ArrayPool<Entry> _pool;
        private Entry[] _items;
        private int _count;

        public PooledEntryBuffer(ArrayPool<Entry> pool, int initialCapacity)
        {
            _pool = pool;
            _items = _pool.Rent(Math.Max(1, initialCapacity));
        }

        public int Count => _count;

        public Entry this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _items[index];
            }
        }

        public void Add(Entry entry)
        {
            if (_count == _items.Length)
                Grow();

            _items[_count] = entry;
            _count++;
        }

        public void Clear()
        {
            if (_count == 0)
                return;

            Array.Clear(_items, 0, _count);
            _count = 0;
        }

        public IEnumerator<Entry> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
                yield return _items[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (_items.Length == 0)
                return;

            _pool.Return(_items, clearArray: true);
            _items = [];
            _count = 0;
        }

        private void Grow()
        {
            var next = _pool.Rent(_items.Length * 2);
            Array.Copy(_items, next, _count);
            _pool.Return(_items, clearArray: true);
            _items = next;
        }
    }
}
