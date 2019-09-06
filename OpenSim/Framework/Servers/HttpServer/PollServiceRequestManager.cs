

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using Amib.Threading;
using log4net;
using OpenSim.Framework.Monitoring;

namespace OpenSim.Framework.Servers.HttpServer
{
    public class PollServiceRequestManager
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Dictionary<int, Queue<PollServiceHttpRequest>> m_bycontext;
        private BlockingCollection<PollServiceHttpRequest> m_requests = new BlockingCollection<PollServiceHttpRequest>();
        private ConcurrentQueue<PollServiceHttpRequest> m_retryRequests = new ConcurrentQueue<PollServiceHttpRequest>();

        private uint m_WorkerThreadCount = 0;
        private Thread[] m_workerThreads;
        private Thread m_retrysThread;

        private bool m_running = false;

        private SmartThreadPool m_threadPool;

        public PollServiceRequestManager(bool performResponsesAsync, uint pWorkerThreadCount, int pTimeout)
        {
            m_WorkerThreadCount = pWorkerThreadCount;
            m_workerThreads = new Thread[m_WorkerThreadCount];

            m_bycontext = new Dictionary<int, Queue<PollServiceHttpRequest>>(256);

            STPStartInfo startInfo = new STPStartInfo();
            startInfo.IdleTimeout = 30000;
            startInfo.MaxWorkerThreads = 20;
            startInfo.MinWorkerThreads = 1;
            startInfo.ThreadPriority = ThreadPriority.Normal;
            startInfo.StartSuspended = true;
            startInfo.ThreadPoolName = "PoolService";

            m_threadPool = new SmartThreadPool(startInfo);
        }

        public void Start()
        {
            if (m_running)
            {
                return;
            }

            m_running = true;
            m_threadPool.Start();
            
            // startup worker threads
            for (uint i = 0; i < m_WorkerThreadCount; i++)
            {
                m_workerThreads[i]
                    = WorkManager.StartThread(
                        PoolWorkerJob,
                        string.Format("PollServiceWorkerThread {0}", i),
                        ThreadPriority.Normal,
                        true,
                        false,
                        null,
                        int.MaxValue);
            }

            m_retrysThread = WorkManager.StartThread(
                this.CheckRetries,
                string.Format("PollServiceWatcherThread"),
                ThreadPriority.Normal,
                true,
                true,
                null,
                1000 * 60 * 10);
        }

        private void ReQueueEvent(PollServiceHttpRequest req)
        {
            if (m_running)
            {
                m_retryRequests.Enqueue(req);
            }
        }

        public void Enqueue(PollServiceHttpRequest req)
        {
            Queue<PollServiceHttpRequest> ctxQeueue;
            int rhash = req.contextHash;

            lock (m_bycontext)
            {
                if (m_bycontext.TryGetValue(rhash, out ctxQeueue))
                {
                    ctxQeueue.Enqueue(req);
                }
                else
                {
                    ctxQeueue = new Queue<PollServiceHttpRequest>();
                    m_bycontext[rhash] = ctxQeueue;
                    EnqueueInt(req);
                }
            }
        }

        public void byContextDequeue(PollServiceHttpRequest req)
        {
            Queue<PollServiceHttpRequest> ctxQeueue;
            int rhash = req.contextHash;

            lock (m_bycontext)
            {
                if (m_bycontext.TryGetValue(rhash, out ctxQeueue))
                {
                    if (ctxQeueue.Count > 0)
                    {
                        PollServiceHttpRequest newreq = ctxQeueue.Dequeue();
                        EnqueueInt(newreq);
                    }
                    else
                    {
                        m_bycontext.Remove(rhash);
                    }
                }
            }
        }

        public void DropByContext(PollServiceHttpRequest req)
        {
            Queue<PollServiceHttpRequest> ctxQeueue;
            int rhash = req.contextHash;

            lock (m_bycontext)
            {
                if (m_bycontext.TryGetValue(rhash, out ctxQeueue))
                {
                    ctxQeueue.Clear();
                    m_bycontext.Remove(rhash);
                }
            }
        }

