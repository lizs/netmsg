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
using System.Text;
using mom;

#if NET35
#else
using System.Threading.Tasks;
#endif

namespace Sample
{
    internal class Program
    {
        private static Client _client;
        private static Server _server;

        private static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "server":
                        run_server();
                        break;

                    default:
                        run_client();
                        break;
                }
            }
            else
            {
                run_client();
            }

            Monitor.Ins.Start();

            // 创建并启动Launcher
            Loop.Ins.Run();

            Monitor.Ins.Stop();
            // 销毁客户端
            _client?.Stop();
            _server?.Stop();
        }

        private class ClientHandler : DefaultDispatcher
        {
            private static readonly byte[] Data = Encoding.ASCII.GetBytes("Hello world!");
            private static readonly Scheduler _scheduler = new Scheduler();

            private static void Push(Session session)
            {
                session.Push(Data, b =>
                {
                    if (b)
                        Push(session);
                    else
                        Console.WriteLine("Push failed");
                });
            }
            
            private static void Request(Session session)
            {
                session.Request(Data, ret => { Request(session); });
            }


            public override void OnOpen(Session session)
            {
                // request with callback
                Request(session);

                // or push
                //Push(session);
            }
        }

        private class ServerHandler : DefaultDispatcher
        {
            public override void OnRequest(Session session, byte[] data, Action<Result> cb)
            {
                cb(new Result((ushort) NetError.Success));
            }
        }

        private static void run_client()
        {
            // 创建并启动客户端
            _client = new Client("127.0.0.1", 5002, new ClientHandler());
            _client.Start();
        }

        private static void run_server()
        {
            // 创建并启动客户端
            _server = new Server("127.0.0.1", 5002, new ServerHandler());
            _server.Start();
        }
    }
}
