using System;
#if NET35
#else
using System.Threading.Tasks;
#endif

namespace mom
{
    public interface IDispatcher
    {
        void OnPush(Session session, byte[] data);
#if NET35
        void OnRequest(Session session, byte[] data, Action<Result> cb);
#else
        Task<Result> OnRequest(Session session, byte[] data);
#endif
        void OnClose(Session session, SessionCloseReason reason);
        void OnOpen(Session session);
        void OnError(string msg);
    }

    public class DefaultDispatcher : IDispatcher
    {
#if NET35
        public virtual void OnRequest(Session session, byte[] data, Action<Result> cb)
        {
            Logger.Ins.Warn("You should override OnRequest.");
            cb(new Result((ushort) NetError.NoHandler));
        }
#else
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public virtual async Task<Result> OnRequest(Session session, byte[] data)
        {
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
            Logger.Ins.Warn("You should override OnRequest.");
            return NetError.NoHandler;
        }
#endif

        public virtual void OnPush(Session session, byte[] data)
        {
            Logger.Ins.Warn("You should override OnPush.");
        }

        public virtual void OnClose(Session session, SessionCloseReason reason)
        {
            Logger.Ins.Warn("You should override OnClose.");
        }

        public virtual void OnOpen(Session session)
        {
            Logger.Ins.Warn("You should override OnOpen.");
        }

        public virtual void OnError(string msg)
        {
            Logger.Ins.Warn("You should override OnError.");
        }
    }

    internal sealed class InternalDispatcher : IDispatcher
    {
        private readonly IDispatcher _dispatcher;
        private readonly Action<SessionCloseReason> _closeHandler;

        public InternalDispatcher(IDispatcher dispatcher, Action<SessionCloseReason> closeHandler = null)
        {
            _dispatcher = dispatcher;
            _closeHandler = closeHandler;
        }

        public void OnPush(Session session, byte[] data)
        {
            _dispatcher.OnPush(session, data);
        }

#if NET35
        public void OnRequest(Session session, byte[] data, Action<Result> cb)
        {
            _dispatcher.OnRequest(session, data, cb);
        }
#else
        public async Task<Result> OnRequest(Session session, byte[] data)
        {
            return await _dispatcher.OnRequest(session, data);
        }
#endif

        public void OnClose(Session session, SessionCloseReason reason)
        {
            Logger.Ins.Debug($"{session.Name} closed by {reason}!");
            _dispatcher?.OnClose(session, reason);
            _closeHandler?.Invoke(reason);
        }

        public void OnOpen(Session session)
        {
            Logger.Ins.Debug($"{session.Name} connected!");
            _dispatcher?.OnOpen(session);
        }

        public void OnError(string msg)
        {
            Logger.Ins.Error(msg);
            _dispatcher?.OnError(msg);
        }
    }
}