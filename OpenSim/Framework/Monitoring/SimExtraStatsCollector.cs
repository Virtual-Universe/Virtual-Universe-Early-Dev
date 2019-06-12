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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Timers;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework.Monitoring.Interfaces;

namespace OpenSim.Framework.Monitoring
{
    /// <summary>
    /// Collects sim statistics which aren't already being collected for the linden viewer's statistics pane
    /// </summary>
    public class SimExtraStatsCollector : BaseStatsCollector
    {
//        private long assetsInCache;
//        private long texturesInCache;
//        private long assetCacheMemoryUsage;
//        private long textureCacheMemoryUsage;
//        private TimeSpan assetRequestTimeAfterCacheMiss;
//        private long blockedMissingTextureRequests;

//        private long assetServiceRequestFailures;
//        private long inventoryServiceRetrievalFailures;

        private Ping m_externalPingSender;
        private Timer m_externalPingTimer;
        private string m_externalServerName;
        private double m_externalPingFreq;
        private double m_avgPing = 0.0;
        private bool m_pingCompleted;

        private const string m_defaultServerName = "www.google.com";
        private const double m_pingFrequency = 3.0;

        private volatile float timeDilation;
        private volatile float simFps;
        private volatile float physicsFps;
        private volatile float agentUpdates;
        private volatile float rootAgents;
        private volatile float childAgents;
        private volatile float totalPrims;
        private volatile float activePrims;
        private volatile float totalFrameTime;
        private volatile float netFrameTime;
        private volatile float physicsFrameTime;
        private volatile float otherFrameTime;
        private volatile float imageFrameTime;
        private volatile float inPacketsPerSecond;
        private volatile float outPacketsPerSecond;
        private volatile float unackedBytes;
        private volatile float agentFrameTime;
        private volatile float pendingDownloads;
        private volatile float pendingUploads;
        private volatile float activeScripts;
        private volatile float scriptLinesPerSecond;
        private volatile float m_frameDilation;
        private volatile float m_usersLoggingIn;
        private volatile float m_totalGeoPrims;
        private volatile float m_totalMeshes;
        private volatile float m_inUseThreads;
        private volatile float m_inByteRate;
        private volatile float m_outByteRate;
        private volatile float m_errorPacketRate;
        private volatile float m_queueSize;
        private volatile float m_clientPing;

//        /// <summary>
//        /// These statistics are being collected by push rather than pull.  Pull would be simpler, but I had the
//        /// notion of providing some flow statistics (which pull wouldn't give us).  Though admittedly these
//        /// haven't yet been implemented...
//        /// </summary>
//        public long AssetsInCache { get { return assetsInCache; } }
//        
//        /// <value>
//        /// Currently unused
//        /// </value>
//        public long TexturesInCache { get { return texturesInCache; } }
//        
//        /// <value>
//        /// Currently misleading since we can't currently subtract removed asset memory usage without a performance hit
//        /// </value>
//        public long AssetCacheMemoryUsage { get { return assetCacheMemoryUsage; } }
//        
//        /// <value>
//        /// Currently unused
//        /// </value>
//        public long TextureCacheMemoryUsage { get { return textureCacheMemoryUsage; } }

