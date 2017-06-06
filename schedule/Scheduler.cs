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
using System.Linq;

namespace mom {
    public sealed class Scheduler {
        private readonly Dictionary<Action, TimerWrapper> _timers = new Dictionary<Action, TimerWrapper>();

        public void Stop() {
            Clear();
        }

        public void Clear() {
            foreach (var timer in _timers.Select(x => x.Value))
                timer.Stop();
            _timers.Clear();
        }

        public void Invoke(Action action, uint delay, uint period) {
            Cancel(action);

            var timer = TimerWrapper.Create(action, delay, period);
            _timers.Add(action, timer);
            timer.Start();
        }

        public void Invoke(Action action, uint delay) {
            Cancel(action);

            var timer = TimerWrapper.Create(action, delay);
            _timers.Add(action, timer);
            timer.Start();
        }

        public void Cancel(Action action) {
            if (!_timers.ContainsKey(action)) return;

            var timer = _timers[action];
            timer.Stop();
            _timers.Remove(action);
        }
    }
}
