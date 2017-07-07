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
#if MESSAGE_TRACK_ENABLED
using System.IO;
using System.Text;
#endif

namespace mom
{
    public enum PackerError
    {
        Success,
        Running,
        Failed
    }

    /// <summary>
    ///     拆包
    ///     非线程安全，需要上层来确保始终在某个线程运行
    /// </summary>
    public class Extractor
    {
        public Extractor(ushort packMaxSize)
        {
            PackageMaxSize = packMaxSize;
        }

        public ushort PackageMaxSize { get; }
        public const int HearderLen = sizeof(ushort);

        private bool _headerExtracted;

        private ushort _packageLen;
        private byte[] _body;
        private ushort _alreadyExtractedLen;

        public Queue<byte[]> Packages = new Queue<byte[]>();

        public PackerError Process(CircularBuffer buffer, ref ushort packagesCnt)
        {
            PackerError error;
            if (!_headerExtracted)
            {
                error = ProcessHeader(buffer);
                switch (error)
                {
                    case PackerError.Running:
                    case PackerError.Failed:
                        return error;
                }
            }

            error = ProcessBody(buffer);
            switch (error)
            {
                case PackerError.Success:
                {
                    packagesCnt++;
                    Packages.Enqueue(_body);

#if MESSAGE_TRACK_ENABLED
                    var bytes = new byte[2];
                    using (var ms = new MemoryStream(bytes))
                    using (var bw = new BinaryWriter(ms))
                    {
                        bw.Write(_packageLen);
                    }

                    var sb = new StringBuilder();
                    sb.AppendFormat($"Read [ ");

                    foreach (var b in bytes)
                    {
                        sb.Append($"{b.ToString("X2")} ");
                    }

                    foreach (var b in _body)
                    {
                        sb.Append($"{b.ToString("X2")} ");
                    }

                    sb.Append("]");
                    Logger.Ins.Debug(sb.ToString());
#endif

                    Reset();

                    return Process(buffer, ref packagesCnt);
                }

                default:
                    return error;
            }
        }

        private void Reset()
        {
            _body = null;
            _headerExtracted = false;
            _packageLen = 0;
            _alreadyExtractedLen = 0;
        }

        private PackerError ProcessHeader(CircularBuffer buffer)
        {
            if (buffer.ReadableSize < HearderLen) return PackerError.Running;

            var one = buffer.Buffer[buffer.Head];
            var two = buffer.Buffer[buffer.Head + 1];

            _packageLen = (ushort) (two << 8 | one);
            if (_packageLen > PackageMaxSize)
            {
                Logger.Ins.Warn("Processing buffer size : {0} bytes,  bigger than {1} bytes!", _packageLen,
                    PackageMaxSize);
                return PackerError.Failed;
            }

            _body = new byte[_packageLen];

            buffer.MoveByRead(HearderLen);
            _headerExtracted = true;
            return PackerError.Success;
        }

        private PackerError ProcessBody(CircularBuffer buffer)
        {
            var extractLen = Math.Min((ushort) (_packageLen - _alreadyExtractedLen), buffer.ReadableSize);

            Buffer.BlockCopy(buffer.Buffer, buffer.Head, _body, _alreadyExtractedLen, extractLen);

            buffer.MoveByRead(extractLen);
            _alreadyExtractedLen += extractLen;

            return _alreadyExtractedLen == _packageLen ? PackerError.Success : PackerError.Running;
        }
    }
}
