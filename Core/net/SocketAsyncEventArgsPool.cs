using System.Collections.Concurrent;
using System.Net.Sockets;

namespace mom {
    public class SocketAsyncEventArgsPool {
        public static SocketAsyncEventArgsPool Instance { get; } = new SocketAsyncEventArgsPool();

        public const ushort Capacity = 128;

        public void Push(SocketAsyncEventArgs item) {
            if (_items.Count >= Capacity) return;
            _items.Push(item);
        }

        public SocketAsyncEventArgs Pop() {
            SocketAsyncEventArgs ret;
            return _items.TryPop(out ret) ? ret : new SocketAsyncEventArgs();
        }

        private readonly ConcurrentStack<SocketAsyncEventArgs> _items = new ConcurrentStack<SocketAsyncEventArgs>();
    }
}