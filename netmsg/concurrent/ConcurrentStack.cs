#if NET35
using System.Collections.Generic;

namespace mom
{
    public class ConcurrentStack<T>
    {
        private readonly object _lock = new object();
        private readonly Stack<T> _items = new Stack<T>();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public void Push(T item)
        {
            lock (_lock)
            {
                _items.Push(item);
            }
        }

        public bool TryPop(out T item)
        {
            lock (_lock)
            {
                if (_items.Count > 0)
                {
                    item = _items.Pop();
                    return true;
                }

                item = default(T);
                return false;
            }
        }
    }
}

#endif