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
using System.Collections.Concurrent;
using System.Diagnostics;

namespace mom {
    /// <summary>
    ///     逻辑服务
    ///     1、可将多线程任务转换为单线程任务
    ///     2、提供定时调度、协程调度服务
    /// </summary>
    public sealed class Loop {
        public static Loop Instance { get; } = new Loop();

        private const int StopWatchDivider = 128;
        private bool _quit;
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly BlockingCollection<IJob> _workingQueue;

        /// <summary>
        ///     定时器调度器
        /// </summary>
        public TimerScheduler Scheduler { get; }

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
        ///     Idle event, call by working thread
        ///     every period.
        ///     <remarks>
        ///         generally the working thread
        ///         call the event every period, but if it's too busy
        ///         because the working item consumes too much time,
        ///         the calling period may grater than the original period
        ///     </remarks>
        /// </summary>
        public event Action Idle;

        /// <summary>
        ///     Specify the working period of the working thread in milliseconds.
        ///     That is, every period,the working thread loop back to the working
        ///     procedure's top. and, the Idle event is called.
        /// </summary>
        public int Period { get; set; } = 10;

        /// <summary>
        ///     A time counter that count the work items consume how much time
        ///     in one working thread's loop. It maybe grater than the working period,
        ///     which indicates the work items consume too much time.
        /// </summary>
        public long WiElapsed { get; set; }

        /// <summary>
        ///     A time counter that count the Idle event callbacks consume how much
        ///     time in one working thread's loop. This value should be less than period.
        /// </summary>
        public long IdleCallbackElapsed { get; set; }

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
        public void Enqueue(IJob w) {
            _workingQueue.Add(w);
        }

        /// <summary>
        ///     External(e.g. the TCP socket thread) call this method to push
        ///     a work item into the working queue.
        /// </summary>
        /// <param name="proc">the working procedure</param>
        /// <param name="param">additional parameter that passed to working procedure</param>
        public void Enqueue<T>(Action<T> proc, T param) {
            var w = new Job<T>(proc, param);
            Enqueue(w);
        }

        /// <summary>
        ///     入队
        /// </summary>
        /// <param name="proc"></param>
        public void Enqueue(Action proc) {
            var job = new Job(proc);
            Enqueue(job);
        }

        public Loop() {
            _workingQueue = new BlockingCollection<IJob>();
            Scheduler = new TimerScheduler();
        }

        public void Run() {
            Scheduler.Start();
            _watch.Start();

            while (!_quit) {
                var periodCounter = StopWatchDivider;
                var tick = Environment.TickCount;

                var t1 = _watch.ElapsedMilliseconds;

                do {
                    try {
                        IJob item;
                        if (_workingQueue.TryTake(out item, Period)) {
                            item.Do();
                            periodCounter--;

                            if (periodCounter >= 1) continue;
                            if (_watch.ElapsedMilliseconds - t1 >= Period) {
                                break;
                            }
                            periodCounter = StopWatchDivider;
                        }
                        else
                            break;
                    }
                    catch (Exception ex) {
                        Logger.Ins.Fatal("{0} : {1}", ex.Message, ex.StackTrace);
                    }
                } while (Environment.TickCount - tick < Period);

                WiElapsed = _watch.ElapsedMilliseconds - t1;
                var t2 = _watch.ElapsedMilliseconds;
                try {
                    Idle?.Invoke();
                }
                catch (Exception ex) {
                    Logger.Ins.Fatal($"{ex.Message} : {ex.StackTrace}");
                }

                IdleCallbackElapsed = _watch.ElapsedMilliseconds - t2;
            }


            IJob leftItem;
            while (_workingQueue.TryTake(out leftItem, Period*10)) {
                leftItem.Do();
            }

            _watch.Stop();
        }

        public void Stop() {
            _quit = true;
            Scheduler.Stop();
        }
    }
}