        public float TimeDilation { get { return timeDilation; } }
        public float SimFps { get { return simFps; } }
        public float PhysicsFps { get { return physicsFps; } }
        public float AgentUpdates { get { return agentUpdates; } }
        public float RootAgents { get { return rootAgents; } }
        public float ChildAgents { get { return childAgents; } }
        public float TotalPrims { get { return totalPrims; } }
        public float ActivePrims { get { return activePrims; } }
        public float TotalFrameTime { get { return totalFrameTime; } }
        public float NetFrameTime { get { return netFrameTime; } }
        public float PhysicsFrameTime { get { return physicsFrameTime; } }
        public float OtherFrameTime { get { return otherFrameTime; } }
        public float ImageFrameTime { get { return imageFrameTime; } }
        public float InPacketsPerSecond { get { return inPacketsPerSecond; } }
        public float OutPacketsPerSecond { get { return outPacketsPerSecond; } }
        public float UnackedBytes { get { return unackedBytes; } }
        public float AgentFrameTime { get { return agentFrameTime; } }
        public float PendingDownloads { get { return pendingDownloads; } }
        public float PendingUploads { get { return pendingUploads; } }
        public float ActiveScripts { get { return activeScripts; } }
        public float ScriptLinesPerSecond { get { return scriptLinesPerSecond; } }

//        /// <summary>
//        /// This is the time it took for the last asset request made in response to a cache miss.
//        /// </summary>
//        public TimeSpan AssetRequestTimeAfterCacheMiss { get { return assetRequestTimeAfterCacheMiss; } }
//
//        /// <summary>
//        /// Number of persistent requests for missing textures we have started blocking from clients.  To some extent
//        /// this is just a temporary statistic to keep this problem in view - the root cause of this lies either
//        /// in a mishandling of the reply protocol, related to avatar appearance or may even originate in graphics
//        /// driver bugs on clients (though this seems less likely).
//        /// </summary>
//        public long BlockedMissingTextureRequests { get { return blockedMissingTextureRequests; } }
//
//        /// <summary>
//        /// Record the number of times that an asset request has failed.  Failures are effectively exceptions, such as
//        /// request timeouts.  If an asset service replies that a particular asset cannot be found, this is not counted
//        /// as a failure
//        /// </summary>
//        public long AssetServiceRequestFailures { get { return assetServiceRequestFailures; } }

        /// <summary>
        /// Number of known failures to retrieve avatar inventory from the inventory service.  This does not
        /// cover situations where the inventory service accepts the request but never returns any data, since
        /// we do not yet timeout this situation.
        /// </summary>
        /// <remarks>Commented out because we do not cache inventory at this point</remarks>
//        public long InventoryServiceRetrievalFailures { get { return inventoryServiceRetrievalFailures; } }

        /// <summary>
        /// Retrieve the total frame time (in ms) of the last frame
        /// </summary>
        //public float TotalFrameTime { get { return totalFrameTime; } }

        /// <summary>
        /// Retrieve the physics update component (in ms) of the last frame
        /// </summary>
        //public float PhysicsFrameTime { get { return physicsFrameTime; } }

        /// <summary>
        /// Retain a dictionary of all packet queues stats reporters
        /// </summary>
        private IDictionary<UUID, PacketQueueStatsCollector> packetQueueStatsCollectors
            = new Dictionary<UUID, PacketQueueStatsCollector>();

        /// <summary>
        /// List of various statistical data of connected agents.
        /// </summary>
        private List<AgentSimData> agentList = new List<AgentSimData>();

        public SimExtraStatsCollector()
        {
            // Default constructor
        }

        public SimExtraStatsCollector(IConfigSource config)
        {
            // Acquire the statistics section of the OpenSim.ini file and check to see if it
            // exists
            IConfig statsConfig = config.Configs["Statistics"];
            if (statsConfig != null)
            {
                // Check if the configuration enables pinging the external server; disabled
                // by default
                bool pingServer = statsConfig.GetBoolean("PingExternalServerEnabled", false);

                // Get the rest of the values, for ping requests, if enabled
                if (pingServer)
                {
                    // Get the name for the external server to ping and the frequency to ping it; use
                    // the default constant values of neither were found in the configuration
                    m_externalServerName = statsConfig.GetString("ExternalServer", m_defaultServerName);
                    m_externalPingFreq = statsConfig.GetDouble("ExternalPingFrequency", m_pingFrequency);

                    // Begin pinging the external server
                    StartPingRequests();
                }
            }
        }

