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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace mom
{
    /// <summary>
    /// </summary>
    public sealed class Client
    {
        public string Name => $"{Host}:{Port}";

        public string Host { get; }
        public int Port { get; }

        public IPAddress Address { get; private set; }
        public EndPoint EndPoint { get; private set; }

        private readonly IDispatcher _dispatcher;

        private Socket _underlineSocket;
        public Session Session { get; private set; }
        private SocketAsyncEventArgs _connectEvent;
        private readonly Scheduler _scheduler = new Scheduler();

        public uint ReconnectDelay { get; set; } = 2*1000;
        public uint KeepAliveInterval { get; set; } = 10*1000; // ms
        public uint KeepAliveCountDeadLine { get; set; } = 5;
        public bool AutoReconnectEnabled { get; set; } = true;

        private bool _stopped;

        public Client(string host, int port, IDispatcher dispatcher = null)
        {
            _dispatcher = new InternalDispatcher(dispatcher ?? new DefaultDispatcher(), reason =>
            {
                if (!_stopped && AutoReconnectEnabled)
                {
                    _reconnect();
                }

                _scheduler.Cancel(_keepAlive);
            });

            Host = host;
            Port = port;

            Logger.Ins.Debug($"TcpClient : {Host}:{port}");
        }

        private void _keepAlive()
        {
            if(Session == null) return;

            if (Session.KeepAliveCounter > KeepAliveCountDeadLine)
            {
                Session.Close(SessionCloseReason.ClosedByRemotePeer);
            }
            else if (Session.ElapsedSinceLastResponse > KeepAliveInterval)
            {
                Session.Ping();
            }
        }

        private void _reconnect()
        {
            _scheduler.Invoke(Reconnect, ReconnectDelay);
        }

        private void OnError(string msg)
        {
            Logger.Ins.Error(msg);
            _dispatcher.OnError(msg);

            if (AutoReconnectEnabled)
            {
                _reconnect();
            }
        }

        private bool ParseAddress()
        {
            try
            {
                IPAddress addr;
                if (!IPAddress.TryParse(Host, out addr))
                {
                    // get ip by host
                    var entry = Dns.GetHostEntry(Host);
                    if (entry.AddressList.IsNullOrEmpty())
                    {
                        return false;
                    }

                    // 优先使用ipv4
                    // 纯ipv6环境下才会使用ipv6
                    addr = entry.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ??
                           entry.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetworkV6);
                }

                Address = addr;
                EndPoint = new IPEndPoint(Address, Port);

                Logger.Ins.Debug($"ParseAddress : {Address.AddressFamily} : {Address}");
                return true;
            }
            catch (Exception e)
            {
                Logger.Ins.Exception("ParseAddress", e);
                return false;
            }
        }

        public void Start()
        {
            if (!ParseAddress())
            {
                Loop.Ins.Perform(() => { OnError($"Parse address {Host}:{Port} failed."); });
                return;
            }

            if (string.IsNullOrEmpty(Host) || Port == 0)
            {
                throw new Exception("Address must be setted before start!");
            }

            _connectEvent = new SocketAsyncEventArgs();
            _connectEvent.Completed += OnConnectCompleted;

            _underlineSocket = SocketExt.CreateTcpSocket(Address.AddressFamily);
            Connect();

            _stopped = false;
        }

        public void Stop()
        {
            if (_stopped)
                return;

            _stopped = true;

            _connectEvent.Dispose();
            _underlineSocket = null;
            _connectEvent = null;

            if (Session != null)
            {
                Session.Close(SessionCloseReason.Stop);
                Session = null;
            }

            Logger.Ins.Debug("Client stopped!");
        }


        public void Reconnect()
        {
            Start();
        }

        public void Connect()
        {
            _connectEvent.RemoteEndPoint = EndPoint;
            try
            {
                if (!_underlineSocket.ConnectAsync(_connectEvent))
                    HandleConnection(_underlineSocket);
            }
            catch (Exception e)
            {
                var msg = $"Connection failed, detail {e.Message} : {e.StackTrace}";
                OnError(msg);
            }
        }

        private void HandleConnection(Socket sock)
        {
            Session = new Session(sock, 0, _dispatcher);
            Session.Start();

            _scheduler.Invoke(_keepAlive, KeepAliveInterval, KeepAliveInterval);
        }

        private void OnConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            Loop.Ins.Perform(() =>
            {
                if (e.SocketError == SocketError.Success)
                    HandleConnection(_underlineSocket);
                else
                {
                    var msg = $"Connection failed, detail {e.SocketError}";
                    OnError(msg);
                }
            });
        }
    }
}