        public void EnqueueInt(PollServiceHttpRequest req)
        {
            if (m_running)
            {
                m_requests.Add(req);
            }
        }

        private void CheckRetries()
        {
            PollServiceHttpRequest preq;

            while (m_running)
            {
                Thread.Sleep(100);
                Watchdog.UpdateThread();

                while (m_running && m_retryRequests.TryDequeue(out preq))
                {
                    m_requests.Add(preq);
                }
            }
        }

        public void Stop()
        {
            if (!m_running)
            {
                return;
            }

            m_running = false;

            Thread.Sleep(100); // let the world move

            foreach (Thread t in m_workerThreads)
            {
                Watchdog.AbortThread(t.ManagedThreadId);
            }

            m_threadPool.Shutdown();

            // any entry in m_bycontext should have a active request on the other queues
            // so just delete contents to easy GC
            foreach (Queue<PollServiceHttpRequest> qu in m_bycontext.Values)
            {
                qu.Clear();
            }

            m_bycontext.Clear();

            PollServiceHttpRequest req;

            try
            {
                while (m_retryRequests.TryDequeue(out req))
                {
                    req.DoHTTPstop();
                }
            }
            catch
            {
            }

            try
            {
                while (m_requests.TryTake(out req, 0))
                {
                    req.DoHTTPstop();
                }
            }
            catch
            {
            }

            m_requests.Dispose();
        }

        // work threads
        private void PoolWorkerJob()
        {
            PollServiceHttpRequest req;

            while (m_running)
            {
                try
                {
                    req = null;

                    if (!m_requests.TryTake(out req, 4500) || req == null)
                    {
                        Watchdog.UpdateThread();
                        continue;
                    }

                    Watchdog.UpdateThread();

                    if (!req.HttpContext.CanSend())
                    {
                        req.PollServiceArgs.Drop(req.RequestID, req.PollServiceArgs.Id);
                        byContextDequeue(req);
                        continue;
                    }

                    if (req.HttpContext.IsSending())
                    {
                        if ((Environment.TickCount - req.RequestTime) > req.PollServiceArgs.TimeOutms)
                        {
                            req.PollServiceArgs.Drop(req.RequestID, req.PollServiceArgs.Id);
                            byContextDequeue(req);
                        }
                        else
                        {
                            ReQueueEvent(req);
                        }
                        continue;
                    }

                    if (req.PollServiceArgs.HasEvents(req.RequestID, req.PollServiceArgs.Id))
                    {
                        PollServiceHttpRequest nreq = req;
                        m_threadPool.QueueWorkItem(x =>
                        {
                            try
                            {
                                Hashtable responsedata = nreq.PollServiceArgs.GetEvents(nreq.RequestID, nreq.PollServiceArgs.Id);
                                nreq.DoHTTPGruntWork(responsedata);
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                            finally
                            {
                                byContextDequeue(nreq);
                                nreq = null;
                            }
                            return null;
                        }, null);
                    }
                    else
                    {
                        if ((Environment.TickCount - req.RequestTime) > req.PollServiceArgs.TimeOutms)
                        {
                            PollServiceHttpRequest nreq = req;
                            m_threadPool.QueueWorkItem(x =>
                            {
                                try
                                {
                                    nreq.DoHTTPGruntWork(nreq.PollServiceArgs.NoEvents(nreq.RequestID, nreq.PollServiceArgs.Id));
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                                finally
                                {
                                    byContextDequeue(nreq);
                                    nreq = null;
                                }
                                return null;
                            }, null);
                        }
                        else
                        {
                            ReQueueEvent(req);
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    Thread.ResetAbort();
                    
                    // Shouldn't set this to 'false', the normal shutdown should cause things to exit
                    // but robust is still not normal neither is mono
                    m_running = false;
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("Exception in poll service thread: " + e.ToString());
                }
            }
        }
    }
}