        ~SimExtraStatsCollector()
        {
            // Stop the timer to ping the external server
            if (m_externalPingTimer != null)
               m_externalPingTimer.Stop();
        }

//        public void AddAsset(AssetBase asset)
//        {
//            assetsInCache++;
//            //assetCacheMemoryUsage += asset.Data.Length;
//        }
//        
//        public void RemoveAsset(UUID uuid)
//        {
//            assetsInCache--;
//        }
//
//        public void AddTexture(AssetBase image)
//        {
//            if (image.Data != null)
//            {
//                texturesInCache++;
//
//                // This could have been a pull stat, though there was originally a nebulous idea to measure flow rates
//                textureCacheMemoryUsage += image.Data.Length;
//            }
//        }
//
//        /// <summary>
//        /// Signal that the asset cache has been cleared.
//        /// </summary>
//        public void ClearAssetCacheStatistics()
//        {
//            assetsInCache = 0;
//            assetCacheMemoryUsage = 0;
//            texturesInCache = 0;
//            textureCacheMemoryUsage = 0;
//        }
//        
//        public void AddAssetRequestTimeAfterCacheMiss(TimeSpan ts)
//        {
//            assetRequestTimeAfterCacheMiss = ts;
//        }
//
//        public void AddBlockedMissingTextureRequest()
//        {
//            blockedMissingTextureRequests++;
//        }
//
//        public void AddAssetServiceRequestFailure()
//        {
//            assetServiceRequestFailures++;
//        }

//        public void AddInventoryServiceRetrievalFailure()
//        {
//            inventoryServiceRetrievalFailures++;
//        }

        public void AddAgent(string name, string ipAddress, string timestamp)
        {
            // Save new agent data to the list of connected agents
            AgentSimData agentSimData = new AgentSimData(name, ipAddress, timestamp);
            agentList.Add(agentSimData);
        }

        public void RemoveAgent(string name)
        {
            // Search for the agent being removed in the list of agents currently connected to the server
            foreach (AgentSimData agent in agentList)
            {
                // Check if the given name matches the current one in the list
                if (agent.Name.CompareTo(name) == 0)
                {
                    // Agent found, so remove them from the list and exit
                    agentList.Remove(agent);
                    return;
                }
            }
        }

        /// <summary>
        /// Register as a packet queue stats provider
        /// </summary>
        /// <param name="uuid">An agent UUID</param>
        /// <param name="provider"></param>
        public void RegisterPacketQueueStatsProvider(UUID uuid, IPullStatsProvider provider)
        {
            lock (packetQueueStatsCollectors)
            {
                // FIXME: If the region service is providing more than one region, then the child and root agent
                // queues are wrongly replacing each other here.
                packetQueueStatsCollectors[uuid] = new PacketQueueStatsCollector(provider);
            }
        }

        /// <summary>
        /// Deregister a packet queue stats provider
        /// </summary>
        /// <param name="uuid">An agent UUID</param>
        public void DeregisterPacketQueueStatsProvider(UUID uuid)
        {
            lock (packetQueueStatsCollectors)
            {
                packetQueueStatsCollectors.Remove(uuid);
            }
        }

        /// <summary>
        /// This is the method on which the classic sim stats reporter (which collects stats for
        /// client purposes) sends information to listeners.
        /// </summary>
        /// <param name="pack"></param>
        public void ReceiveClassicSimStatsPacket(SimStats stats)
        {
            // FIXME: SimStats shouldn't allow an arbitrary stat packing order (which is inherited from the original
            // SimStatsPacket that was being used).

            // For an unknown reason the original designers decided not to
            // include the spare MS statistic inside of this class, this is
            // located inside the StatsBlock at location 21, thus it is skipped
            timeDilation            = stats.StatsBlock[0].StatValue;
            simFps                  = stats.StatsBlock[1].StatValue;
            physicsFps              = stats.StatsBlock[2].StatValue;
            agentUpdates            = stats.StatsBlock[3].StatValue;
            rootAgents              = stats.StatsBlock[4].StatValue;
            childAgents             = stats.StatsBlock[5].StatValue;
            totalPrims              = stats.StatsBlock[6].StatValue;
            activePrims             = stats.StatsBlock[7].StatValue;
            totalFrameTime          = stats.StatsBlock[8].StatValue;
            netFrameTime            = stats.StatsBlock[9].StatValue;
            physicsFrameTime        = stats.StatsBlock[10].StatValue;
            otherFrameTime          = stats.StatsBlock[11].StatValue;
            imageFrameTime          = stats.StatsBlock[12].StatValue;
            inPacketsPerSecond      = stats.StatsBlock[13].StatValue;
            outPacketsPerSecond     = stats.StatsBlock[14].StatValue;
            unackedBytes            = stats.StatsBlock[15].StatValue;
            agentFrameTime          = stats.StatsBlock[16].StatValue;
            pendingDownloads        = stats.StatsBlock[17].StatValue;
            pendingUploads          = stats.StatsBlock[18].StatValue;
            activeScripts           = stats.StatsBlock[19].StatValue;
            scriptLinesPerSecond    = stats.StatsBlock[20].StatValue;
            m_frameDilation         = stats.StatsBlock[22].StatValue;
            m_usersLoggingIn        = stats.StatsBlock[23].StatValue;
            m_totalGeoPrims         = stats.StatsBlock[24].StatValue;
            m_totalMeshes           = stats.StatsBlock[25].StatValue;
            m_inUseThreads          = stats.StatsBlock[26].StatValue;
            m_inByteRate            = stats.StatsBlock[27].StatValue;
            m_outByteRate           = stats.StatsBlock[28].StatValue;
            m_errorPacketRate       = stats.StatsBlock[29].StatValue;
            m_queueSize             = stats.StatsBlock[30].StatValue;
            m_clientPing            = stats.StatsBlock[31].StatValue;
        }

