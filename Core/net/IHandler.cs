using System;
using System.Threading.Tasks;

namespace mom {
    public interface IHandler {
        void OnPush(Session session, byte[] data);
        Task<Tuple<ushort, byte[]>> OnRequest(Session session, byte[] data);
        void OnClose(Session session, SessionCloseReason reason);
        void OnOpen(Session session);
    }

    public class DefaultHandler : IHandler {
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public virtual async Task<Tuple<ushort, byte[]>> OnRequest(Session session, byte[] data) {
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
            return new Tuple<ushort, byte[]>((ushort) NetError.NoHandler, null);
        }

        public virtual void OnPush(Session session, byte[] data) {}
        public virtual void OnClose(Session session, SessionCloseReason reason) {}
        public virtual void OnOpen(Session session) {}
    }

    internal sealed class InternalHandler : IHandler {
        private readonly IHandler _handler;
        private readonly Action<SessionCloseReason> _closeHandler;

        public InternalHandler(IHandler handler, Action<SessionCloseReason> closeHandler = null) {
            _handler = handler;
            _closeHandler = closeHandler;
        }

        public void OnPush(Session session, byte[] data) {
            _handler.OnPush(session, data);
        }

        public async Task<Tuple<ushort, byte[]>> OnRequest(Session session, byte[] data) {
            return await _handler.OnRequest(session, data);
        }

        public void OnClose(Session session, SessionCloseReason reason) {
            Logger.Ins.Info("{0} closed by {1}!", session.Name, reason);
            _handler?.OnClose(session, reason);
            _closeHandler?.Invoke(reason);
        }

        public void OnOpen(Session session) {
            Logger.Ins.Info("{0} connected!", session.Name);
            _handler?.OnOpen(session);
        }
    }
}