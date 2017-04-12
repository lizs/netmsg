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
using System.Net;
using System.Net.Sockets;

namespace mom {
    /// <summary>
    /// </summary>
    public class Client {
        /// <summary>
        ///     Default reconnect retry delay time
        /// </summary>
        public const uint ReconnectDelay = 1000;

        /// <summary>
        ///     Default reconnect retry max delay time
        /// </summary>
        public const uint ReconnectMaxDelay = 32*1000;

        /// <summary>
        ///     Raised when a session closed
        /// </summary>
        public event Action<Session, SessionCloseReason> EventClosed;

        /// <summary>
        ///     Raised when a session established
        /// </summary>
        public event Action<Session> EventConnected;
        
        /// <summary>
        ///     Raised when error catched
        /// </summary>
        public event Action<string> EventErrorCatched;

        public string Name => $"{Ip}:{Port}";

        public string Ip { get; private set; }
        public int Port { get; private set; }
        public IPAddress Address { get; private set; }
        public EndPoint EndPoint { get; private set; }

        public bool Connected {
            get { return _connected; }
            private set {
                _connected = value;
                ReconnectRetryDelay = _connected
                    ? ReconnectDelay
                    : Math.Min(ReconnectMaxDelay, ReconnectRetryDelay*2);
            }
        }

        /// <summary>
        ///     是否在断开连接之后自动重连
        /// </summary>
        public bool AutoReconnectEnabled { get; set; }

        /// <summary>
        ///     重连重试延时
        /// </summary>
        private uint ReconnectRetryDelay { get; set; } = ReconnectDelay;

        private Socket _underlineSocket;
        public Session Session { get; private set; }
        private SocketAsyncEventArgs _connectEvent;
        private bool _connected;
        private Scheduler _scheduler = new Scheduler();

        public Action<Session, byte[]> PushHandler { get; set; } = null;
        public Action<Session, byte[], Action<ushort, byte[]>> RequestHandler { get; set; } = null;

        public Client(string ip, int port,  bool autoReconnectEnabled = true) {
            AutoReconnectEnabled = autoReconnectEnabled;

            if (string.IsNullOrEmpty(ip) || port == 0)
                Logger.Ins.Warn("Ip or Port is invalid!");
            else if (!SetAddress(ip, port))
                throw new Exception("Ip or Port is invalid!");
        }

        protected virtual void OnConnected(Session session) {
            Connected = true;
            Logger.Ins.Info("{0}:{1} connected!", Name, session.Name);
        }

        protected virtual void OnDisconnected(Session session, SessionCloseReason reason) {
            Connected = false;
            Logger.Ins.Info("{0}:{1} disconnected by {2}", Name, session.Name, reason);

            if (AutoReconnectEnabled) {
                // todo
                // Invoke(Reconnect, ReconnectRetryDelay);
            }
        }

        protected virtual void OnError(string msg) {
            Connected = false;
            Logger.Ins.Error("{0}:{1}", Name, msg);

            if (AutoReconnectEnabled) {
                // todo
                // Invoke(Reconnect, ReconnectRetryDelay);
            }
        }

        public bool SetAddress(string ip, int port) {
            Ip = ip;
            Port = port;

            try {
                Address = IPAddress.Parse(Ip);
                EndPoint = new IPEndPoint(Address, Port);
            }
            catch (Exception e) {
                var msg = $"{e.Message} : {e.StackTrace}";
                OnError(msg);

                EventErrorCatched?.Invoke(msg);

                Ip = string.Empty;
                Port = 0;
                return false;
            }

            return true;
        }

        public void Start() {
            if (string.IsNullOrEmpty(Ip) || Port == 0)
                throw new Exception("Address must be setted before start!");

            _connectEvent = new SocketAsyncEventArgs();
            _connectEvent.Completed += OnConnectCompleted;

            _underlineSocket = SocketExt.CreateTcpSocket();
            Connect();
        }

        public void Stop() {
            EventClosed = null;
            EventConnected = null;

            _connectEvent.Dispose();
            _underlineSocket.Close();

            EventClosed?.Invoke(Session, SessionCloseReason.ClosedByMyself);
            Session.Close(SessionCloseReason.ClosedByMyself);
            
            Logger.Ins.Debug("Client stopped!");
        }


        public void Reconnect() {
            _underlineSocket = SocketExt.CreateTcpSocket();
            Connect();
        }

        public void Connect() {
            _connectEvent.RemoteEndPoint = EndPoint;
            try {
                if (!_underlineSocket.ConnectAsync(_connectEvent))
                    HandleConnection(_underlineSocket);
            }
            catch (Exception e) {
                var msg = $"Connection failed, detail {e.Message} : {e.StackTrace}";
                OnError(msg);

                EventErrorCatched?.Invoke(msg);
            }
        }

        private void HandleConnection(Socket sock) {
            Session = new Session(sock, 0) {
                RequestHandler = RequestHandler,
                PushHandler = PushHandler
            };
            EventConnected?.Invoke(Session);
        }

        private void OnConnectCompleted(object sender, SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success)
                HandleConnection(_underlineSocket);
            else {
                var msg = $"Connection failed, detail {e.SocketError}";
                OnError(msg);
                EventErrorCatched?.Invoke(msg);
            }
        }
    }
}