        /// <summary>
        /// Report back collected statistical information.
        /// </summary>
        /// <returns></returns>
        public override string Report()
        {
            StringBuilder sb = new StringBuilder(Environment.NewLine);
//            sb.Append("ASSET STATISTICS");
//            sb.Append(Environment.NewLine);
                        
            /*
            sb.Append(
                string.Format(
@"Asset cache contains   {0,6} non-texture assets using {1,10} K
Texture cache contains {2,6} texture     assets using {3,10} K
Latest asset request time after cache miss: {4}s
Blocked client requests for missing textures: {5}
Asset service request failures: {6}"+ Environment.NewLine,
                    AssetsInCache, Math.Round(AssetCacheMemoryUsage / 1024.0),
                    TexturesInCache, Math.Round(TextureCacheMemoryUsage / 1024.0),
                    assetRequestTimeAfterCacheMiss.Milliseconds / 1000.0,
                    BlockedMissingTextureRequests,
                    AssetServiceRequestFailures));
            */

            /*
            sb.Append(
                string.Format(
@"Asset cache contains   {0,6} assets
Latest asset request time after cache miss: {1}s
Blocked client requests for missing textures: {2}
Asset service request failures: {3}" + Environment.NewLine,
                    AssetsInCache,
                    assetRequestTimeAfterCacheMiss.Milliseconds / 1000.0,
                    BlockedMissingTextureRequests,
                    AssetServiceRequestFailures));
                    */

            sb.Append(Environment.NewLine);
            sb.Append("CONNECTION STATISTICS");
            sb.Append(Environment.NewLine);

            List<Stat> stats = StatsManager.GetStatsFromEachContainer("clientstack", "ClientLogoutsDueToNoReceives");

            sb.AppendFormat(
                "Client logouts due to no data receive timeout: {0}\n\n", 
                stats != null ? stats.Sum(s => s.Value).ToString() : "unknown");

//            sb.Append(Environment.NewLine);
//            sb.Append("INVENTORY STATISTICS");
//            sb.Append(Environment.NewLine);
//            sb.Append(
//                string.Format(
//                    "Initial inventory caching failures: {0}" + Environment.NewLine,
//                    InventoryServiceRetrievalFailures));

            sb.Append(Environment.NewLine);
            sb.Append("SAMPLE FRAME STATISTICS");
            sb.Append(Environment.NewLine);
            sb.Append("Dilatn  SimFPS  PhyFPS  AgntUp  RootAg  ChldAg  Prims   AtvPrm  AtvScr  ScrLPS");
            sb.Append(Environment.NewLine);
            sb.Append(
                string.Format(
                    "{0,6:0.00}  {1,6:0}  {2,6:0.0}  {3,6:0.0}  {4,6:0}  {5,6:0}  {6,6:0}  {7,6:0}  {8,6:0}  {9,6:0}",
                    timeDilation, simFps, physicsFps, agentUpdates, rootAgents,
                    childAgents, totalPrims, activePrims, activeScripts, scriptLinesPerSecond));

            sb.Append(Environment.NewLine);
            sb.Append(Environment.NewLine);
            // There is no script frame time currently because we don't yet collect it
            sb.Append("PktsIn  PktOut  PendDl  PendUl  UnackB  TotlFt  NetFt   PhysFt  OthrFt  AgntFt  ImgsFt");
            sb.Append(Environment.NewLine);
            sb.Append(
                string.Format(
                    "{0,6:0}  {1,6:0}  {2,6:0}  {3,6:0}  {4,6:0}  {5,6:0.0}  {6,6:0.0}  {7,6:0.0}  {8,6:0.0}  {9,6:0.0}  {10,6:0.0}\n\n",
                    inPacketsPerSecond, outPacketsPerSecond, pendingDownloads, pendingUploads, unackedBytes, totalFrameTime,
                    netFrameTime, physicsFrameTime, otherFrameTime, agentFrameTime, imageFrameTime));

            /* 20130319 RA: For the moment, disable the dump of 'scene' catagory as they are mostly output by
             * the two formatted printouts above.
            SortedDictionary<string, SortedDictionary<string, Stat>> sceneStats;
            if (StatsManager.TryGetStats("scene", out sceneStats))
            {
                foreach (KeyValuePair<string, SortedDictionary<string, Stat>> kvp in sceneStats)
                {
                    foreach (Stat stat in kvp.Value.Values)
                    {
                        if (stat.Verbosity == StatVerbosity.Info)
                        {
                            sb.AppendFormat("{0} ({1}): {2}{3}\n", stat.Name, stat.Container, stat.Value, stat.UnitName);
                        }
                    }
                }
            }
             */

            /*
            sb.Append(Environment.NewLine);
            sb.Append("PACKET QUEUE STATISTICS");
            sb.Append(Environment.NewLine);
            sb.Append("Agent UUID                          ");
            sb.Append(
                string.Format(
                    "  {0,7}  {1,7}  {2,7}  {3,7}  {4,7}  {5,7}  {6,7}  {7,7}  {8,7}  {9,7}",
                    "Send", "In", "Out", "Resend", "Land", "Wind", "Cloud", "Task", "Texture", "Asset"));
            sb.Append(Environment.NewLine);

            foreach (UUID key in packetQueueStatsCollectors.Keys)
            {
                sb.Append(string.Format("{0}: ", key));
                sb.Append(packetQueueStatsCollectors[key].Report());
                sb.Append(Environment.NewLine);
            }
            */

            sb.Append(base.Report());

            return sb.ToString();
        }

