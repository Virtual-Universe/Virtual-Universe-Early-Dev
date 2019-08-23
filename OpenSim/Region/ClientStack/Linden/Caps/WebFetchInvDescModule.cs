/* 3 May 2019
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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Capabilities;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;
using OpenSim.Capabilities.Handlers;
using OpenSim.Framework.Monitoring;

namespace OpenSim.Region.ClientStack.Linden
{
    /// <summary>
    /// This module implements both WebFetchInventoryDescendents and FetchInventoryDescendents2 capabilities.
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "WebFetchInvDescModule")]
    public class WebFetchInvDescModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Control whether requests will be processed asynchronously.
        /// </summary>
        /// <remarks>
        /// Defaults to true.  Can currently not be changed once a region has been added to the module.
        /// </remarks>
        public bool ProcessQueuedRequestsAsync { get; private set; }

        /// <summary>
        /// Number of inventory requests processed by this module.
        /// </summary>
        /// <remarks>
        /// It's the PollServiceRequestManager that actually sends completed requests back to the requester.
        /// </remarks>
        public static int ProcessedRequestsCount { get; set; }

        private static Stat s_processedRequestsStat;

        private static Scene m_scene = null;
        public Scene Scene { get { return m_scene; }
                             private set { m_scene = value; }  }

        private static IInventoryService m_InventoryService;
        private static ILibraryService m_LibraryService;

        private bool m_Enabled;

        private string m_fetchInventoryDescendents2Url;

        private static NActionChain actionChain =
                   new NActionChain(8, true, 
                       ThreadPriority.Normal); // Leave at Normal. 
                                               // We give the textures and mesh AboveNormal.

        #region ISharedRegionModule Members

        public WebFetchInvDescModule() : this(true) {}

        public WebFetchInvDescModule(bool processQueuedResultsAsync)
        {
            ProcessQueuedRequestsAsync = true;
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            m_fetchInventoryDescendents2Url = config.GetString("Cap_FetchInventoryDescendents2", string.Empty);
            if (m_fetchInventoryDescendents2Url != string.Empty)
            {
                m_Enabled = true;
            }
        }

        public void AddRegion(Scene s)
        {
            if (!m_Enabled)
                return;

            Scene = s;
        }

        public void RemoveRegion(Scene s)
        {
            if (!m_Enabled)
                return;

            Scene.EventManager.OnRegisterCaps -= RegisterCaps;

            StatsManager.DeregisterStat(s_processedRequestsStat);
        }

        public void RegionLoaded(Scene s)
        {
            if (!m_Enabled)
                return;

            if (s_processedRequestsStat == null)
                s_processedRequestsStat =
                    new Stat(
                        "ProcessedFetchInventoryRequests",
                        "Number of processed fetch inventory requests",
                        "These have not necessarily yet been dispatched back to the requester.",
                        "",
                        "inventory",
                        "httpfetch",
                        StatType.Pull,
                        MeasuresOfInterest.AverageChangeOverTime,
                        stat => { stat.Value = ProcessedRequestsCount; },
                        StatVerbosity.Debug);

            StatsManager.RegisterStat(s_processedRequestsStat);

            m_InventoryService = Scene.InventoryService;
            m_LibraryService = Scene.LibraryService;

            Scene.EventManager.OnRegisterCaps += RegisterCaps;
		}

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name { get { return "WebFetchInvDescModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        private class PollServiceInventoryEventArgs : PollServiceEventArgs
        {
            private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

            private Dictionary<UUID, Hashtable> responses =
                    new Dictionary<UUID, Hashtable>();

            private WebFetchInvDescModule m_module;

            private FetchInvDescHandler m_getHandler;

            public PollServiceInventoryEventArgs(WebFetchInvDescModule module, string url, UUID pId) :
                base(null, url, null, null, null, null, pId, int.MaxValue)
            {
                m_module = module;
                m_getHandler = new FetchInvDescHandler(m_InventoryService, m_LibraryService, m_scene);

                HasEvents = (x, y) =>
                {
                    lock (responses)
                          return responses.ContainsKey(x);
                };

                GetEvents = (x, y) =>
                {
                    lock (responses)
                    {
                        try
                        {
                            return responses[x];
                        }
                        finally
                        {
                            responses.Remove(x);
                        }
                    }
                };

                Request = (x, y) =>
                {
                    if (x == UUID.Zero || y == null)
                        return;

                    actionChain.Enqueue(delegate { Process(x, y); });
                };

                NoEvents = (x, y) =>
                {
                    Hashtable response = new Hashtable();
                    response["int_response_code"] = 500;
                    response["str_response_string"] = "Script timeout";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;
                    response["reusecontext"] = false;
                    return response;
                };
            }

            public void Process(UUID ReqID,
                                Hashtable Request)
            {
                if (m_module == null || m_module.Scene == null || m_module.Scene.ShuttingDown)
                    return;

                // Decode the request here
                // test the syntax.
                try
                {
                    string request = Request["body"].ToString();
                    request = request.Replace("<string>00000000-0000-0000-0000-000000000000</string>", "<uuid>00000000-0000-0000-0000-000000000000</uuid>");
                    request = request.Replace("<key>fetch_folders</key><integer>0</integer>", "<key>fetch_folders</key><boolean>0</boolean>");
                    request = request.Replace("<key>fetch_folders</key><integer>1</integer>", "<key>fetch_folders</key><boolean>1</boolean>");

                    Hashtable hash = (Hashtable)LLSD.LLSDDeserialize(Utils.StringToBytes(request));
                }
                catch (LLSD.LLSDParseException e)
                {
                    m_log.ErrorFormat("[INVENTORY]: Fetch error: {0}{1}" + e.Message, e.StackTrace);
                    return;
                }
                catch
                {
                    m_log.ErrorFormat("[INVENTORY]: XML Format error");
                    return;
                }

                Hashtable response = new Hashtable();
                response["int_response_code"] = 200;
                response["content_type"] = "text/plain";
                response["keepalive"] = false;
                response["reusecontext"] = false;
                response["str_response_string"] = m_getHandler.FetchInventoryDescendentsRequest(
                         Request["body"].ToString(),
                         String.Empty, String.Empty, null, null);

                lock (responses)
                      responses[ReqID] = response;

                Request.Clear();
                Request = null;

                WebFetchInvDescModule.ProcessedRequestsCount++;
            }
        }

        private void RegisterCaps(UUID agentID, Caps caps)
        {
            RegisterFetchDescendentsCap(agentID, caps, "FetchInventoryDescendents2", m_fetchInventoryDescendents2Url);
        }

        private void RegisterFetchDescendentsCap(UUID agentID, Caps caps, string capName, string url)
        {
            string capUrl;

            // disable the cap clause
            if (url == "")
                return;

            // handled by the simulator
            if (url == "localhost")
            {
                capUrl = "/CAPS/" + UUID.Random() + "/";

                // Register this as a poll service
                PollServiceInventoryEventArgs args = new PollServiceInventoryEventArgs(this, capUrl, agentID);
 
                caps.RegisterPollHandler(capName, args);
            }
            // external handler
            else
            {
                capUrl = url;
                IExternalCapsModule handler = Scene.RequestModuleInterface<IExternalCapsModule>();
                if (handler != null)
                    handler.RegisterExternalUserCapsHandler(agentID,caps,capName,capUrl);
                else
                    caps.RegisterHandler(capName, capUrl);
            }
        }
    }
}
