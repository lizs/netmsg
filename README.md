netmsg
======================
mom组件(https://git.oschina.net/lizs4ever/MOM.git)的C#实现

##Getting started
###Server
```C#
    // 自定义Handler
    class ClientHandler : DefaultHandler {
        private static readonly byte[] Data = Encoding.ASCII.GetBytes("Hello world!");
        private static void Push(Session session) {
            session.Push(Data, b => {
                if (b)
                    Push(session);
                else
                    Console.WriteLine("Push failed");
            });
        }

        private static async Task<bool> Request(Session session) {
            while (true) {
                var ret = await session.Request(Data);
                return await Request(session);
            }
        }

        public override void OnOpen(Session session) {
            Request(session);

            // or push
            //Push(session);
        }
    }

    // 创建服务器
    var server = new Server("127.0.0.1", 5002);
    server.Start();

    // 创建客户端
    var client = new Client("127.0.0.1", 5002, new CustomHandler());
    client.Start();

    // 主循环
    Loop.Instance.Run();

    // 结束
    client.Stop();
    server.Stop();
```

##Question
QQ group ：http://jq.qq.com/?_wv=1027&k=VptNja
<br>e-mail : lizs4ever@163.com