        /// <summary>
        /// Report back collected statistical information as json serialization.
        /// </summary>
        /// <returns></returns>
        public override string XReport(string uptime, string version)
        {
            return OSDParser.SerializeJsonString(OReport(uptime, version));
        }

        /// <summary>
        /// Report back collected statistical information as an OSDMap
        /// </summary>
        /// <returns></returns>
        public override OSDMap OReport(string uptime, string version)
        {
            // Get the amount of physical memory, allocated with the instance of this program, in kilobytes;
            // the working set is the set of memory pages currently visible to this program in physical RAM
            // memory and includes both shared (e.g. system libraries) and private data
            double memUsage = Process.GetCurrentProcess().WorkingSet64 / 1024.0;

            // Get the number of threads from the system that are currently
            // running
            int numberThreadsRunning = 0;
            foreach (ProcessThread currentThread in
                Process.GetCurrentProcess().Threads)
            {
                // A known issue with the current .Threads property is that it 
                // can return null threads, thus don't count those as running
                // threads and prevent the program function from failing
                if (currentThread != null && 
                    currentThread.ThreadState == ThreadState.Running)
                {
                    numberThreadsRunning++;
                }
            }

            OSDMap args = new OSDMap(30);
//            args["AssetsInCache"] = OSD.FromString (String.Format ("{0:0.##}", AssetsInCache));
//            args["TimeAfterCacheMiss"] = OSD.FromString (String.Format ("{0:0.##}",
//                    assetRequestTimeAfterCacheMiss.Milliseconds / 1000.0));
//            args["BlockedMissingTextureRequests"] = OSD.FromString (String.Format ("{0:0.##}",
//                    BlockedMissingTextureRequests));
//            args["AssetServiceRequestFailures"] = OSD.FromString (String.Format ("{0:0.##}",
//                    AssetServiceRequestFailures));
//            args["abnormalClientThreadTerminations"] = OSD.FromString (String.Format ("{0:0.##}",
//                    abnormalClientThreadTerminations));
//            args["InventoryServiceRetrievalFailures"] = OSD.FromString (String.Format ("{0:0.##}",
//                    InventoryServiceRetrievalFailures));
            args["Dilatn"] = OSD.FromString (String.Format ("{0:0.##}", timeDilation));
            args["SimFPS"] = OSD.FromString (String.Format ("{0:0.##}", simFps));
            args["PhyFPS"] = OSD.FromString (String.Format ("{0:0.##}", physicsFps));
            args["AgntUp"] = OSD.FromString (String.Format ("{0:0.##}", agentUpdates));
            args["RootAg"] = OSD.FromString (String.Format ("{0:0.##}", rootAgents));
            args["ChldAg"] = OSD.FromString (String.Format ("{0:0.##}", childAgents));
            args["Prims"] = OSD.FromString (String.Format ("{0:0.##}", totalPrims));
            args["AtvPrm"] = OSD.FromString (String.Format ("{0:0.##}", activePrims));
            args["AtvScr"] = OSD.FromString (String.Format ("{0:0.##}", activeScripts));
            args["ScrLPS"] = OSD.FromString (String.Format ("{0:0.##}", scriptLinesPerSecond));
            args["PktsIn"] = OSD.FromString (String.Format ("{0:0.##}", inPacketsPerSecond));
            args["PktOut"] = OSD.FromString (String.Format ("{0:0.##}", outPacketsPerSecond));
            args["PendDl"] = OSD.FromString (String.Format ("{0:0.##}", pendingDownloads));
            args["PendUl"] = OSD.FromString (String.Format ("{0:0.##}", pendingUploads));
            args["UnackB"] = OSD.FromString (String.Format ("{0:0.##}", unackedBytes));
            args["TotlFt"] = OSD.FromString (String.Format ("{0:0.##}", totalFrameTime));
            args["NetEvtTime"] = OSD.FromString (String.Format ("{0:0.##}", 
                netFrameTime));
            args["NetQSize"] = OSD.FromString(String.Format("{0:0.##}", 
                m_queueSize));
            args["PhysFt"] = OSD.FromString (String.Format ("{0:0.##}", physicsFrameTime));
            args["OthrFt"] = OSD.FromString (String.Format ("{0:0.##}", otherFrameTime));
            args["AgntFt"] = OSD.FromString (String.Format ("{0:0.##}", agentFrameTime));
            args["ImgsFt"] = OSD.FromString (String.Format ("{0:0.##}", imageFrameTime));
            args["Memory"] = OSD.FromString (base.XReport (uptime, version));
            args["Uptime"] = OSD.FromString (uptime);
            args["Version"] = OSD.FromString (version);

            args["FrameDilatn"] = OSD.FromString(String.Format("{0:0.##}", m_frameDilation));
            args["Logging in Users"] = OSD.FromString(String.Format("{0:0.##}",
                m_usersLoggingIn));
            args["GeoPrims"] = OSD.FromString(String.Format("{0:0.##}",
                m_totalGeoPrims));
            args["Mesh Objects"] = OSD.FromString(String.Format("{0:0.##}",
                m_totalMeshes));
            args["XEngine Thread Count"] = OSD.FromString(String.Format("{0:0.##}",
                m_inUseThreads));
            args["Util Thread Count"] = OSD.FromString(String.Format("{0:0.##}",
                Util.GetSmartThreadPoolInfo().InUseThreads));
            args["System Thread Count"] = OSD.FromString(String.Format(
                "{0:0.##}", numberThreadsRunning));
            args["ProcMem"] = OSD.FromString(String.Format("{0:#,###,###.##}",
                memUsage));

            args["UDPIn"] = OSD.FromString(String.Format("{0:0.##}",
                m_inByteRate));
            args["UDPOut"] = OSD.FromString(String.Format("{0:0.##}",
                m_outByteRate));
            args["UDPInError"] = OSD.FromString(String.Format("{0:0.##}",
                m_errorPacketRate));
            args["ClientPing"] = OSD.FromString(String.Format("{0:0.##}", m_clientPing));
            args["AvgPing"] = OSD.FromString(String.Format("{0:0.######}", m_avgPing));
            
            return args;
        }

