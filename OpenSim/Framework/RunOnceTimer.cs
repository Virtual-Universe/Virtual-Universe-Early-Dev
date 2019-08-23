// Nani 2019

namespace OpenSim.Framework
{
    // Nani added this:
    // A threaded timer that, much like FireAndForget, can run a callback function in a thread, after a given time.
    // The constructor accepts a value of a given type T as parameter, which will passed to the callback function.
    public static class RunOnceTimer<T>
    {
        public delegate void RunCallback(T Value);

        public class RunData
        {
            public T Value = default(T);
            public System.Threading.Timer Timer = null;
            public RunCallback CallbackDelegate = null;
            public RunData(RunCallback runCallback, T value, int dueTime)
            {
                Value = value;
                CallbackDelegate = runCallback;
                Timer = new System.Threading.Timer(Callback, this, dueTime, -1);
            }
        }

        // this will run in a background thread.
        private static void Callback(object state)
        {
            try
            {
                RunData d = (RunData)state;
                // Stop and clean up the timer.
                d.Timer.Dispose();
                d.Timer = null;
                // Call the callback function.
                try
                {
                    d.CallbackDelegate(d.Value);
                }
                catch { }
                // Do some clean up.
                d.CallbackDelegate = null;
                d.Value = default(T);
                d = null;
            }
            catch { }
        }

        public static RunData Run(RunCallback runCallback, T value, int dueTime)
        {
            RunData d = null;
            try
            {
                d = new RunData(runCallback, value, dueTime);
            }
            catch
            {
                d = null;
            }
            return d;
        }
    }

    // Nani added this: a even more simple version of RunOnceTimer<T> with the <T>
    // A threaded timer that, much like FireAndForget, can run a callback function in a thread, after a given time.
    // The callback function has no parameters.
    public static class RunOnceTimer
    {
        public delegate void RunCallback();

        public class RunData
        {
            public System.Threading.Timer Timer = null;
            public RunCallback CallbackDelegate = null;
            public RunData(RunCallback runCallback, int dueTime)
            {
                CallbackDelegate = runCallback;
                Timer = new System.Threading.Timer(Callback, this, dueTime, -1);
            }
        }

        // this will run in a background thread.
        private static void Callback(object state)
        {
            try
            {
                RunData d = (RunData)state;
                // Stop and clean up the timer.
                d.Timer.Dispose();
                d.Timer = null;
                // Call the callback function.
                try
                {
                    d.CallbackDelegate();
                }
                catch { }
                // Do some clean up.
                d.CallbackDelegate = null;
                d = null;
            }
            catch { }
        }

        public static RunData Run(RunCallback runCallback, int dueTime)
        {
            RunData d = null;
            try
            {
                d = new RunData(runCallback, dueTime);
            }
            catch
            {
                d = null;
            }
            return d;
        }
    }
}
