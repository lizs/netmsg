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
using System.Diagnostics;
#if NET35
#else
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
#endif

namespace mom {
    /// <summary>
    ///     逻辑服务
    ///     1、可将多线程任务转换为单线程任务
    ///     2、提供定时调度、协程调度服务
    /// </summary>
    public sealed class Loop {
        public static Loop Ins { get; } = new Loop();

        private bool _quit;
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly BlockingCollection<IJob> _workingQueue;

        /// <summary>
        ///     定时器调度器
        /// </summary>
        private TimerManager TimerMgr { get; }

        /// <summary>
        /// </summary>
        public Loop()
        {
            _workingQueue = new BlockingCollection<IJob>();
            TimerMgr = new TimerManager();
        }

#if NET35
#else
        /// <summary>
        ///     让await结果在Loop线程返回
        /// </summary>
        public async Task<TRet> Perform<TRet>(Func<Task<TRet>> fun)
        {
            var tcs = new TaskCompletionSource<TRet>();
            var ret = await fun();

            Perform(() =>
            {
                if (!tcs.TrySetResult(ret))
                {
                    Logger.Ins.Error("TrySetResult failed");
                }
            });

            return await tcs.Task;
        }

        /// <summary>
        ///     让await结果在Loop线程返回
        /// </summary>
        public async Task<TRet> Perform<TRet, T>(Func<T, Task<TRet>> fun, T param)
        {
            var tcs = new TaskCompletionSource<TRet>();
            var ret = await fun(param);

            Perform(() =>
            {
                if (!tcs.TrySetResult(ret))
                {
                    Logger.Ins.Error("TrySetResult failed");
                }
            });

            return await tcs.Task;
        }

        /// <summary>
        ///     让await结果在Loop线程返回
        /// </summary>
        /// <returns></returns>
        public async Task<TRet> Perform<TRet, T1, T2>(Func<T1, T2, Task<TRet>> fun, T1 param1, T2 param2)
        {
            var tcs = new TaskCompletionSource<TRet>();
            var ret = await fun(param1, param2);

            Perform(() =>
            {
                if (!tcs.TrySetResult(ret))
                {
                    Logger.Ins.Error("TrySetResult failed");
                }
            });

            return await tcs.Task;
        }
#endif

        /// <summary>
        ///     在本服务执行该Action
        /// </summary>
        /// <param name="action"></param>
        public void Perform(Action action) {
            Enqueue(action);
        }

        /// <summary>
        ///     在本服务执行该Action
        /// </summary>
        /// <param name="action"></param>
        /// <param name="param"></param>
        public void Perform<T>(Action<T> action, T param) {
            Enqueue(action, param);
        }

        /// <summary>
        ///     添加定时器
        /// </summary>
        /// <param name="timer"></param>
        public void QueueTimer(Timer timer) {
            TimerMgr.Add(timer);
        }

        /// <summary>
        ///     移除定时器
        /// </summary>
        /// <param name="timer"></param>
        public void DequeueTimer(Timer timer)
        {
            TimerMgr.Remove(timer);
        }

        /// <summary>
        ///     Specify the working period of the working thread in milliseconds.
        ///     That is, every period,the working thread loop back to the working
        ///     procedure's top. and, the Idle event is called.
        /// </summary>
        public int Period { get; set; } = 10;

        /// <summary>
        ///     Specify the work items count currently in working queue.
        /// </summary>
        public int Jobs => _workingQueue.Count;

        /// <summary>
        ///     Get the elapsed milliseconds since the instance been constructed
        /// </summary>
        public long ElapsedMilliseconds => _watch.ElapsedMilliseconds;

        /// <summary>
        ///     External(e.g. the TCP socket thread) call this method to push
        ///     a work item into the working queue. The work item must not
        ///     be null.
        /// </summary>
        /// <param name="w">the work item object, must not be null</param>
        private void Enqueue(IJob w) {
            _workingQueue.Add(w);
        }

        /// <summary>
        ///     External(e.g. the TCP socket thread) call this method to push
        ///     a work item into the working queue.
        /// </summary>
        /// <param name="proc">the working procedure</param>
        /// <param name="param">additional parameter that passed to working procedure</param>
        private void Enqueue<T>(Action<T> proc, T param) {
            var w = new Job<T>(proc, param);
            Enqueue(w);
        }

        /// <summary>
        ///     入队
        /// </summary>
        /// <param name="proc"></param>
        private void Enqueue(Action proc) {
            var job = new Job(proc);
            Enqueue(job);
        }

        /// <summary>
        ///     主循环
        /// </summary>
        public void Run() {
            _watch.Start();

            while (!_quit) {
                // 1、 定时器调度
                TimerMgr.Update(ElapsedMilliseconds);

                // 2、 协程调度
                CoroutineScheduler.Ins.Update();

                // 3、 job调度
                var enterTime = ElapsedMilliseconds;
                do {
                    try {
                        IJob item;
                        if (_workingQueue.TryTake(out item, Period)) {
                            item.Do();
                        }
                        else
                            break;
                    }
                    catch (Exception ex) {
                        Logger.Ins.Fatal("{0} : {1}", ex.Message, ex.StackTrace);
                    }
                } while (ElapsedMilliseconds - enterTime < Period);
            }

            IJob leftItem;
            while (_workingQueue.TryTake(out leftItem, Period*10)) {
                leftItem.Do();
            }

            _watch.Stop();
        }

        public void Stop() {
            _quit = true;
        }
    }
}