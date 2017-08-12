using System.Collections;
using System.Collections.Generic;

#if NET35

namespace mom
{
    public class ConcurrentDictionary<TKey, TVal> : IEnumerable<KeyValuePair<TKey, TVal>>
    {
        private readonly object _lock = new object();
        private readonly Dictionary<TKey, TVal> _items = new Dictionary<TKey, TVal>();

        public bool TryAdd(TKey key, TVal val)
        {
            lock (_lock)
            {
                if (_items.ContainsKey(key))
                    return false;

                _items.Add(key, val);
                return true;
            }
        }

        public bool TryRemove(TKey key, out TVal val)
        {
            lock (_lock)
            {
                if (_items.ContainsKey(key))
                {
                    val = _items[key];
                    return _items.Remove(key);
                }

                val = default(TVal);
                return false;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }

        public IEnumerator<KeyValuePair<TKey, TVal>> GetEnumerator()
        {
            lock (_lock)
            {
                foreach (var kv in _items)
                {
                    yield return kv;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

#endif
