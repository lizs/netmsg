netmsg
======================
mom组件(https://git.oschina.net/lizs4ever/MOM.git)的C#实现

##Getting started
###Server
```C#
    // 创建服务器
    var server = new Server("127.0.0.1", 5002) {
                PushHandler = (session, bytes) => { },
                RequestHandler = (session, bytes, cb) => { cb((ushort) NetError.Success, null); },
                OpenHandler = session => { },
                CloseHandler = (session, reason) => { }
            };

    server.Start();
    // 创建客户端
    var client = new Client("127.0.0.1", 5002) {
                PushHandler = (session, bytes) => { },
                RequestHandler = (session, bytes, cb) => { cb((ushort) NetError.Success, null); },
                OpenHandler = session => { Push(session); },
                CloseHandler = (session, reason) => { }
            };
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
