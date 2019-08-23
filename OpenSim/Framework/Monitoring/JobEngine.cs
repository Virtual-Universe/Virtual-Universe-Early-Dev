/* 4 April 2019
 * 
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Reflection;
using System.Threading;
using log4net;

namespace OpenSim.Framework.Monitoring
{
    public class JobEngine
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public int LogLevel { get; set; }

        public string Name { get; private set; }

        public string LoggingName { get; private set; }

        /// <summary>
        /// Is this engine running?
        /// </summary>
        private volatile bool m_isrunning = false;
        public bool IsRunning { get { return m_isrunning; }
                        private set { m_isrunning = value; } }

        /// <summary>
        /// The current job that the engine is running.
        /// </summary>
        /// <remarks>
        /// Will be null if no job is currently running.
        /// </remarks>
        private Job m_currentJob;
        public Job CurrentJob { get { return m_currentJob;} }

        /// <summary>
        /// Number of jobs waiting to be processed.
        /// </summary>
        public int JobsWaiting { get { return m_jobQueue.Count; } }

        /// <summary>
        /// The timeout in milliseconds to wait for at least one event to be written when the recorder is stopping.
        /// </summary>
        public int RequestProcessTimeoutOnStop { get; set; }

        /// <summary>
        /// Controls whether we need to warn in the log about exceeding the max queue size.
        /// </summary>
        /// <remarks>
        /// This is flipped to false once queue max has been exceeded and back to true when it falls below max, in
        /// order to avoid spamming the log with lots of warnings.
        /// </remarks>
        private NSingleReaderConcurrentQueue<Job> m_jobQueue = new NSingleReaderConcurrentQueue<Job>();
        private int m_BoundedCapacity = 5000;

		private WaitCallback m_process = null;
        
        private int m_timeout = -1;

        private int m_threadFlag = 0;
        private object m_syncLock = new object();

		public JobEngine(string name, string loggingName, int timeout = -1)
        {
            Name = name;
            LoggingName = loggingName;
            m_timeout = timeout;
            m_isrunning = true;
            RequestProcessTimeoutOnStop = 5000;

			if (m_timeout > 0)
			{
				m_process = ProcessRequests;
			}
			else
			{
				m_process = ProcessRequestsInfinite;
			}
		}

        public void Start()
        {
            m_isrunning = true;
            // Grab the flag.
            if (0 == Interlocked.CompareExchange(ref m_threadFlag, 1, 0))
            { 
                 WorkManager.RunInThreadPool(m_process, null, Name, false);
            }
        }

        public void Stop()
        {
            m_isrunning = false;
            m_jobQueue.CancelWait();
            // Release the flag.
            Interlocked.Exchange(ref m_threadFlag, 0);
        }

        /// <summary>
        /// Make a job.
        /// </summary>
        /// <remarks>
        /// We provide this method to replace the constructor so that we can later pool job objects if necessary to
        /// reduce memory churn.  Normally one would directly call QueueJob() with parameters anyway.
        /// </remarks>
        /// <returns></returns>
        /// <param name="name">Name.</param>
        /// <param name="action">Action.</param>
        /// <param name="commonId">Common identifier.</param>
        public static Job MakeJob(string name, Action action, string commonId = null)
        {
            return Job.MakeJob(name, action, commonId);
        }

        /// <summary>
        /// Remove the next job queued for processing.
        /// </summary>
        /// <remarks>
        /// Returns null if there is no next job.
        /// Will not remove a job currently being performed.
        /// </remarks>
        public Job RemoveNextJob()
        {
			Job nextJob;
			m_jobQueue.Dequeue(out nextJob);
            return nextJob;
        }

        /// <summary>
        /// Queue the job for processing.
        /// </summary>
        /// <returns><c>true</c>, if job was queued, <c>false</c> otherwise.</returns>
        /// <param name="name">Name of job.  This appears on the console and in logging.</param>
        /// <param name="action">Action to perform.</param>
        /// <param name="commonId">
        /// Common identifier for a set of jobs.  This is allows a set of jobs to be removed
        /// if required (e.g. all jobs for a given agent.  Optional.
        /// </param>
        public bool QueueJob(string name, Action action, string commonId = null)
        {
            return QueueJob(MakeJob(name, action, commonId));
        }

        /// <summary>
        /// Queue the job for processing.
        /// </summary>
        /// <returns><c>true</c>, if job was queued, <c>false</c> otherwise.</returns>
        /// <param name="job">The job</param>
        /// </param>
        public bool QueueJob(Job job)
        {
			if (m_isrunning)
			{
                // Grab the flag.
                if (0 == Interlocked.CompareExchange(ref m_threadFlag, 1, 0))
                {
                    WorkManager.RunInThreadPool(m_process, null, Name, false);
                }

                if (m_jobQueue.Count < m_BoundedCapacity)
				{
					m_jobQueue.Enqueue(job);
					return true;
				}
			}
            return false;
        }

		private void ProcessRequests(Object o)
        {
            while (m_isrunning)
            {
                try
                {
                    if (!m_jobQueue.TryDequeue(out m_currentJob, m_timeout))
                    {
                        // Release the flag.
                        Interlocked.Exchange(ref m_threadFlag, 0);
                        return;
                    }					
                }
                catch 
                {
                    break;
                }

                try
                {
                    m_currentJob.Action();
                }
                catch { }

                m_currentJob = null;
            }

            Thread.Sleep(RequestProcessTimeoutOnStop);
        }

        private void ProcessRequestsInfinite(Object o)
        {
            while (m_isrunning)
            {
                try
                {
                    if (!m_jobQueue.TryDequeue(out m_currentJob))
                    {
                        // Release the flag.
                        Interlocked.Exchange(ref m_threadFlag, 0);
                        return;
                    }
                }
                catch
                {
                    break;
                }

                try
                {
                    m_currentJob.Action();
                }
                catch { }
                m_currentJob = null;
			}

            Thread.Sleep(RequestProcessTimeoutOnStop);
        }

        public class Job
        {
            /// <summary>
            /// Name of the job.
            /// </summary>
            /// <remarks>
            /// This appears on console and debug output.
            /// </remarks>
            public string Name { get; private set; }

            /// <summary>
            /// Common ID for this job.
            /// </summary>
            /// <remarks>
            /// This allows all jobs with a certain common ID (e.g. a client UUID) to be removed en-masse if required.
            /// Can be null if this is not required.
            /// </remarks>
            public string CommonId { get; private set; }

            /// <summary>
            /// Action to perform when this job is processed.
            /// </summary>
            public Action Action { get; private set; }

            private Job(string name, string commonId, Action action)
            {
                Name = name;
                CommonId = commonId;
                Action = action;
            }

            /// <summary>
            /// Make a job.  It needs to be separately queued.
            /// </summary>
            /// <remarks>
            /// We provide this method to replace the constructor so that we can pool job objects if necessary to
            /// to reduce memory churn.  Normally one would directly call JobEngine.QueueJob() with parameters anyway.
            /// </remarks>
            /// <returns></returns>
            /// <param name="name">Name.</param>
            /// <param name="action">Action.</param>
            /// <param name="commonId">Common identifier.</param>
            public static Job MakeJob(string name, Action action, string commonId = null)
            {
                return new Job(name, commonId, action);
            }
        }
    }
}
