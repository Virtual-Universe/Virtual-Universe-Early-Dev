﻿/*
 * Copyright (c) Contributors, https://virtual-planets.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Virtual Universe Project nor the
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
using System.Collections;
using System.Collections.Generic;
using System.Timers;
using OpenMetaverse.Packets;
using OpenSim.Framework;
using OpenSim.Framework.Monitoring;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.Framework.Scenes
{
    /// <summary>
    /// Collect statistics from the scene to send to the client and for access by other monitoring tools.
    /// </summary>
    /// <remarks>
    /// FIXME: This should be a monitoring region module
    /// </remarks>
    public class SimStatsReporter
    {
        private static readonly log4net.ILog m_log
            = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const string LastReportedObjectUpdateStatName = "LastReportedObjectUpdates";
        public const string SlowFramesStatName = "SlowFrames";

        public delegate void SendStatResult(SimStats stats);
        public delegate void YourStatsAreWrong();
        public delegate void SendAgentStat(string name, string ipAddress, string timestamp);

        public event SendStatResult OnSendStatsResult;

        public event YourStatsAreWrong OnStatsIncorrect;

        private SendStatResult handlerSendStatResult;

        private YourStatsAreWrong handlerStatsIncorrect;

        // Determines the size of the array that is used to collect StatBlocks
        // for sending to the SimStats and SimExtraStatsCollector
        private const int m_statisticArraySize = 32;

        // Holds the names of the users that are currently attempting to login
        // to the server
        private ArrayList m_usersLoggingIn;

        /// <summary>
        /// These are the IDs of stats sent in the StatsPacket to the viewer.
        /// </summary>
        /// <remarks>
        /// Some of these are not relevant to OpenSimulator since it is architected differently to other simulators
        /// (e.g. script instructions aren't executed as part of the frame loop so 'script time' is tricky).
        /// </remarks>
        public enum Stats : uint
        {
            TimeDilation = 0,
            SimFPS = 1,
            PhysicsFPS = 2,
            AgentUpdates = 3,
            FrameMS = 4,
            NetMS = 5,
            OtherMS = 6,
            PhysicsMS = 7,
            AgentMS = 8,
            ImageMS = 9,
            ScriptMS = 10,
            TotalPrim = 11,
            ActivePrim = 12,
            Agents = 13,
            ChildAgents = 14,
            ActiveScripts = 15,
            ScriptLinesPerSecond = 16,
            InPacketsPerSecond = 17,
            OutPacketsPerSecond = 18,
            PendingDownloads = 19,
            PendingUploads = 20,
            VirtualSizeKb = 21,
            ResidentSizeKb = 22,
            PendingLocalUploads = 23,
            UnAckedBytes = 24,
            PhysicsPinnedTasks = 25,
            PhysicsLodTasks = 26,
            SimPhysicsStepMs = 27,
            SimPhysicsShapeMs = 28,
            SimPhysicsOtherMs = 29,
            SimPhysicsMemory = 30,
            ScriptEps = 31,
            SimSpareMs = 32,
            SimSleepMs = 33,
            SimIoPumpTime = 34,
            FrameDilation = 35,
            UsersLoggingIn = 36,
            TotalGeoPrim = 37,
            TotalMesh = 38,
            ThreadCount = 39,
            UDPInRate = 40,
            UDPOutRate = 41,
            UDPErrorRate = 42,
            NetworkQueueSize = 43,
            ClientPingAvg = 44
        }

        /// <summary>
        /// This is for llGetRegionFPS
        /// </summary>
        public float LastReportedSimFPS
        {
            get { return lastReportedSimFPS; }
        }

        /// <summary>
        /// Number of object updates performed in the last stats cycle
        /// </summary>
        /// <remarks>
        /// This isn't sent out to the client but it is very useful data to detect whether viewers are being sent a
        /// large number of object updates.
        /// </remarks>
        public float LastReportedObjectUpdates { get; private set; }

        public float[] LastReportedSimStats
        {
            get { return lastReportedSimStats; }
        }

        /// <summary>
        /// Number of frames that have taken longer to process than Scene.MIN_FRAME_TIME
        /// </summary>
        public Stat SlowFramesStat { get; private set; }

        /// <summary>
        /// The threshold at which we log a slow frame.
        /// </summary>
        public int SlowFramesStatReportThreshold { get; private set; }

        /// <summary>
        /// Extra sim statistics that are used by monitors but not sent to the client.
        /// </summary>
        /// <value>
        /// The keys are the stat names.
        /// </value>
        private Dictionary<string, float> m_lastReportedExtraSimStats = new Dictionary<string, float>();

        // Sending a stats update every 3 seconds-
        private int m_statsUpdatesEveryMS = 3000;
        private float m_statsUpdateFactor;
        private float m_timeDilation;
        private int m_fps;

        /// <summary>
        /// Number of the last frame on which we processed a stats udpate.
        /// </summary>
        private uint m_lastUpdateFrame;

        /// <summary>
        /// Our nominal fps target, as expected in fps stats when a sim is running normally.
        /// </summary>
        private float m_nominalReportedFps = 55;

        /// <summary>
        /// Parameter to adjust reported scene fps
        /// </summary>
        /// <remarks>
        /// Our scene loop runs slower than other server implementations, apparantly because we work somewhat differently.
        /// However, we will still report an FPS that's closer to what people are used to seeing.  A lower FPS might
        /// affect clients and monitoring scripts/software.
        /// </remarks>
        private float m_reportedFpsCorrectionFactor = 5;

        // saved last reported value so there is something available for llGetRegionFPS 
        private float lastReportedSimFPS;
        private float[] lastReportedSimStats = new float[m_statisticArraySize];
        private float m_pfps;

        /// <summary>
        /// Number of agent updates requested in this stats cycle
        /// </summary>
        private int m_agentUpdates;

        /// <summary>
        /// Number of object updates requested in this stats cycle
        /// </summary>
        private int m_objectUpdates;

        private int m_frameMS;
        private int m_spareMS;
        private int m_netMS;
        private int m_agentMS;
        private int m_physicsMS;
        private int m_imageMS;
        private int m_otherMS;

//Ckrinke: (3-21-08) Comment out to remove a compiler warning. Bring back into play when needed.
//Ckrinke        private int m_scriptMS = 0;

        private int m_rootAgents;
        private int m_childAgents;
        private int m_numPrim;
        private int m_numGeoPrim;
        private int m_numMesh;
        private double m_inPacketsPerSecond;
        private double m_outPacketsPerSecond;
        private int m_activePrim;
        private int m_unAckedBytes;
        private int m_pendingDownloads;
        private int m_pendingUploads = 0;  // FIXME: Not currently filled in
        private int m_activeScripts;
        private int m_scriptLinesPerSecond;

        private int m_objectCapacity = 45000;

        // This is the number of frames that will be stored and then averaged for
        // the Total, Simulation, Physics, and Network Frame Time; It is set to
        // 10 by default but can be changed by the OpenSim.ini configuration file
        // NumberOfFrames parameter
        private int m_numberFramesStored = Scene.m_defaultNumberFramesStored;

        // The arrays that will hold the time it took to run the past N frames,
        // where N is the num_frames_to_average given by the configuration file
        private double[] m_totalFrameTimeMilliseconds;
        private double[] m_simulationFrameTimeMilliseconds;
        private double[] m_physicsFrameTimeMilliseconds;
        private double[] m_networkFrameTimeMilliseconds;
        private int[] m_networkQueueSize;

        // The location of the next time in milliseconds that will be
        // (over)written when the next frame completes
        private int m_nextLocation = 0;
        private int m_netLocation = 0;

        // The correct number of frames that have completed since the last stats
        // update for physics
        private int m_numberPhysicsFrames;

        // The last reported value of threads from the SmartThreadPool inside of
        // XEngine
        private int m_inUseThreads;
        
        // These variables record the most recent snapshot of the UDP network 
        // by holding values for the bytes per second in and out, and the number
        // of packets ignored per second
        private double m_inByteRate = 0.0;
        private double m_outByteRate = 0.0;
        private double m_errorPacketRate = 0.0;

        // Average ping between the server and a subset of connected users
        private double m_clientPing = 0.0;

        // Keeps track of the total ping time, and the number, of all connected clients pinged
        private double m_totalPingTime = 0;
        private int m_clientPingCount = 0;

        private Scene m_scene;

        private RegionInfo ReportingRegion;

        private Timer m_report = new Timer();

        private IEstateModule estateModule;

        public SimStatsReporter(Scene scene)
        {
            // Initialize the different frame time arrays to the correct sizes
            m_totalFrameTimeMilliseconds = new double[m_numberFramesStored];
            m_simulationFrameTimeMilliseconds = new double[m_numberFramesStored];
            m_physicsFrameTimeMilliseconds = new double[m_numberFramesStored];
            m_networkFrameTimeMilliseconds = new double[m_numberFramesStored];
            m_networkQueueSize = new int[m_numberFramesStored];

            // Initialize the array to hold the names of the users currently
            // attempting to login to the server
            m_usersLoggingIn = new ArrayList();

            m_scene = scene;
            m_reportedFpsCorrectionFactor = scene.MinFrameSeconds * m_nominalReportedFps;
            m_statsUpdateFactor = (float)(m_statsUpdatesEveryMS / 1000);
            ReportingRegion = scene.RegionInfo;

            m_objectCapacity = scene.RegionInfo.ObjectCapacity;
            m_report.AutoReset = true;
            m_report.Interval = m_statsUpdatesEveryMS;
            m_report.Elapsed += TriggerStatsHeartbeat;
            m_report.Enabled = true;

            if (StatsManager.SimExtraStats != null)
            {
                OnSendStatsResult += StatsManager.SimExtraStats.ReceiveClassicSimStatsPacket;
            }

            /// At the moment, we'll only report if a frame is over 120% of target, since commonly frames are a bit
            /// longer than ideal (which in itself is a concern).
            SlowFramesStatReportThreshold = (int)Math.Ceiling(scene.MinFrameTicks * 1.2);

            SlowFramesStat
                = new Stat(
                    "SlowFrames",
                    "Slow Frames",
                    "Number of frames where frame time has been significantly longer than the desired frame time.",
                    " frames",
                    "scene",
                    m_scene.Name,
                    StatType.Push,
                    null,
                    StatVerbosity.Info);

            StatsManager.RegisterStat(SlowFramesStat);
        }


        public SimStatsReporter(Scene scene, int numberOfFrames) : this (scene)
        {
            // Store the number of frames from the OpenSim.ini configuration file
            m_numberFramesStored = numberOfFrames;
        }


        public void Close()
        {
            m_report.Elapsed -= TriggerStatsHeartbeat;
            m_report.Close();
        }

        /// <summary>
        /// Sets the number of milliseconds between stat updates.
        /// </summary>
        /// <param name='ms'></param>
        public void SetUpdateMS(int ms)
        {
            m_statsUpdatesEveryMS = ms;
            m_statsUpdateFactor = (float)(m_statsUpdatesEveryMS / 1000);
            m_report.Interval = m_statsUpdatesEveryMS;
        }

        private void TriggerStatsHeartbeat(object sender, EventArgs args)
        {
            try
            {
                statsHeartBeat(sender, args);
            }
            catch (Exception e)
            {
                m_log.Warn(string.Format(
                    "[SIM STATS REPORTER] Update for {0} failed with exception ",
                    m_scene.RegionInfo.RegionName), e);
            }
        }

        private void statsHeartBeat(object sender, EventArgs e)
        {
            double totalSumFrameTime;
            double simulationSumFrameTime;
            double physicsSumFrameTime;
            double networkSumFrameTime;
            double networkSumQueueSize;
            float frameDilation;
            int currentFrame;

            if (!m_scene.Active)
                return;

            // Create arrays to hold the statistics for this current scene,
            // these will be passed to the SimExtraStatsCollector, they are also
            // sent to the SimStats class
            SimStatsPacket.StatBlock[] sb = new
                SimStatsPacket.StatBlock[m_statisticArraySize];
            SimStatsPacket.RegionBlock rb = new SimStatsPacket.RegionBlock();
            
            // Know what's not thread safe in Mono... modifying timers.
            // m_log.Debug("Firing Stats Heart Beat");
            lock (m_report)
            {
                uint regionFlags = 0;
                
                try
                {
                    if (estateModule == null)
                        estateModule = m_scene.RequestModuleInterface<IEstateModule>();
                    regionFlags = estateModule != null ? estateModule.GetRegionFlags() : (uint) 0;
                }
                catch (Exception)
                {
                    // leave region flags at 0
                }

#region various statistic googly moogly

               // ORIGINAL code commented out until we have time to add our own
               // statistics to the statistics window, this will be done as a
               // new section given the title of our current project
                // We're going to lie about the FPS because we've been lying since 2008.  The actual FPS is currently
                // locked at a maximum of 11.  Maybe at some point this can change so that we're not lying.
                //int reportedFPS = (int)(m_fps * m_reportedFpsCorrectionFactor);
               int reportedFPS = m_fps;

                // save the reported value so there is something available for llGetRegionFPS 
                lastReportedSimFPS = reportedFPS / m_statsUpdateFactor;

               // ORIGINAL code commented out until we have time to add our own
               // statistics to the statistics window
                //float physfps = ((m_pfps / 1000));
               float physfps = m_numberPhysicsFrames;

                //if (physfps > 600)
                //physfps = physfps - (physfps - 600);

                if (physfps < 0)
                    physfps = 0;

#endregion

                m_rootAgents = m_scene.SceneGraph.GetRootAgentCount();
                m_childAgents = m_scene.SceneGraph.GetChildAgentCount();
                m_numPrim = m_scene.SceneGraph.GetTotalObjectsCount();
                m_numGeoPrim = m_scene.SceneGraph.GetTotalPrimObjectsCount();
                m_numMesh = m_scene.SceneGraph.GetTotalMeshObjectsCount();
                m_activePrim = m_scene.SceneGraph.GetActiveObjectsCount();
                m_activeScripts = m_scene.SceneGraph.GetActiveScriptsCount();

                // FIXME: Checking for stat sanity is a complex approach.  What we really need to do is fix the code
                // so that stat numbers are always consistent.
                CheckStatSanity();
                
                //Our time dilation is 0.91 when we're running a full speed,
                // therefore to make sure we get an appropriate range,
                // we have to factor in our error.   (0.10f * statsUpdateFactor)
                // multiplies the fix for the error times the amount of times it'll occur a second
                // / 10 divides the value by the number of times the sim heartbeat runs (10fps)
                // Then we divide the whole amount by the amount of seconds pass in between stats updates.

                // 'statsUpdateFactor' is how often stats packets are sent in seconds. Used below to change
                // values to X-per-second values.

                uint thisFrame = m_scene.Frame;
                float framesUpdated = (float)(thisFrame - m_lastUpdateFrame) * m_reportedFpsCorrectionFactor;
                m_lastUpdateFrame = thisFrame;

                // Avoid div-by-zero if somehow we've not updated any frames.
                if (framesUpdated == 0)
                    framesUpdated = 1;

                for (int i = 0; i < m_statisticArraySize; i++)
                {
                    sb[i] = new SimStatsPacket.StatBlock();
                }

                // Resetting the sums of the frame times to prevent any errors
                // in calculating the moving average for frame time
                totalSumFrameTime = 0;
                simulationSumFrameTime = 0;
                physicsSumFrameTime = 0;
                networkSumFrameTime = 0;
                networkSumQueueSize = 0;

                // Loop through all the frames that were stored for the current
                // heartbeat to process the moving average of frame times
                for (int i = 0; i < m_numberFramesStored; i++)
                {
                    // Sum up each frame time in order to calculate the moving
                    // average of frame time
                    totalSumFrameTime += m_totalFrameTimeMilliseconds[i];
                    simulationSumFrameTime +=
                        m_simulationFrameTimeMilliseconds[i];
                    physicsSumFrameTime += m_physicsFrameTimeMilliseconds[i];
                    networkSumFrameTime += m_networkFrameTimeMilliseconds[i];
                    networkSumQueueSize += m_networkQueueSize[i];
                }

                // Get the index that represents the current frame based on the next one known; go back
                // to the last index if next one is stated to restart at 0
                if (m_nextLocation == 0)
                    currentFrame = m_numberFramesStored - 1;
                else
                    currentFrame = m_nextLocation - 1;

                // Calculate the frame dilation; which is currently based on the ratio between the sum of the
                // physics and simulation rate, and the set minimum time to run a scene's frame
                frameDilation = (float)(m_simulationFrameTimeMilliseconds[currentFrame] +
                   m_physicsFrameTimeMilliseconds[currentFrame]) / m_scene.MinFrameTicks;

                // ORIGINAL code commented out until we have time to add our own
                sb[0].StatID = (uint) Stats.TimeDilation;
                sb[0].StatValue = (Single.IsNaN(m_timeDilation)) ? 0.1f : m_timeDilation ; //((((m_timeDilation + (0.10f * statsUpdateFactor)) /10)  / statsUpdateFactor));

                sb[1].StatID = (uint) Stats.SimFPS;
                sb[1].StatValue = reportedFPS / m_statsUpdateFactor;

                sb[2].StatID = (uint) Stats.PhysicsFPS;
                sb[2].StatValue = physfps / m_statsUpdateFactor;

                sb[3].StatID = (uint) Stats.AgentUpdates;
                sb[3].StatValue = (m_agentUpdates / m_statsUpdateFactor);

                sb[4].StatID = (uint) Stats.Agents;
                sb[4].StatValue = m_rootAgents;

                sb[5].StatID = (uint) Stats.ChildAgents;
                sb[5].StatValue = m_childAgents;

                sb[6].StatID = (uint) Stats.TotalPrim;
                sb[6].StatValue = m_numPrim;

                sb[7].StatID = (uint) Stats.ActivePrim;
                sb[7].StatValue = m_activePrim;

               // ORIGINAL code commented out until we have time to add our own
               // statistics to the statistics window
                sb[8].StatID = (uint)Stats.FrameMS;
                //sb[8].StatValue = m_frameMS / framesUpdated;
               sb[8].StatValue = (float) totalSumFrameTime / m_numberFramesStored;

                sb[9].StatID = (uint)Stats.NetMS;
                //sb[9].StatValue = m_netMS / framesUpdated;
               sb[9].StatValue = (float) networkSumFrameTime / m_numberFramesStored;

                sb[10].StatID = (uint)Stats.PhysicsMS;
                //sb[10].StatValue = m_physicsMS / framesUpdated;
               sb[10].StatValue = (float) physicsSumFrameTime / m_numberFramesStored;

                sb[11].StatID = (uint)Stats.ImageMS ;
                sb[11].StatValue = m_imageMS / framesUpdated;

                sb[12].StatID = (uint)Stats.OtherMS;
                //sb[12].StatValue = m_otherMS / framesUpdated;
               sb[12].StatValue = (float) simulationSumFrameTime /
                  m_numberFramesStored;

                sb[13].StatID = (uint)Stats.InPacketsPerSecond;
                sb[13].StatValue = (float) m_inPacketsPerSecond;

                sb[14].StatID = (uint)Stats.OutPacketsPerSecond;
                sb[14].StatValue = (float) m_outPacketsPerSecond;

                sb[15].StatID = (uint)Stats.UnAckedBytes;
                sb[15].StatValue = m_unAckedBytes;

                sb[16].StatID = (uint)Stats.AgentMS;
                sb[16].StatValue = m_agentMS / framesUpdated;

                sb[17].StatID = (uint)Stats.PendingDownloads;
                sb[17].StatValue = m_pendingDownloads;

                sb[18].StatID = (uint)Stats.PendingUploads;
                sb[18].StatValue = m_pendingUploads;

                sb[19].StatID = (uint)Stats.ActiveScripts;
                sb[19].StatValue = m_activeScripts;

                sb[20].StatID = (uint)Stats.ScriptLinesPerSecond;
                sb[20].StatValue = m_scriptLinesPerSecond / m_statsUpdateFactor;

                sb[21].StatID = (uint)Stats.SimSpareMs;
                sb[21].StatValue = m_spareMS / framesUpdated;

                // Current ratio between the sum of physics and sim rate, and the
                // minimum time to run a scene's frame
                sb[22].StatID = (uint)Stats.FrameDilation;
                sb[22].StatValue = frameDilation;

                // Current number of users currently attemptint to login to region
                sb[23].StatID = (uint)Stats.UsersLoggingIn;
                sb[23].StatValue = m_usersLoggingIn.Count;

                // Total number of geometric primitives in the scene
                sb[24].StatID = (uint)Stats.TotalGeoPrim;
                sb[24].StatValue = m_numGeoPrim;

                // Total number of mesh objects in the scene
                sb[25].StatID = (uint)Stats.TotalMesh;
                sb[25].StatValue = m_numMesh;

                // Current number of threads that XEngine is using
                sb[26].StatID = (uint)Stats.ThreadCount;
                sb[26].StatValue = m_inUseThreads;
                
                // Tracks the number of bytes that are received by the server's
                // UDP network handler
                sb[27].StatID = (uint)Stats.UDPInRate;
                sb[27].StatValue = (float) m_inByteRate;
                
                // Tracks the number of bytes that are sent by the server's UDP 
                // network handler
                sb[28].StatID = (uint)Stats.UDPOutRate;
                sb[28].StatValue = (float) m_outByteRate;
                
                // Tracks the number of packets that were received by the 
                // server's UDP network handler, that were unable to be processed
                sb[29].StatID = (uint)Stats.UDPErrorRate;
                sb[29].StatValue = (float) m_errorPacketRate;

                // Track the queue size of the network as a moving average
                sb[30].StatID = (uint)Stats.NetworkQueueSize;
                sb[30].StatValue = (float) networkSumQueueSize / 
                    m_numberFramesStored;

                // Current average ping between the server and a subset of its conneced users
                sb[31].StatID = (uint)Stats.ClientPingAvg;
                sb[31].StatValue = (float) m_clientPing;
                
                for (int i = 0; i < m_statisticArraySize; i++)
                {
                    lastReportedSimStats[i] = sb[i].StatValue;
                }
              
                SimStats simStats 
                    = new SimStats(
                        ReportingRegion.RegionLocX, ReportingRegion.RegionLocY, regionFlags, (uint)m_objectCapacity,
                        rb, sb, m_scene.RegionInfo.originRegionID);

                handlerSendStatResult = OnSendStatsResult;
                if (handlerSendStatResult != null)
                {
                    handlerSendStatResult(simStats);
                }

                // Extra statistics that aren't currently sent to clients
                lock (m_lastReportedExtraSimStats)
                {
                    m_lastReportedExtraSimStats[LastReportedObjectUpdateStatName] = m_objectUpdates / m_statsUpdateFactor;
                    m_lastReportedExtraSimStats[SlowFramesStat.ShortName] = (float)SlowFramesStat.Value;

                    Dictionary<string, float> physicsStats = m_scene.PhysicsScene.GetStats();
    
                    if (physicsStats != null)
                    {
                        foreach (KeyValuePair<string, float> tuple in physicsStats)
                        {
                            // FIXME: An extremely dirty hack to divide MS stats per frame rather than per second
                            // Need to change things so that stats source can indicate whether they are per second or
                            // per frame.
                            if (tuple.Key.EndsWith("MS"))
                                m_lastReportedExtraSimStats[tuple.Key] = tuple.Value / framesUpdated;
                            else
                                m_lastReportedExtraSimStats[tuple.Key] = tuple.Value / m_statsUpdateFactor;
                        }
                    }
                }

                ResetValues();
            }
        }

        private void ResetValues()
        {
            // Reset the number of frames that the physics library has
            // processed since the last stats report
            m_numberPhysicsFrames = 0;

            m_timeDilation = 0;
            m_fps = 0;
            m_pfps = 0;
            m_agentUpdates = 0;
            m_objectUpdates = 0;
            //m_inPacketsPerSecond = 0;
            //m_outPacketsPerSecond = 0;
            m_unAckedBytes = 0;
            m_scriptLinesPerSecond = 0;

            m_frameMS = 0;
            m_agentMS = 0;
            m_netMS = 0;
            m_physicsMS = 0;
            m_imageMS = 0;
            m_otherMS = 0;
            m_spareMS = 0;

//Ckrinke This variable is not used, so comment to remove compiler warning until it is used.
//Ckrinke            m_scriptMS = 0;
        }

        # region methods called from Scene
        // The majority of these functions are additive
        // so that you can easily change the amount of
        // seconds in between sim stats updates

        public void AddTimeDilation(float td)
        {
            //float tdsetting = td;
            //if (tdsetting > 1.0f)
                //tdsetting = (tdsetting - (tdsetting - 0.91f));

            //if (tdsetting < 0)
                //tdsetting = 0.0f;
            m_timeDilation = td;
        }

        internal void CheckStatSanity()
        {
            if (m_rootAgents < 0 || m_childAgents < 0)
            {
                handlerStatsIncorrect = OnStatsIncorrect;
                if (handlerStatsIncorrect != null)
                {
                    handlerStatsIncorrect();
                }
            }
            if (m_rootAgents == 0 && m_childAgents == 0)
            {
                m_unAckedBytes = 0;
            }
        }

        public void AddFPS(int frames)
        {
            m_fps += frames;
        }

        public void AddPhysicsFPS(float frames)
        {
            m_pfps += frames;
        }

        public void AddObjectUpdates(int numUpdates)
        {
            m_objectUpdates += numUpdates;
        }

        public void AddAgentUpdates(int numUpdates)
        {
            m_agentUpdates += numUpdates;
        }

        public void AddInPackets(int numPackets)
        {
            m_inPacketsPerSecond = numPackets;
        }

        public void AddOutPackets(int numPackets)
        {
            m_outPacketsPerSecond = numPackets;
        }

        public void AddunAckedBytes(int numBytes)
        {
            m_unAckedBytes += numBytes;
            if (m_unAckedBytes < 0) m_unAckedBytes = 0;
        }

        public void addFrameMS(int ms)
        {
            m_frameMS += ms;

            // At the moment, we'll only report if a frame is over 120% of target, since commonly frames are a bit
            // longer than ideal due to the inaccuracy of the Sleep in Scene.Update() (which in itself is a concern).
            if (ms > SlowFramesStatReportThreshold)
                SlowFramesStat.Value++;
        }

        public void AddSpareMS(int ms)
        {
            m_spareMS += ms;
        }

        public void addNetMS(int ms)
        {
            m_netMS += ms;
        }

        public void addAgentMS(int ms)
        {
            m_agentMS += ms;
        }

        public void addPhysicsMS(int ms)
        {
            m_physicsMS += ms;
        }

        public void addImageMS(int ms)
        {
            m_imageMS += ms;
        }

        public void addOtherMS(int ms)
        {
            m_otherMS += ms;
        }

      public void addPhysicsFrame(int frames)
      {
         // Add the number of physics frames to the correct total physics
         // frames
         m_numberPhysicsFrames += frames;
      }

      public void addFrameTimeMilliseconds(double total, double simulation,
         double physics)
      {
         // Save the frame times from the current frame into the appropriate
         // arrays
         m_totalFrameTimeMilliseconds[m_nextLocation] = total;
         m_simulationFrameTimeMilliseconds[m_nextLocation] = simulation;
         m_physicsFrameTimeMilliseconds[m_nextLocation] = physics;

         // Update to the next location in the list
         m_nextLocation++;

         // Since the list will begin to overwrite the oldest frame values
         // first, the next location needs to loop back to the beginning of the
         // list whenever it reaches the end
         m_nextLocation = m_nextLocation % m_numberFramesStored;
      }

        public void AddPendingDownloads(int count)
        {
            m_pendingDownloads += count;

            if (m_pendingDownloads < 0)
                m_pendingDownloads = 0;

            //m_log.InfoFormat("[stats]: Adding {0} to pending downloads to make {1}", count, m_pendingDownloads);
        }

        public void addScriptLines(int count)
        {
            m_scriptLinesPerSecond += count;
        }

        public void AddPacketsStats(double inPacketRate, double outPacketRate, 
            int unAckedBytes, double inByteRate, double outByteRate, 
            double errorPacketRate)
        {
            m_inPacketsPerSecond = inPacketRate;
            m_outPacketsPerSecond = outPacketRate;
            AddunAckedBytes(unAckedBytes);
            m_inByteRate = inByteRate;
            m_outByteRate = outByteRate;
            m_errorPacketRate = errorPacketRate;
        }
        
        public void AddPacketProcessStats(double processTime, int queueSize)
        {
            // Store the time that it took to process the most recent UDP 
            // message and the size of the UDP network in queue
            m_networkFrameTimeMilliseconds[m_netLocation] = processTime;
            m_networkQueueSize[m_netLocation] = queueSize;
            
            m_netLocation++;
            
            // Since the list will begin to overwrite the oldest frame values
            // first, the network location needs to loop back to the beginning 
            // of the list whenever it reaches the end
            m_netLocation = m_netLocation % m_numberFramesStored;
        }

        public void AddUserLoggingIn(string name)
        {
            // Check that the name does not exist in the list of users logging
            // in, this prevents the case of the user disconnecting while
            // logging in and reconnecting from adding multiple instances of
            // the user
            if (!m_usersLoggingIn.Contains(name))
            {
                // Add the name of the user attempting to connect to the server
                // to our list, this will allow tracking of which users have
                // succesfully updated the texture of their avatar
                m_usersLoggingIn.Add(name);
            }
        }

        public void RemoveUserLoggingIn(string name)
        {
            // Remove the user that has finished logging into the server, if
            // the name doesn't exist no change to the array list occurs
            m_usersLoggingIn.Remove(name);
        }

        public void SetThreadCount(int inUseThreads)
        {
            // Save the new number of threads to our member variable to send to
            // the extra stats collector
            m_inUseThreads = inUseThreads;
        }

        public void AddClientPingTime(double pingTime, int subset)
        {
            // Keep track of the total ping time from various clients
            m_totalPingTime += pingTime;

            // Increment the number of clients pinged and check to see if we've reached
            // the desired number of clients
            m_clientPingCount++;
            if (m_clientPingCount >= subset)
            {
                // Calculate the ping average between the server and its connected clients
                m_clientPing = m_totalPingTime / (double)m_clientPingCount;

                // Reset the client count and the total ping time
                m_clientPingCount = 0;
                m_totalPingTime = 0;
            }
        }

        public void AddNewAgent(string name, string ipAddress, string timestamp)
        {
            // Report the new agent being added to the additional stats collector,
            // if the extra stats collector exists
            if (StatsManager.SimExtraStats != null)
                StatsManager.SimExtraStats.AddAgent(name, ipAddress, timestamp);
        }

        public void RemoveAgent(string name)
        {
            // Report the agent being removed to the additional stats collector,
            // if the extra stats collector exists
            if (StatsManager.SimExtraStats != null)
                StatsManager.SimExtraStats.RemoveAgent(name);
        }

        #endregion

        public Dictionary<string, float> GetExtraSimStats()
        {
            lock (m_lastReportedExtraSimStats)
                return new Dictionary<string, float>(m_lastReportedExtraSimStats);
        }
    }
}
