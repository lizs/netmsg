using System.Threading;

namespace mom {
    public sealed class Monitor {
        private const uint Period = 5*1000;

        public static Monitor Ins { get; } = new Monitor();

        public void Start() {
            _scheduler.Invoke(Dump, Period, Period);
        }

        public void Stop() {
            _scheduler.Cancel(Dump);
        }

        private void Dump()
        {
            Logger.Ins.Info(
                $"Write : {Wroted * 1000 / Period}/s Read : {Readed * 1000 / Period}/s Jobs : {Loop.Ins.Jobs}");

            Wroted = 0;
            Interlocked.Exchange(ref Readed, 0);
        }

        public void IncReaded() {
            Interlocked.Increment(ref Readed);
        }

        public void IncWroted() {
            ++Wroted;
        }

        public int Readed = 0;
        public int Wroted { get; private set; } = 0;

        private readonly Scheduler _scheduler = new Scheduler();
    }
}