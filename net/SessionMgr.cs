﻿#region MIT

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

#if NET35
#else
using System.Collections.Concurrent;
#endif
using System.Linq;

namespace mom
{
    public class SessionMgr
    {
        private readonly ConcurrentDictionary<ushort, Session> _items = new ConcurrentDictionary<ushort, Session>();

        public void Stop()
        {
            Clear();
        }

        public bool AddSession(Session session)
        {
            return _items.TryAdd(session.Id, session);
        }

        public bool RemoveSession(ushort id, SessionCloseReason reason)
        {
            Session session;
            return _items.TryRemove(id, out session);
        }

        public void Clear()
        {
            foreach (var session in _items.Select(x => x.Value))
            {
                session.Close(SessionCloseReason.Stop);
            }

            _items.Clear();
        }

        ///// <summary>
        /////     广播
        ///// </summary>
        //public void Broadcast(byte[] data)
        //{
        //    Session.Broadcast(_items.Values.ToArray(), data);
        //}
    }
}
