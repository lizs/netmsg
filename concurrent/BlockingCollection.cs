using System.Collections.Generic;
using System.Threading;

#if NET35

namespace mom
{
    public class BlockingCollection<T>
    {
        private readonly Queue<T> _items = new Queue<T>();
        private readonly object _lock = new object();
        private readonly AutoResetEvent _event = new AutoResetEvent(false);

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

        public void Add(T item)
        {
            lock (_lock)
            {
                _items.Enqueue(item);
                _event.Set();
            }
        }

        public bool Take(out T item)
        {
            lock (_lock)
            {
                if (_items.Count > 0)
                {
                    item = _items.Dequeue();
                    return true;
                }

                item = default(T);
                return false;
            }
        }

        public bool TryTake(out T item, int period)
        {
            if (_event.WaitOne(period))
            {
                return Take(out item);
            }

            item = default(T);
            return false;
        }
    }
}

#endif
