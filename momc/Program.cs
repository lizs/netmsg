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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mom;

namespace momc
{
    internal static class ClientExt
    {
        private static byte[] Make(params string[] strs)
        {
            if (strs.IsNullOrEmpty()) return null;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                foreach (var str in strs)
                {
                    bw.Write(Encoding.ASCII.GetBytes(str));
                    bw.Write((byte) 0);
                }

                return ms.ToArray();
            }
        }

        public static void Request(this Client client, string subject, string data)
        {
            var bytes = Make(subject, data);
            client.Session?.Request(bytes,
                result =>
                {
                    Logger.Ins.Info(
                        $"Resp : {(NetError) result.Err}:{result.Data?.Length}");
                });
        }

        public static void Push(this Client client, string subject, string data)
        {
            var bytes = Make(subject, data);
            client.Session?.Push(bytes);
        }

        public static void Pub(this Client client, string subject, string data)
        {
            client.Session?.Pub(subject, Encoding.ASCII.GetBytes(data));
        }

        public static void Sub(this Client client, string subject)
        {
            client.Session?.Sub(subject);
        }

        public static void Unsub(this Client client, string subject)
        {
            client.Session?.Unsub(subject);
        }
    }

    internal class Program
    {
        private static Client _client;

        /// <summary>
        ///     解析命令行
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static Dictionary<string, List<string>> Parse(string[] args)
        {
            var dic = new Dictionary<string, List<string>>();
            if (args.IsNullOrEmpty()) return dic;

            var key = string.Empty;
            foreach (var arg in args)
            {
                if (arg.Contains("-"))
                {
                    key = arg.Replace("-", "");

                    if (!dic.ContainsKey(key))
                        dic[key] = new List<string>();
                }
                else
                {
                    if (key.IsNullOrEmpty()) continue;
                    dic[key].Add(arg);
                }
            }

            return dic;
        }

        private static void Main(string[] args)
        {
            var thread = new Thread(() =>
            {
                var cmds = Parse(args);

                var host = cmds.ContainsKey("h") ? cmds["h"]?.FirstOrDefault() : "localhost";
                var port = cmds.ContainsKey("p") ? cmds["p"]?.FirstOrDefault() : "9527";

                // 创建并启动客户端
                _client = new Client(host, int.Parse(port), new ClientHandler());
                _client.Start();

                // 创建并启动Launcher
                Loop.Ins.Run();

                // 销毁客户端
                _client?.Stop();
            });

            thread.Start();

            while (true)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                    continue;

                var commands = input.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    if (OnCommand(commands))
                        continue;
                }
                catch (Exception e)
                {
                    Logger.Ins.Exception("Command exception.", e);
                    continue;
                }

                Loop.Ins.Stop();
                thread.Join();
                Console.WriteLine("bye.");
                break;
            }
        }

        private static bool OnCommand(params string[] command)
        {
            if (command.IsNullOrEmpty()) return true;

            switch (command[0].ToLower())
            {
                case "quit":
                case "exit":
                    return false;

                case "req":
                    _client.Request(command[1], command[2]);
                    return true;

                case "push":
                    _client.Push(command[1], command[2]);
                    return true;

                case "pub":
                    _client.Pub(command[1], command[2]);
                    return true;

                case "sub":
                    _client.Sub(command[1]);
                    return true;

                case "unsub":
                    _client.Unsub(command[1]);
                    return true;

                default:
                    return true;
            }
        }

        private class ClientHandler : DefaultDispatcher
        {
            public override void OnOpen(Session session)
            {
            }

            public override void OnPush(Session session, byte[] data)
            {
                Logger.Ins.Info($"Push : {Encoding.UTF8.GetString(data)}");
            }

            public override Task<Result> OnRequest(Session session, byte[] data)
            {
                Logger.Ins.Info($"Req : {Encoding.UTF8.GetString(data)}");
                // echo back
                return Task.FromResult(new Result(0, data));
            }
        }
    }
}
