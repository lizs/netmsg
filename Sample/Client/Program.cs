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

namespace Sample
{
    internal class Program
    {
        private static Client _client;
        private static Server _server;

        private static readonly byte[] PushData = Encoding.ASCII.GetBytes("Hello world!");
        private static void Push(Session session) {
            session.Push(PushData, b => {
                if(b)
                    Push(session);
                else
                    Console.WriteLine("Push failed");
            });
        }

        private static void Main(string[] args) {
            if (args.Length > 0) {
                switch (args[0]) {
                    case "server":
                        run_server();
                        break;

                    default:
                        run_client();
                        break;
                }
            }
            else {
                run_client();
            }

            // 创建并启动Launcher
            Loop.Instance.Run();

            // 销毁客户端
            _client?.Stop();
            _server?.Stop();
        }

        private static void run_client() {
            // 创建并启动客户端
            _client = new Client("127.0.0.1", 5002) {
                PushHandler = (session, bytes) => { },
                RequestHandler = (session, bytes, cb) => { cb((ushort) NetError.Success, null); }
            };
            _client.Start();

            _client.EventConnected += session => {
                Console.WriteLine("connected");
                Push(session);
            };
        }

        private static void run_server() {
            // 创建并启动客户端
            _server = new Server("127.0.0.1", 5002) {
                PushHandler = (session, bytes) => { },
                RequestHandler = (session, bytes, cb) => { cb((ushort) NetError.Success, null); }
            };

            _server.Start();

            _server.EventSessionEstablished += session => {
                Console.WriteLine("connected");
            };
        }
    }
}
