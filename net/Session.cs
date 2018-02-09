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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
#if NET35
#else
using System.Threading.Tasks;

#endif

namespace mom
{
    /// <summary>
    ///     Session closed reason
    /// </summary>
    public enum SessionCloseReason
    {
        /// <summary>
        ///     By me
        /// </summary>
        Stop,

        /// <summary>
        ///     By peer
        /// </summary>
        ClosedByRemotePeer,

        /// <summary>
        ///     Read error
        /// </summary>
        ReadError,

        /// <summary>
        ///     Write error
        /// </summary>
        WriteError,

        /// <summary>
        ///     Pack error
        ///     That's mean data can't packaged by DataParser or data is much too huge
        /// </summary>
        PackError,

        /// <summary>
        ///     Replaced
        /// </summary>
        Replaced
    }

    public enum NetError
    {
        Success = 0,
        ExceptionCatched,
        Write,
        Read,
        RequestDataIsEmpty,
        SerialConflict,
        NoHandler,
        ReadErrorNo,
        SessionClosed,
        SubOverflow,
        End
    }

    /// <summary>
    ///     RPC结果
    /// </summary>
    public class Result
    {
        public Result(ushort err, byte[] data, Action<object> cb, object callbackData) : this(err, data, cb)
        {
            CallbackData = callbackData;
        }

        public Result(ushort err, byte[] data, Action<object> cb) : this(err, data)
        {
            Callback = cb;
        }

        public Result(ushort err, byte[] data) : this(err)
        {
            Data = data;
        }

        public Result(ushort err, Action<object> cb, object callbackData) : this(err, cb)
        {
            CallbackData = callbackData;
        }

        public Result(ushort err, Action<object> cb) : this(err)
        {
            Callback = cb;
        }

        public Result(ushort err)
        {
            Err = err;
            Data = null;
            Callback = null;
            CallbackData = null;
        }

        /// <summary>
        ///     错误码
        /// </summary>
        public ushort Err { get; }

        /// <summary>
        ///     数据
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        ///     Response回调
        /// </summary>
        public Action<object> Callback { get; }

        /// <summary>
        ///     回调数据
        ///     不做修改，原本返回
        /// </summary>
        public object CallbackData { get; set; }

        /// <summary>
        ///     错误码隐式转换成Result
        /// </summary>
        /// <param name="error"></param>
        public static implicit operator Result(ushort error)
        {
            return new Result(error);
        }

        /// <summary>
        ///     错误码隐式转换成Result
        /// </summary>
        /// <param name="error"></param>
        public static implicit operator Result(NetError error)
        {
            return (ushort) error;
        }

        /// <summary>
        ///     Result隐式转换成bool
        /// </summary>
        /// <param name="ret"></param>
        public static implicit operator bool(Result ret)
        {
            return ret?.Err == 0;
        }
    }

    /// <summary>
    ///     会话
    /// </summary>
    public sealed class Session
    {
        public const ushort PackageMaxSize = 1*1024;
        public const ushort ReceiveBufferSize = 1*1024;
        public const ushort HeaderSize = sizeof(ushort);
        private const byte PatternSize = sizeof(byte);
        private const byte SerialSize = sizeof(ushort);
        private const byte ErrorNoSize = sizeof(ushort);
        private readonly IDispatcher _dispatcher;

        // 请求池
        private readonly Dictionary<ushort, Action<Result>> _requests =
            new Dictionary<ushort, Action<Result>>();

        // ping池
        private struct PingInfo
        {
            public long PingTime { get; private set; }
            public Action<int> Callback { get; private set; }

            public PingInfo(long time, Action<int> cb)
            {
                PingTime = time;
                Callback = cb;
            }
        }

        private readonly Dictionary<byte, PingInfo> _pings =
            new Dictionary<byte, PingInfo>();

        private readonly List<byte[]> _slices = new List<byte[]>();
        private readonly Stopwatch _watch = new Stopwatch();

        private int _closeFlag = 1;
        private Extractor _extractor;
        private long _lastResponseTime;

        // 接收相关
        private readonly SocketAsyncEventArgs _receiveAsyncEventArgs;
        private CircularBuffer _receiveBuffer;
        private ushort _serialSeed;
        private byte _pingSerialSeed;

