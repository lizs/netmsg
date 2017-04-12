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
using System.Net.Sockets;
using System.Threading;

namespace mom {
    /// <summary>
    ///     Session closed reason
    /// </summary>
    public enum SessionCloseReason {
        /// <summary>
        ///     By me
        /// </summary>
        ClosedByMyself,

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
        Write,
        Read,
        SerialConflict,
        NoHandler,
        ReadErrorNo,
        SessionClosed,
        End
    }

    public class Session {
        private enum Pattern {
            Push,
            Request,
            Response,
            Ping,
            Pong
        }

        public ushort Id { get; }
        public string Name => $"{GetType().Name}:{Id}";
        public Socket UnderlineSocket { get; private set; }
        private readonly object _receiveLock = new object();

        public const ushort PackageMaxSize = 4*1024;
        public const ushort ReceiveBufferSize = 8*1024;
        protected const ushort HeaderSize = sizeof(ushort);
        private const byte PatternSize = sizeof(byte);
        private const byte SerialSize = sizeof(ushort);
        private const byte ErrorNoSize = sizeof(ushort);

        // 接收相关
        private SocketAsyncEventArgs _receiveAsyncEventArgs;
        private CircularBuffer _receiveBuffer;
        private Packer _packer;

        // 请求池
        private readonly Dictionary<ushort, Action<ushort, byte[]>> _requests =
            new Dictionary<ushort, Action<ushort, byte[]>>();

        private ushort _serialSeed;
        private volatile int _active;

        public Action<Session, byte[]> PushHandler { get; set; } = null;
        public Action<Session, byte[], Action<ushort, byte[]>> RequestHandler { get; set; } = null;

        public Session(Socket socket, ushort id) {
            UnderlineSocket = socket;
            Id = id;

            _receiveBuffer = new CircularBuffer(ReceiveBufferSize);
            _packer = new Packer(PackageMaxSize);

            _receiveAsyncEventArgs = new SocketAsyncEventArgs();
            _receiveAsyncEventArgs.Completed += OnReceiveCompleted;
        }

        public void Close(SessionCloseReason reason) {
            if(_active == 0) return;
            Interlocked.Exchange(ref _active, 0);

            if (UnderlineSocket.Connected)
                UnderlineSocket.Shutdown(SocketShutdown.Both);

            UnderlineSocket.Close();
            UnderlineSocket = null;

            _receiveAsyncEventArgs.Dispose();
            _receiveAsyncEventArgs = null;
            _receiveBuffer = null;
            _packer = null;

            Logger.Ins.Info("Session closed");
        }

