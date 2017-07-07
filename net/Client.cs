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
using System.Net;
using System.Net.Sockets;

namespace mom
{
    /// <summary>
    /// </summary>
    public sealed class Client
    {
        public string Name => $"{Ip}:{Port}";

        public string Ip { get; private set; }
        public int Port { get; private set; }
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

        private bool _stopped;

        public Client(string ip, int port, IDispatcher dispatcher = null)
        {
            _dispatcher = new InternalDispatcher(dispatcher ?? new DefaultDispatcher(), reason =>
            {
                if (!_stopped)
                {
                    _reconnect();
                }

                _scheduler.Cancel(_keepAlive);
            });

            if (string.IsNullOrEmpty(ip) || port == 0)
                Logger.Ins.Warn("Ip or Port is invalid!");
            else if (!SetAddress(ip, port))
                throw new Exception("Ip or Port is invalid!");
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
            Logger.Ins.Error("{0}:{1}", Name, msg);
            _reconnect();
        }

        public bool SetAddress(string ip, int port)
        {
            Ip = ip;
            Port = port;

            try
            {
                Address = IPAddress.Parse(Ip);
                EndPoint = new IPEndPoint(Address, Port);
            }
            catch (Exception e)
            {
                var msg = $"{e.Message} : {e.StackTrace}";
                OnError(msg);

                Ip = string.Empty;
                Port = 0;
                return false;
            }

            return true;
        }

        public void Start()
        {
            if (string.IsNullOrEmpty(Ip) || Port == 0)
                throw new Exception("Address must be setted before start!");

            _connectEvent = new SocketAsyncEventArgs();
            _connectEvent.Completed += OnConnectCompleted;

            _underlineSocket = SocketExt.CreateTcpSocket();
            Connect();

            _stopped = false;
        }

        public void Stop()
        {
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
