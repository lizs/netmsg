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
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        End
    }

    public class Result
    {
        public ushort Err { get; }
        public byte[] Data { get; }

        public Result(ushort err, byte[] data)
        {
            Err = err;
            Data = data;
        }

        public Result(ushort err)
        {
            Err = err;
            Data = null;
        }

        public static implicit operator Result(ushort error)
        {
            return new Result(error, null);
        }

        public static implicit operator bool(Result ret)
        {
            return ret?.Err == 0;
        }

        public override string ToString()
        {
            return $"Error : {Err} Data : {Data?.Length}";
        }
    }

    public sealed class Session
    {
        private enum Pattern
        {
            Push,
            Request,
            Response,
            Ping,
            Pong
        }

        public ushort Id { get; }
        public string Name => $"{GetType().Name}:{Id}";

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

        public const ushort PackageMaxSize = 1*1024;
        public const ushort ReceiveBufferSize = 1*1024;
        public const ushort HeaderSize = sizeof(ushort);
        private const byte PatternSize = sizeof(byte);
        private const byte SerialSize = sizeof(ushort);
        private const byte ErrorNoSize = sizeof(ushort);

        // 接收相关
        private SocketAsyncEventArgs _receiveAsyncEventArgs;
        private CircularBuffer _receiveBuffer;
        private Packer _packer;

        // 请求池
        private readonly Dictionary<ushort, Action<Result>> _requests =
            new Dictionary<ushort, Action<Result>>();

        private ushort _serialSeed;
        private readonly IDispatcher _dispatcher;
        private readonly List<byte[]> _slices = new List<byte[]>();

        private int _closeFlag = 1;

        public Session(Socket socket, ushort id, IDispatcher dispatcher = null)
        {
            UnderlineSocket = socket;
            Id = id;

            _dispatcher = dispatcher ?? new DefaultDispatcher();

            _receiveBuffer = new CircularBuffer(ReceiveBufferSize);
            _packer = new Packer(PackageMaxSize);

            _receiveAsyncEventArgs = new SocketAsyncEventArgs();
            _receiveAsyncEventArgs.Completed += OnReceiveCompleted;
        }

        public void Close(SessionCloseReason reason)
        {
            if(Interlocked.CompareExchange(ref _closeFlag, 1, 0) == 1)
                return;
            
            if (UnderlineSocket.Connected)
                UnderlineSocket.Shutdown(SocketShutdown.Both);
            
            UnderlineSocket.Close();
            UnderlineSocket.Dispose();
            UnderlineSocket = null;

            _receiveAsyncEventArgs.Dispose();
            _receiveAsyncEventArgs = null;

            _receiveBuffer = null;
            _packer = null;

            Loop.Ins.Perform(() => { _dispatcher.OnClose(this, reason); });
        }

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
        ///     广播
        /// </summary>
        /// <param name="sessions"></param>
        /// <param name="data"></param>
        /// <param name="cb"></param>
        public static void Broadcast(Session[] sessions, byte[] data)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Push);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                var bytes = ms.ToArray();
                foreach (var session in sessions)
                {
                    session.Send(bytes, null);
                }
            }
        }

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

        private void Pong()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte) Pattern.Pong);

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
                    bw.Seek(sizeof(byte) + HeaderSize, SeekOrigin.Begin);
                    bw.Write(data, i*limit, packLen);
                    bw.Seek(0, SeekOrigin.Begin);
                    bw.Write((ushort) (packLen + sizeof(byte)));
                    bw.Write((byte) (cnt - i));
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
            Loop.Ins.Perform(() => { (e.UserToken as Action<bool>)?.Invoke(e.SocketError == SocketError.Success); });

            e.Completed -= OnSendCompleted;
            SocketAsyncEventArgsPool.Ins.Push(e);
            Monitor.Ins.IncWroted();

            if (e.SocketError != SocketError.Success)
            {
                Close(SessionCloseReason.WriteError);
            }
        }

        private void WakeupReceive()
        {
            ReceiveNext();
        }

        public void Start()
        {
            Interlocked.Exchange(ref _closeFlag, 0);

            // 投递首次接受请求
            WakeupReceive();
            _dispatcher.OnOpen(this);
        }

        private void ProcessReceive()
        {
            if(_closeFlag == 1)
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
            if (_packer.Process(_receiveBuffer, ref packagesCnt) == PackerError.Failed)
            {
                Close(SessionCloseReason.PackError);
                return;
            }

            if (_receiveBuffer.Overload)
                _receiveBuffer.Arrange();

            // 包处理放在Loop线程
            // 因为Timer也是在Loop线程
            Loop.Ins.Perform(() =>
            {
                while (_packer?.Packages.Count > 0)
                {
                    try
                    {
                        Dispatch();
                    }
                    catch(Exception e)
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
            var pack = _packer.Packages.Dequeue();

            using (var ms = new MemoryStream(pack))
            using (var br = new BinaryReader(ms))
            {
                var cnt = br.ReadByte();
                if (cnt < 1)
                    return;

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

        private async Task<bool> Dispatch(byte[] message)
        {
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
                            var ret = await _dispatcher.OnRequest(this, br.ReadBytes(left));
                            Response(ret.Err, serial, ret.Data, null);
                        }
                        catch
                        {
                            Response((ushort) NetError.ExceptionCatched, serial, null, null);
                            throw;
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
                        //m_lastPingTime = time(nullptr);
                        Pong();
                        break;

                    case Pattern.Pong:
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
    }
}
