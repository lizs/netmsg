﻿#region MIT
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
using System.Linq;

namespace mom
{
    public enum PackerError
    {
        Success,
        Running,
        Failed,
    }

    /// <summary>
    /// 拆包
    /// 非线程安全，需要上层来确保始终在某个线程运行
    /// </summary>
    public class Packer
    {
        public Packer(ushort packMaxSize)
        {
            PackageMaxSize = packMaxSize;
        }

        public ushort PackageMaxSize { get; }
        public const int HearderLen = sizeof (ushort);


        private bool _headerExtracted;

        #region huge package
        private byte _segmentCount = 1;
        private int _arrivedSegmentsCount;
        private byte[][] _segments;
        #endregion

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
                        if (_segmentCount > 1)
                        {
                            if (_arrivedSegmentsCount < _segmentCount)
                            {
                                _segments[_arrivedSegmentsCount++] = _body;
                            }

                            if(_arrivedSegmentsCount == _segmentCount)
                            {
                                var len = _segments.Select(x => x.Length).Sum(x => x);
                                var body = new byte[len];
                                var offset = 0;
                                for (var i = 0; i < _segmentCount; i++)
                                {
                                    Buffer.BlockCopy(_segments[i], 0, body, offset, _segments[i].Length);
                                    offset += _segments[i].Length;
                                }

                                Packages.Enqueue(body);
                                ResetSegments();
                            }
                        }
                        else
                            Packages.Enqueue(_body);

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

        private void ResetSegments()
        {
            _arrivedSegmentsCount = 0;
            _segmentCount = 1;
            _segments = null;
        }

        private PackerError ProcessHeader(CircularBuffer buffer)
        {
            if (buffer.ReadableSize < HearderLen) return PackerError.Running;

            var one = buffer.Buffer[buffer.Head];
            var two = buffer.Buffer[buffer.Head + 1];

            _packageLen = (ushort)(two << 8 | one);
            if (_packageLen > PackageMaxSize)
            {
                Logger.Ins.Warn("Processing buffer size : {0} bytes,  bigger than {1} bytes!", _packageLen, PackageMaxSize);
                return PackerError.Failed;
            }

            // 解析该包是否由多个包组合而成
            if (_segmentCount == 1)
            {
                var cnt = buffer.Buffer[buffer.Head + 2];
                if (cnt > 1)
                {
                    _segmentCount = cnt;
                    _segments = new byte[_segmentCount][];
                }
            }

            _body = new byte[_packageLen];

            buffer.MoveByRead(HearderLen);
            _headerExtracted = true;
            return PackerError.Success;
        }

        private PackerError ProcessBody(CircularBuffer buffer)
        {
            var extractLen = Math.Min((ushort)(_packageLen - _alreadyExtractedLen), buffer.ReadableSize);

            Buffer.BlockCopy(buffer.Buffer, buffer.Head, _body, _alreadyExtractedLen, extractLen);

            buffer.MoveByRead(extractLen);
            _alreadyExtractedLen += extractLen;

            return _alreadyExtractedLen == _packageLen ? PackerError.Success : PackerError.Running;
        }
    }
}