        /// <summary>
        /// Report back collected statistical information, of all connected agents, as a json serialization.
        /// </summary>
        /// <param name="uptime">Time that server has been running</param>
        /// <param name="version">Current version of OpenSim</param>
        /// <returns>JSON string of agent login data</returns>
        public string AgentReport(string uptime, string version)
        {
            // Create new OSDMap to hold the agent data
            OSDMap args = new OSDMap(agentList.Count);

            // Go through the list of connected agents
            foreach (AgentSimData agent in agentList)
            {
                // Add the agent statistical data (name, IP, and login time) to the OSDMap
                args[agent.Name] = OSD.FromString(
                    String.Format("{0} | Login: {1}", agent.IPAddress, agent.Timestamp));
            }

            // Add the given uptime and OpenSim version to the OSDMap
            args["Uptime"] = OSD.FromString(uptime);
            args["Version"] = OSD.FromString(version);

            // Serialize the OSDMap, that was just created, to JSON format and
            // return it
            return OSDParser.SerializeJsonString(args);
        }

        private void StartPingRequests()
        {
            // Create new object to allow for pinging an external server; add the PingCompletedCallback as
            // one of the methods to be called when the PingCompleted delegate is invoked (the
            // PingCompleted literally tracks which methods to call when it is called)
            m_externalPingSender = new Ping();
            m_externalPingSender.PingCompleted += PingCompletedCallback;

            // Create timer to continually ping connected clients, within the specified frequency; add the
            // PingExternal method as one of the methods to be called when the Timer's Elapsed delegate is
            // invoked (Elapsed tracks the methods to call when it is called)
            m_externalPingTimer = new Timer(m_externalPingFreq * 1000);
            m_externalPingTimer.AutoReset = true;
            m_externalPingTimer.Elapsed += PingExternal;

            // Start the timer to ping the external server
            m_pingCompleted = true;
            m_externalPingTimer.Start();
        }

