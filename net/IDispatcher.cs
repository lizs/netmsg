using System;
using System.Threading.Tasks;

namespace mom {
    public interface IDispatcher {
        void OnPush(Session session, byte[] data);
        Task<Result> OnRequest(Session session, byte[] data);
        void OnClose(Session session, SessionCloseReason reason);
        void OnOpen(Session session);
    }

    public class DefaultDispatcher : IDispatcher {
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public virtual async Task<Result> OnRequest(Session session, byte[] data) {
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
            return new Result((ushort) NetError.NoHandler, null);
        }

        public virtual void OnPush(Session session, byte[] data) {}
        public virtual void OnClose(Session session, SessionCloseReason reason) {}
        public virtual void OnOpen(Session session) {}
    }

    internal sealed class InternalDispatcher : IDispatcher {
        private readonly IDispatcher _dispatcher;
        private readonly Action<SessionCloseReason> _closeHandler;

        public InternalDispatcher(IDispatcher dispatcher, Action<SessionCloseReason> closeHandler = null) {
            _dispatcher = dispatcher;
            _closeHandler = closeHandler;
        }

        public void OnPush(Session session, byte[] data) {
            _dispatcher.OnPush(session, data);
        }

        public async Task<Result> OnRequest(Session session, byte[] data) {
            return await _dispatcher.OnRequest(session, data);
        }

        public void OnClose(Session session, SessionCloseReason reason) {
            Logger.Ins.Debug("{0} closed by {1}!", session.Name, reason);
            _dispatcher?.OnClose(session, reason);
            _closeHandler?.Invoke(reason);
        }

        public void OnOpen(Session session) {
            Logger.Ins.Debug("{0} connected!", session.Name);
            _dispatcher?.OnOpen(session);
        }
    }
}