        public void Request(byte[] data, Action<ushort, byte[]> cb) {
            ++_serialSeed;
            if (_requests.ContainsKey(_serialSeed)) {
                cb?.Invoke((ushort) NetError.SerialConflict, null);
                return;
            }

            _requests.Add(_serialSeed, cb);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms)) {
                bw.Write((ushort)((data.IsNullOrEmpty() ? 0 : data.Length) + PatternSize + SerialSize));
                bw.Write((byte) Pattern.Request);
                bw.Write(_serialSeed);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), b => {
                    if (!b) {
                        cb?.Invoke((ushort) NetError.Write, null);
                    }
                });
            }
        }

        public void Push(byte[] data, Action<bool> cb) {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms)) {
                bw.Write((ushort)((data.IsNullOrEmpty() ? 0 : data.Length) + PatternSize));
                bw.Write((byte) Pattern.Push);
                if (!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), cb);
            }
        }

        private void Response(ushort en, ushort serial, byte[] data, Action<bool> cb)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms)) {
                bw.Write((ushort)(PatternSize + ErrorNoSize + SerialSize + (data.IsNullOrEmpty() ? 0 : data.Length)));
                bw.Write((byte)Pattern.Response);
                bw.Write(serial);
                bw.Write(en);
                if(!data.IsNullOrEmpty())
                    bw.Write(data);

                Send(ms.ToArray(), cb);
            }
        }


        private void Send(byte[] data, Action<bool> cb) {
            if (_active == 0) {
                return;
            }

            try {
                var e = SocketAsyncEventArgsPool.Instance.Pop();
                e.Completed += OnSendCompleted;
                e.SetBuffer(data, 0, data.Length);
                e.UserToken = cb;

                if (UnderlineSocket.SendAsync(e))
                    return;

                if (e.SocketError != SocketError.Success) {
                    cb?.Invoke(false);
                    Close(SessionCloseReason.WriteError);
                }

                e.Completed -= OnSendCompleted;
                SocketAsyncEventArgsPool.Instance.Push(e);
            }
            catch (ObjectDisposedException e) {
                Logger.Ins.Warn("Socket already closed! detail : {0}", e.StackTrace);
            }
            catch (Exception e){
                Logger.Ins.Exception("Send", e);
                Close(SessionCloseReason.WriteError);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e) {
            (e.UserToken as Action<bool>)?.Invoke(e.SocketError == SocketError.Success);

            if (e.SocketError != SocketError.Success) {
                Close(SessionCloseReason.WriteError);
            }

            e.Completed -= OnSendCompleted;
            SocketAsyncEventArgsPool.Instance.Push(e);
        }

        private void WakeupReceive() {
            ReceiveNext();
        }

        public void Start()
        {
            Interlocked.Exchange(ref _active, 1);
            // 投递首次接受请求
            WakeupReceive();
        }

        private void ProcessReceive() {
            if (_receiveAsyncEventArgs.SocketError != SocketError.Success) {
                Close(SessionCloseReason.ReadError);
                return;
            }

            if (_receiveAsyncEventArgs.BytesTransferred == 0) {
                Close(SessionCloseReason.ClosedByRemotePeer);
                return;
            }

            _receiveBuffer.MoveByWrite((ushort) _receiveAsyncEventArgs.BytesTransferred);
            ushort packagesCnt = 0;
            if (_packer.Process(_receiveBuffer, ref packagesCnt) == PackerError.Failed) {
                Close(SessionCloseReason.PackError);
                return;
            }

            if (_receiveBuffer.Overload)
                _receiveBuffer.Arrange();

            while (_packer.Packages.Count > 0) {
                try {
                    Dispatch();
                }
                catch {
                    Close(SessionCloseReason.ReadError);
                }
            }

            ReceiveNext();
        }

        private void Pong() {
            
        }

        private void OnResponse(ushort serial, byte[] data) {
            
        }

        private void Dispatch() {
            var pack = _packer.Packages.Dequeue();

            var left = pack.Length;
            using (var ms = new MemoryStream(pack))
            using (var br = new BinaryReader(ms)) {
                var pattern = (Pattern) br.ReadByte();
                left -= PatternSize;
                switch (pattern) {
                    case Pattern.Push:
                        PushHandler?.Invoke(this, br.ReadBytes(left));
                        break;

                    case Pattern.Request: {
                        var serial = br.ReadUInt16();
                        left -= SerialSize;
                        if (RequestHandler != null) {
                            RequestHandler(this, br.ReadBytes(left),
                                (en, bytes) => { Response(en, serial, bytes, null); });
                        }
                        else {
                            Response((ushort) NetError.NoHandler, serial, null, null);
                        }
                        break;
                    }

                    case Pattern.Response: {
                        var serial = br.ReadUInt16();
                        left -= SerialSize;
                        OnResponse(serial, br.ReadBytes(left));
                        break;
                    }

                    case Pattern.Ping:
                        //m_lastPingTime = time(nullptr);
                        Pong();
                        break;

                    case Pattern.Pong:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void ReceiveNext() {
            _receiveAsyncEventArgs.SetBuffer(_receiveBuffer.Buffer, _receiveBuffer.Tail, _receiveBuffer.WritableSize);
            _receiveAsyncEventArgs.UserToken = _receiveBuffer;
            if (!UnderlineSocket.ReceiveAsync(_receiveAsyncEventArgs)) {
                ProcessReceive();
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e) {
            if (e.SocketError != SocketError.Success) {
                Close(SessionCloseReason.ReadError);
                return;
            }

            lock (_receiveLock) {
                ProcessReceive();
            }
        }
    }
}
