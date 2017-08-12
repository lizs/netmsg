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
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace mom
{
    /// <summary>
    ///     协程调度器
    /// </summary>
    public sealed class CoroutineScheduler
    {
        public static CoroutineScheduler Ins { get; } = new CoroutineScheduler();

        private readonly LinkedList<Coroutine> _coroutines = new LinkedList<Coroutine>();
        private readonly LinkedList<Coroutine> _comming = new LinkedList<Coroutine>();
        private readonly LinkedList<Coroutine> _dead = new LinkedList<Coroutine>();
        private readonly Scheduler _scheduler = new Scheduler();
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="coroutine"></param>
        public void StopCoroutine(Coroutine coroutine)
        {
            _dead.AddLast(coroutine);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fun"></param>
        /// <param name="arg"></param>
        /// <returns></returns>
        public Coroutine StartCoroutine(Func<object[], IEnumerator> fun, params object[] arg)
        {
            var co = new Coroutine(fun, arg);
            _comming.AddLast(co);
            return co;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fun"></param>
        /// <returns></returns>
        public Coroutine StartCoroutine(Func<IEnumerator> fun)
        {
            var co = new Coroutine(fun);
            _comming.AddLast(co);
            return co;
        }
        
        /// <summary>
        ///     等待period毫秒
        /// </summary>
        /// <param name="period"></param>
        /// <returns></returns>
        public IEnumerator<bool> Wait(uint period)
        {
            var continous = true;
            _scheduler.Invoke(() => { continous = false; }, period);
            while(continous)
                yield return true;
        }

        /// <summary>
        ///     调度
        /// </summary>
        public void Update()
        {
            foreach (var coroutine in _dead)
                _coroutines.Remove(coroutine);
            
            foreach (var coroutine in _dead)
                _comming.Remove(coroutine);

            foreach (var coroutine in _comming)
                _coroutines.AddLast(coroutine);

            _dead.Clear();
            _comming.Clear();
            
            foreach (var coroutine in _coroutines)
            {
                if (!coroutine.Update())
                    _dead.AddLast(coroutine);
            }
        }
    }
}