        public Session(Socket socket, ushort id, IDispatcher dispatcher = null)
        {
            _watch.Start();
            UnderlineSocket = socket;
            Id = id;

            _dispatcher = dispatcher ?? new DefaultDispatcher();

            _receiveBuffer = new CircularBuffer(ReceiveBufferSize);
            _extractor = new Extractor(PackageMaxSize);

            _receiveAsyncEventArgs = new SocketAsyncEventArgs();
            _receiveAsyncEventArgs.Completed += OnReceiveCompleted;
        }

        public ushort Id { get; }
        public string Name => $"{GetType().Name}:{Id}";

        /// <summary>
        ///     对端IP
        /// </summary>
        public string RemoteIp
        {
            get
            {
                var ip = string.Empty;
                var address = UnderlineSocket?.RemoteEndPoint.ToString();
                var items = address?.Split(new[] {':'}, StringSplitOptions.RemoveEmptyEntries);
                if (items != null && items.Length == 2)
                {
                    ip = items[0];
                }

                return ip;
            }
        }

        public Socket UnderlineSocket { get; private set; }
        public object UserData { get; set; }
        public long ElapsedSinceLastResponse => _watch.ElapsedMilliseconds - _lastResponseTime;

        /// <summary>
        ///     KeepAlive计数
        /// </summary>
        public int KeepAliveCounter { get; private set; }

        /// <summary>
        ///     启动会话
        /// </summary>
        public void Start()
        {
            Interlocked.Exchange(ref _closeFlag, 0);

            // 投递首次接受请求
            WakeupReceive();

            // 回调Dispatcher
            _dispatcher.OnOpen(this);
        }

        /// <summary>
        ///     关闭会话
        /// </summary>
        /// <param name="reason"></param>
        public void Close(SessionCloseReason reason)
        {
            if (Interlocked.CompareExchange(ref _closeFlag, 1, 0) == 1)
                return;

            if (UnderlineSocket.Connected)
                UnderlineSocket.Shutdown(SocketShutdown.Both);

            UnderlineSocket.Close();
            UnderlineSocket = null;

            //_receiveAsyncEventArgs.Dispose();
            //_receiveAsyncEventArgs = null;

            _receiveBuffer = null;
            _extractor = null;

            Loop.Ins.Perform(() => { _dispatcher.OnClose(this, reason); });
        }

#if NET35
#else
        /// <summary>
        ///     awaitable Request
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<Result> Request(byte[] data)
        {
            var tcs = new TaskCompletionSource<Result>();
            try
            {
                Request(data, ret =>
                {
                    if (!tcs.TrySetResult(ret))
                    {
                        Logger.Ins.Error("TrySetResult failed");
                    }
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return await tcs.Task;
        }
#endif

        /// <summary>
        ///     请求
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cb"></param>
        public void Request(byte[] data, Action<Result> cb)
        {
            if (data.IsNullOrEmpty())
            {
                cb?.Invoke(new Result((ushort) NetError.RequestDataIsEmpty));
                return;
            }

            ++_serialSeed;
            if (_requests.ContainsKey(_serialSeed))
            {
                cb?.Invoke(new Result((ushort) NetError.SerialConflict));
                return;
            }

            _requests.Add(_serialSeed, cb);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Request);
                bw.Write(_serialSeed);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), b =>
                {
                    if (!b)
                    {
                        cb?.Invoke(new Result((ushort) NetError.Write));
                    }
                });
            }
        }

