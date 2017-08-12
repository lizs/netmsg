#if NET35
#else
using System.Collections.Concurrent;
#endif
using System.Net.Sockets;

namespace mom
{
    public class SocketAsyncEventArgsPool
    {
        public static SocketAsyncEventArgsPool Ins { get; } = new SocketAsyncEventArgsPool();

        public const ushort Capacity = 128;

        public void Push(SocketAsyncEventArgs item)
        {
            item.UserToken = null;
            item.BufferList = null;

            if (_items.Count >= Capacity)
            {
                item.Dispose();
                return;
            }
            _items.Push(item);
        }

        public SocketAsyncEventArgs Pop()
        {
            SocketAsyncEventArgs ret;
            return _items.TryPop(out ret) ? ret : new SocketAsyncEventArgs();
        }

        private readonly ConcurrentStack<SocketAsyncEventArgs> _items = new ConcurrentStack<SocketAsyncEventArgs>();
    }
}