        private void PingCompletedCallback(object sender, PingCompletedEventArgs e)
        {
            // Get the ping time if request succeeded, otherwise save a
            // value of -1 to indicate failure
            if (e.Reply.Status == IPStatus.Success)
                m_avgPing = e.Reply.RoundtripTime;
            else
                m_avgPing = -1;

            // Indicate that ping to external server has completed
            m_pingCompleted = true;
        }

        private void PingExternal(object sender, ElapsedEventArgs e)
        {
            // Make sure that there is no pending ping
            if (m_pingCompleted)
            {
                // Asynchronously send a ping to the designated external server's address
                m_externalPingSender.SendAsync(m_externalServerName, null);

                // Indicate that a ping was just sent
                m_pingCompleted = false;
            }
        }
    }


    /// <summary>
    /// Pull packet queue stats from packet queues and report
    /// </summary>
    public class PacketQueueStatsCollector : IStatsCollector
    {
        private IPullStatsProvider m_statsProvider;

        public PacketQueueStatsCollector(IPullStatsProvider provider)
        {
            m_statsProvider = provider;
        }

        /// <summary>
        /// Report back collected statistical information.
        /// </summary>
        /// <returns></returns>
        public string Report()
        {
            return m_statsProvider.GetStats();
        }
        
        public string XReport(string uptime, string version)
        {
            return "";
        }
        
        public OSDMap OReport(string uptime, string version)
        {
            OSDMap ret = new OSDMap();
            return ret;
        }
    }


    public class AgentSimData
    {
        private string m_agentName;
        private string m_agentIPAddress;
        private string m_loginTimestamp;

        public AgentSimData(string name, string ipAddress, string loginTimestamp)
        {
            // Save the given agent data: their name, IP address, and login timestamp
            m_agentName = name;
            m_agentIPAddress = ipAddress;
            m_loginTimestamp = loginTimestamp;
        }

        public string Name
        {
            get { return m_agentName; }
        }

        public string IPAddress
        {
            get { return m_agentIPAddress; }
        }

        public string Timestamp
        {
            get { return m_loginTimestamp; }
        }
    }
}
