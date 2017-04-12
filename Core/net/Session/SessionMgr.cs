#region MIT

//  /*The MIT License (MIT)
// 
//  Copyright 2016 lizs lizs4ever@163.com
//  
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//  
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
//  
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//   * */

#endregion

using System;
using System.Collections.Concurrent;
using System.Linq;

namespace mom {
    public class SessionMgr {
        private Action<Session> _openCb;
        private Action<Session, SessionCloseReason> _closeCb;
        private readonly ConcurrentDictionary<ushort, Session> _items = new ConcurrentDictionary<ushort, Session>();

        public SessionMgr(Action<Session> openCb, Action<Session, SessionCloseReason> closeCb) {
            _openCb = openCb;
            _closeCb = closeCb;
        }

        public void Stop() {
            Clear();

            _openCb = null;
            _closeCb = null;
        }

        public void AddSession(Session session) {
            if (_items.TryAdd(session.Id, session)) {
                _openCb?.Invoke(session);
            }
            else
                Logger.Ins.Warn("Insert session failed for id : " + session.Id);
        }

        public void RemoveSession(ushort id, SessionCloseReason reason) {
            Session session;
            if (_items.TryRemove(id, out session)) {
                if (_closeCb == null) return;
                _closeCb(session, reason);
            }
            else if (_items.ContainsKey(id))
                Logger.Ins.Warn("Remove session failed for id : " + id);
            else
                Logger.Ins.Warn("Remove session failed for id :  cause of it doesn't exist" + id);
        }

        public void Clear() {
            foreach (var session in _items.Select(x => x.Value)) {
                session.Close(SessionCloseReason.ClosedByMyself);
            }
        }
    }
}