        /// <summary>
        ///     推送
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cb"></param>
        public void Push(byte[] data, Action<bool> cb = null)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Push);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), cb);
            }
        }

        private void Response(ushort en, ushort serial, byte[] data, Action<bool> cb)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Response);
                bw.Write(serial);
                bw.Write(en);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), cb);
            }
        }

        private void Pong(byte serial)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Pong);
                bw.Write(serial);
                Send(ms.ToArray(), null);
            }
        }

        public void Ping(Action<int> cb = null)
        {
            var serial = ++_pingSerialSeed;
            if (_pings.ContainsKey(serial))
                return;

            _pings[serial] = new PingInfo(_watch.ElapsedMilliseconds, cb);

            ++KeepAliveCounter;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Ping);
                bw.Write(serial);
                Send(ms.ToArray(), b =>
                {
                    if (b) return;

                    _pings.Remove(serial);
                    cb?.Invoke(int.MaxValue);
                });
            }
        }

        public void Sub(string subject)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Sub);
                bw.Write(Encoding.ASCII.GetBytes(subject));
                Send(ms.ToArray(), null);
            }
        }

        public void Unsub(string subject)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Unsub);
                bw.Write(Encoding.ASCII.GetBytes(subject));
                Send(ms.ToArray(), null);
            }
        }

        public void Pub(string subject, byte[] data)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)Pattern.Pub);
                bw.Write(Encoding.ASCII.GetBytes(subject));
                bw.Write((byte)0);
                bw.Write(data);
                Send(ms.ToArray(), null);
            }
        }

        private void Send(byte[] data, Action<bool> cb)
        {
            if (_closeFlag == 1 || data.IsNullOrEmpty())
            {
                cb?.Invoke(false);
                return;
            }

            var buffers = new List<ArraySegment<byte>>();
            const int limit = PackageMaxSize - sizeof(byte);

            var cnt = data.Length/limit;
            var lastLen = data.Length%limit;
            if (lastLen != 0)
                ++cnt;

            for (var i = 0; i < cnt; ++i)
            {
                var packLen = limit;
                if (i == cnt - 1 && lastLen != 0)
                    packLen = lastLen;

                var slice = new byte[packLen + sizeof(byte) + HeaderSize];
                using (var ms = new MemoryStream(slice))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write((ushort) (packLen + sizeof(byte)));
                    bw.Write((byte) (cnt - i));
                    bw.Write(data, i*limit, packLen);
                }

                buffers.Add(new ArraySegment<byte>(slice));
            }

            try
            {
                var e = SocketAsyncEventArgsPool.Ins.Pop();
                e.Completed += OnSendCompleted;
                e.BufferList = buffers;
                e.UserToken = cb;

#if MESSAGE_TRACK_ENABLED
                var sb = new StringBuilder();
                for (var i = 0; i < buffers.Count; ++i)
                {
                    var buf = buffers[i];
                    sb.Append("Write [ ");
                    foreach (var b in buf)
                    {
                        sb.Append($"{b.ToString("X2")} ");
                    }
                    sb.Append("]");

                    if (i != buffers.Count - 1)
                    {
                        sb.AppendLine();
                    }
                }

                Logger.Ins.Debug(sb.ToString());
#endif

                if (UnderlineSocket.SendAsync(e))
                    return;

                if (e.SocketError != SocketError.Success)
                {
                    cb?.Invoke(false);
                    Close(SessionCloseReason.WriteError);
                }

                e.Completed -= OnSendCompleted;
                SocketAsyncEventArgsPool.Ins.Push(e);
            }
            catch (ObjectDisposedException e)
            {
                Logger.Ins.Warn("Socket already closed! detail : {0}", e.StackTrace);
            }
            catch (Exception e)
            {
                Logger.Ins.Exception("Send", e);
                Close(SessionCloseReason.WriteError);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            // 在Loop线程回调
            Loop.Ins.Perform(() =>
            {
                (e.UserToken as Action<bool>)?.Invoke(e.SocketError == SocketError.Success);

                e.Completed -= OnSendCompleted;
                SocketAsyncEventArgsPool.Ins.Push(e);
            });

            Monitor.Ins.IncWroted();

            if (e.SocketError != SocketError.Success)
            {
                Logger.Ins.Warn(e.SocketError);
                Close(SessionCloseReason.WriteError);
            }
        }

        private void WakeupReceive()
        {
            ReceiveNext();
        }

        private void ProcessReceive()
        {
            if (_closeFlag == 1)
                return;

            if (_receiveAsyncEventArgs.SocketError != SocketError.Success)
            {
                Close(SessionCloseReason.ReadError);
                return;
            }

            if (_receiveAsyncEventArgs.BytesTransferred == 0)
            {
                Close(SessionCloseReason.ClosedByRemotePeer);
                return;
            }

            _receiveBuffer.MoveByWrite((ushort) _receiveAsyncEventArgs.BytesTransferred);
            ushort packagesCnt = 0;
            if (_extractor.Process(_receiveBuffer, ref packagesCnt) == PackerError.Failed)
            {
                Close(SessionCloseReason.PackError);
                return;
            }

            if (_receiveBuffer.Overload)
                _receiveBuffer.Arrange();

            // 包处理放在Loop线程
            // Timer也在Loop线程
            Loop.Ins.Perform(() =>
            {
                while (_extractor?.Packages.Count > 0)
                {
                    try
                    {
                        Dispatch();
                    }
                    catch (Exception e)
                    {
                        Logger.Ins.Exception("Dispatch", e);
                        Close(SessionCloseReason.ReadError);
                    }
                }

                ReceiveNext();
            });
        }

        private void OnResponse(ushort serial, ushort err, byte[] data)
        {
            if (!_requests.ContainsKey(serial))
            {
                Logger.Ins.Warn($"{serial} not exist in request pool");
                return;
            }

            var cb = _requests[serial];
            _requests.Remove(serial);

            cb?.Invoke(new Result(err, data));
        }

        private void Dispatch()
        {
            var pack = _extractor.Packages.Dequeue();

            using (var ms = new MemoryStream(pack))
            using (var br = new BinaryReader(ms))
            {
                var cnt = br.ReadByte();
                if (cnt < 1)
                {
                    Close(SessionCloseReason.PackError);
                    _slices.Clear();
                    return;
                }

                _slices.Add(br.ReadBytes(pack.Length - sizeof(byte)));
                if (cnt == 1)
                {
                    // 组包
                    var totalLen = _slices.Sum(slice => slice.Length);
                    var offset = 0;
                    var message = new byte[totalLen];
                    foreach (var slice in _slices)
                    {
                        Buffer.BlockCopy(slice, 0, message, offset, slice.Length);
                        offset += slice.Length;
                    }

                    try
                    {
#pragma warning disable 4014
                        Dispatch(message);
#pragma warning restore 4014
                    }
                    catch (Exception e)
                    {
                        Logger.Ins.Exception("Dispatch", e);
                    }
                    finally
                    {
                        _slices.Clear();
                    }
                }
            }

            Monitor.Ins.IncReaded();
        }

