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
    public sealed class Server {
        private const int DefaultBacktrace = 10;
        private ushort _sessionIdSeed;
        public int Port { get; }
        public string Ip { get; }
        public IPAddress Address { get; }
        public EndPoint EndPoint { get; }

        public string Name => $"{Ip}:{Port}";

        public SessionMgr SessionMgr { get; }

        private Socket _listener;
        private SocketAsyncEventArgs _acceptEvent;

        public Action<Session, byte[]> PushHandler { get; set; } = null;
        public Action<Session, byte[], Action<ushort, byte[]>> RequestHandler { get; set; } = null;
        public Action<Session, SessionCloseReason> CloseHandler { get; set; } = null;
        public Action<Session> OpenHandler { get; set; } = null;

        public Server(string ip, int port) {
            Ip = ip;
            Port = port;

            IPAddress address;
            if (!IPAddress.TryParse(Ip, out address))
                Logger.Ins.Fatal("Invalid ip {0}", Ip);

            Address = address;
            EndPoint = new IPEndPoint(Address, Port);

            SessionMgr = new SessionMgr();
        }
        
        private void OnError(string msg) {
            Logger.Ins.Error("{0}:{1}", Name, msg);
        }

        public void Start() {
            try {
                _listener = SocketExt.CreateTcpSocket();
                _listener.Bind(EndPoint);
                _listener.Listen(DefaultBacktrace);
            }
            catch (Exception e) {
                OnError($"Server start failed, detail {e.Message} : {e.StackTrace}");
                return;
            }

            _acceptEvent = new SocketAsyncEventArgs();
            _acceptEvent.Completed += OnAcceptCompleted;

            AcceptNext();
            Logger.Ins.Debug("Server started on {0}:{1}", Ip, Port);
        }

        public void Stop() {
            SessionMgr.Stop();

            _listener.Close();
            _acceptEvent?.Dispose();
            
            Logger.Ins.Info("Server stopped!");
        }

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs e) {
            ProcessAccept(e.AcceptSocket, e.SocketError);
        }

        private void ProcessAccept(Socket sock, SocketError error) {
            if (error != SocketError.Success) {
                Logger.Ins.Error("Listener down!");
                Stop();
            }
            else {
                var session = new Session(sock, ++_sessionIdSeed) {
                    OpenHandler = s => {
                        Logger.Ins.Info("{0}:{1} connected!", Name, s.Name);
                        OpenHandler?.Invoke(s);
                    },
                    CloseHandler = (s, reason) => {
                        Logger.Ins.Info("{0}:{1} disconnected by {2}", Name, s.Name, reason);
                        CloseHandler?.Invoke(s, reason);
                    },
                    PushHandler = PushHandler,
                    RequestHandler = RequestHandler
                };

                if (SessionMgr.AddSession(session)) {
                    session.Start();
                }

                AcceptNext();
            }
        }

        private void AcceptNext() {
            _acceptEvent.AcceptSocket = null;

            try {
                if (!_listener.AcceptAsync(_acceptEvent)) {
                    ProcessAccept(_acceptEvent.AcceptSocket, _acceptEvent.SocketError);
                }
            }
            catch (Exception e) {
                var msg = $"Accept failed, detail {e.Message} : {e.StackTrace}";
                OnError(msg);
                AcceptNext();
            }
        }
    }
}
