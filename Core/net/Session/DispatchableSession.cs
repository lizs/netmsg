﻿//#region MIT
////  /*The MIT License (MIT)
//// 
////  Copyright 2016 lizs lizs4ever@163.com
////  
////  Permission is hereby granted, free of charge, to any person obtaining a copy
////  of this software and associated documentation files (the "Software"), to deal
////  in the Software without restriction, including without limitation the rights
////  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
////  copies of the Software, and to permit persons to whom the Software is
////  furnished to do so, subject to the following conditions:
////  
////  The above copyright notice and this permission notice shall be included in
////  all copies or substantial portions of the Software.
////  
////  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
////  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
////  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
////  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
////  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
////  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
////  THE SOFTWARE.
////   * */
//#endregion
//using System;
//using System.Collections.Generic;
//using System.Collections.Concurrent;
//using System.Threading.Tasks;

//namespace mom
//{
//    /// <summary>
//    ///     Dispatchable session
//    ///     Messages received from peer are dispatched here
//    /// </summary>
//    public abstract class DispatchableSession : Session, IDispatchableSession
//    {
//        private ushort _serial;
//        private readonly ConcurrentDictionary<ushort, Action<bool, byte[]>> _requestPool
//            = new ConcurrentDictionary<ushort, Action<bool, byte[]>>();

//        private Func<byte[], IDataProtocol> _dataParser = data => PiSerializer.Deserialize<DefaultDataProtocol>(data);
//        /// <summary>
//        ///     Get/Set custom data parser
//        /// </summary>
//        protected Func<byte[], IDataProtocol> DataParser
//        {
//            get { return _dataParser; }
//            set { _dataParser = value; }
//        }

//        private static NetPackage PackRequest<T>(T proto) where T : IDataProtocol
//        {
//            return new NetPackage { Type = ERpc.Request, Data = PiSerializer.Serialize(proto) };
//        }

//        private static NetPackage PackPush<T>(T proto) where T : IDataProtocol
//        {
//            return new NetPackage { Type = ERpc.Push, Data = PiSerializer.Serialize(proto) };
//        }

//        public override void Close(SessionCloseReason reason)
//        {
//            base.Close(reason);

//            // 请求失败
//            foreach (var kv in _requestPool)
//            {
//                kv.Value(false, null);
//            }

//            _requestPool.Clear();
//        }

//        /// <summary>
//        /// 多播
//        /// </summary>
//        /// <typeparam name="T"></typeparam>
//        /// <param name="proto"></param>
//        /// <param name="sessions"></param>
//        public void MultiCast<T>(T proto, IEnumerable<ISession> sessions) where T : IDataProtocol
//        {
//            foreach (var session in sessions)
//            {
//                session.InternalSend(PackPush(proto));
//            }
//        }

//        /// <summary>
//        /// 广播
//        /// </summary>
//        /// <typeparam name="T"></typeparam>
//        /// <param name="proto"></param>
//        public void Broadcast<T>(T proto) where T : IDataProtocol
//        {
//            MultiCast(proto, HostPeer.SessionMgr);
//        }
        
//        public abstract Task<NetResult> HandleRequest(IDataProtocol rq);
//        public abstract Task<bool> HandlePush(IDataProtocol ps);
        
//        public async Task<NetResult> RequestAsync<T>(T proto) where T : IDataProtocol
//        {
//            var pack = PackRequest(proto);
//            pack.Serial = ++_serial;

//            return await RequestAsync(pack.Serial, pack);
//        }

//        /// <summary>
//        ///     异步请求（按请求序列号回调结果）
//        /// </summary>
//        /// <param name="serial"></param>
//        /// <param name="pack"></param>
//        /// <returns></returns>
//        private async Task<NetResult> RequestAsync(ushort serial, NetPackage pack)
//        {
//            var tcs = new TaskCompletionSource<NetResult>();

//            //  在逻辑服务线程回调
//            Action<bool, byte[]> cb =
//                (b, bytes) => HostPeer.PerformInLogic(() => tcs.SetResult(new NetResult(b, bytes)));

//            // 回调池
//            if (!_requestPool.TryAdd(serial, cb))
//            {
//                Logger.Ins.Error("Request of serial {0} already processing!", serial);
//                return new NetResult(false, null);
//            }

//            // 发送
//            InternalSend(pack);

//            return await tcs.Task;
//        }

//        public void RequestAsync<T>(T proto, Action<bool, byte[]> cb) where T : IDataProtocol
//        {
//            var pack = PackRequest(proto);
//            pack.Serial = ++_serial;

//            if (!_requestPool.TryAdd(pack.Serial, cb))
//            {
//                Logger.Ins.Error("Request of serial {0} already processing!", pack.Serial);
//                cb(false, null);
//                return;
//            }

//            InternalSend(pack);
//        }

//        public void Push<T>(T proto) where T : IDataProtocol
//        {
//            InternalSend(PackPush(proto));
//        }
        
//        public override async Task Dispatch(byte[] data)
//        {
//            var pack = PiSerializer.Deserialize<NetPackage>(data);

//            switch (pack.Type)
//            {
//                case ERpc.Request:
//                {
//                    try
//                    {
//                        var rq = DataParser(pack.Data);
//                        var rp = await HandleRequest(rq);
//                        if (rp == null)
//                            Response(false, null, pack.Serial);
//                        else
//                            Response(rp.Key, rp.Value, pack.Serial);
//                    }
//                    catch (Exception e)
//                    {
//                        Logger.Ins.Error("Exception {0} : {1} when processing request {2}", e.Message,
//                            e.StackTrace, pack);
//                        Response(false, null, pack.Serial);
//                    }
//                    break;
//                }

//                case ERpc.Response:
//                {
//                    try
//                    {
//                        if (_requestPool.ContainsKey(pack.Serial))
//                        {
//                            var cb = _requestPool[pack.Serial];

//                            Action<bool, byte[]> x;
//                            if (!_requestPool.TryRemove(pack.Serial, out x))
//                                Logger.Ins.Error("Remove response of serial {0} failed", pack.Serial);

//                            cb(pack.Success, pack.Data);
//                        }
//                        else
//                            Logger.Ins.Error("No target for response of serial {0}", pack.Serial);
//                    }
//                    catch (Exception e)
//                    {
//                        Logger.Ins.Error("Exception {0} : {1} when processing response {2}", e.Message,
//                            e.StackTrace, pack);
//                    }
//                    break;
//                }

//                case ERpc.Push:
//                {
//                    try
//                    {
//                        var ps = DataParser(pack.Data);
//                        var success = await HandlePush(ps);
//                        if (!success)
//                            Logger.Ins.Error("Handle push {0} failed!", ps);
//                    }
//                    catch (Exception e)
//                    {
//                        Logger.Ins.Error("Exception {0} : {1} when processing push {2}", e.Message,
//                            e.StackTrace, pack);
//                    }
//                }
//                    break;

//                default:
//                    Logger.Ins.Error("Invalid rpc type : {0} of  route : {1}", pack.Type, pack.Serial);
//                    Close(SessionCloseReason.ClosedByMyself);
//                    break;
//            }
//        }

//        private void Response(bool success, byte[] data, ushort serial)
//        {
//            InternalSend(new NetPackage
//            {
//                Type = ERpc.Response,
//                Serial = serial,
//                Success = success,
//                Data = data,
//            });
//        }
//    }
//}