#if NET35
        private bool Dispatch(byte[] message)
#else
        private async Task<bool> Dispatch(byte[] message)
#endif
        {
            _lastResponseTime = _watch.ElapsedMilliseconds;
            var left = message.Length;
            using (var ms = new MemoryStream(message))
            using (var br = new BinaryReader(ms))
            {
                var pattern = (Pattern) br.ReadByte();
                left -= PatternSize;
                switch (pattern)
                {
                    case Pattern.Push:
                        _dispatcher.OnPush(this, br.ReadBytes(left));
                        break;

                    case Pattern.Request:
                    {
                        var serial = br.ReadUInt16();
                        left -= SerialSize;
                        try
                        {
#if NET35
                                _dispatcher.OnRequest(this, br.ReadBytes(left), ret =>
                                {
                                    Response(ret.Err, serial, ret.Data, null);
                                    ret.Callback?.Invoke(ret.CallbackData);
                                });
#else
                            var ret = await _dispatcher.OnRequest(this, br.ReadBytes(left));
                            Response(ret.Err, serial, ret.Data, null);
                            ret.Callback?.Invoke(ret.CallbackData);
#endif
                        }
                        catch (Exception e)
                        {
                            Response((ushort) NetError.ExceptionCatched, serial, null, null);
                            Logger.Ins.Exception("Pattern.Request", e);
                        }
                        break;
                    }

                    case Pattern.Response:
                    {
                        var serial = br.ReadUInt16();
                        left -= SerialSize;
                        var err = br.ReadUInt16();
                        left -= ErrorNoSize;
                        OnResponse(serial, err, br.ReadBytes(left));
                        break;
                    }

                    case Pattern.Ping:
                    {
                        var serial = br.ReadByte();
                        Pong(serial);
                        break;
                    }

                    case Pattern.Pong:
                    {
                        var serial = br.ReadByte();
                        if (_pings.ContainsKey(serial))
                        {
                            var info = _pings[serial];
                            info.Callback?.Invoke((int) (_watch.ElapsedMilliseconds - info.PingTime));
                            _pings.Remove(serial);
                        }

                        if (KeepAliveCounter > 0)
                            --KeepAliveCounter;

                        break;
                    }

                    case Pattern.Sub:
                        break;

                    case Pattern.Unsub:
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private void ReceiveNext()
        {
            if (_closeFlag == 1)
            {
                return;
            }

            _receiveAsyncEventArgs.SetBuffer(_receiveBuffer.Buffer, _receiveBuffer.Tail, _receiveBuffer.WritableSize);
            _receiveAsyncEventArgs.UserToken = _receiveBuffer;
            if (!UnderlineSocket.ReceiveAsync(_receiveAsyncEventArgs))
            {
                ProcessReceive();
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                Close(SessionCloseReason.ReadError);
                return;
            }

            ProcessReceive();
        }

        /// <summary>
        ///     消息模型
        /// </summary>
        private enum Pattern : byte
        {
            Push,
            Request,
            Response,
            Ping,
            Pong,
            Sub,
            Unsub,
            Pub
        }
    }
}