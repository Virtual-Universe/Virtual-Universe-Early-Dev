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
using Mono.Addins;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Capabilities.Handlers;
using OpenSim.Framework;
using OpenSim.Framework.Monitoring;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GetMeshModule")]
    public class GetMeshModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;
        private bool m_Enabled;

        private string m_GetMeshURL;
        private string m_GetMesh2URL;

        private Dictionary<UUID, string> m_capsDict = new Dictionary<UUID, string>();
        private Dictionary<UUID, string> m_capsDict2 = new Dictionary<UUID, string>();

        private Dictionary<UUID, PollServiceMeshEventArgs> m_pollservices = new Dictionary<UUID, PollServiceMeshEventArgs>();
        private Dictionary<UUID, PollServiceMeshEventArgs> m_pollservices2 = new Dictionary<UUID, PollServiceMeshEventArgs>();

        private static NActionChain actionChain =
                   new NActionChain(8, true, ThreadPriority.AboveNormal);

        #region Region Module interfaceBase Members

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            m_GetMeshURL = config.GetString("Cap_GetMesh", string.Empty);
            if (m_GetMeshURL != string.Empty)
                m_Enabled = true;

            m_GetMesh2URL = config.GetString("Cap_GetMesh2", string.Empty);
            if (m_GetMesh2URL != string.Empty)
                m_Enabled = true;
        }

        public void AddRegion(Scene pScene)
        {
            if (!m_Enabled)
                return;

            m_scene = pScene;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
            m_scene.EventManager.OnDeregisterCaps -= DeregisterCaps;

            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
            m_scene.EventManager.OnDeregisterCaps += DeregisterCaps;
        }

        public void Close()
        {
        }

        public string Name { get { return "GetMeshModule"; } }

        #endregion

        private class PollServiceMeshEventArgs : PollServiceEventArgs
        {
            private Dictionary<UUID, Hashtable> responses = new Dictionary<UUID, Hashtable>();

            private Scene m_scene;
            private GetMeshHandler m_getHandler;

            public PollServiceMeshEventArgs(string uri, UUID pId, Scene scene) :
                base(null, uri, null, null, null, null, pId, int.MaxValue)
            {
                m_scene = scene;
                m_getHandler = new GetMeshHandler(m_scene.AssetService);

                // x is request id
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

                // x is request id, y is request data hashtable
                Request = (x, y) =>
                {
                    if (x != UUID.Zero)
                    {
                        actionChain.Enqueue(delegate { Process(x, y); });
                    }
                };

                // this should never happen except possible on shutdown
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

            public void Process(UUID reqID, Hashtable request)
            {
                if (m_scene.ShuttingDown)
                    return;

                Hashtable response = null;
                // If the avatar is gone, don't bother to get the texture
                if (m_scene.GetScenePresence(Id) == null)
                {
                    response = new Hashtable();
                    response["int_response_code"] = 500;
                    response["str_response_string"] = "Script timeout";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;
                    response["reusecontext"] = false;

                    lock (responses)
                            responses[reqID] = response;

                    request.Clear();
                    request = null;
                    return;
                }

                response = m_getHandler.ProcessGetMesh(request);
                lock (responses)
                        responses[reqID] = response;

                request.Clear();
                request = null;
            }
        }

        public void RegisterCaps(UUID agentID, Caps caps)
        {
            if (m_GetMeshURL == "localhost")
            {
                string capUrl = "/CAPS/" + UUID.Random() + "/";

                // Register this as a poll service
                PollServiceMeshEventArgs args = new PollServiceMeshEventArgs(capUrl, agentID, m_scene);

                MainServer.Instance.AddPollServiceHTTPHandler(capUrl, args);

                string hostName = m_scene.RegionInfo.ExternalHostName;
                uint port = (MainServer.Instance == null) ? 0 : MainServer.Instance.Port;
                string protocol = "http";

                if (MainServer.Instance.UseSSL)
                {
                    hostName = MainServer.Instance.SSLCommonName;
                    port = MainServer.Instance.SSLPort;
                    protocol = "https";
                }
                caps.RegisterHandler("GetMesh", String.Format("{0}://{1}:{2}{3}", protocol, hostName, port, capUrl));

                lock (m_pollservices)
                      m_pollservices[agentID] = args;

                lock (m_capsDict)
                      m_capsDict[agentID] = capUrl;
            }
            else
            {
                caps.RegisterHandler("GetMesh", m_GetMeshURL);
            }

            if (m_GetMesh2URL == "localhost")
            {
                string capUrl = "/CAPS/" + UUID.Random() + "/";

                // Register this as a poll service
                PollServiceMeshEventArgs args = new PollServiceMeshEventArgs(capUrl, agentID, m_scene);

                MainServer.Instance.AddPollServiceHTTPHandler(capUrl, args);

                string hostName = m_scene.RegionInfo.ExternalHostName;
                uint port = (MainServer.Instance == null) ? 0 : MainServer.Instance.Port;
                string protocol = "http";

                if (MainServer.Instance.UseSSL)
                {
                    hostName = MainServer.Instance.SSLCommonName;
                    port = MainServer.Instance.SSLPort;
                    protocol = "https";
                }
                caps.RegisterHandler("GetMesh2", String.Format("{0}://{1}:{2}{3}", protocol, hostName, port, capUrl));

                lock (m_pollservices2)
                      m_pollservices2[agentID] = args;

                lock (m_capsDict2)
                      m_capsDict2[agentID] = capUrl;
            }
            else
            {
                caps.RegisterHandler("GetMesh2", m_GetMesh2URL);
            }
        }

        private void DeregisterCaps(UUID agentID, Caps caps)
		{
			string capURL;
            lock (m_capsDict)
            {
                if (m_capsDict.TryGetValue(agentID, out capURL))
                {
                    MainServer.Instance.RemoveHTTPHandler("", capURL);
                    MainServer.Instance.RemovePollServiceHTTPHandler("",capURL);
                    m_capsDict.Remove(agentID);
                }
            }

            lock (m_pollservices)
                  m_pollservices.Remove(agentID);

            lock (m_capsDict2)
            {
                if (m_capsDict2.TryGetValue(agentID, out capURL))
                {
                    MainServer.Instance.RemoveHTTPHandler("", capURL);
                    MainServer.Instance.RemovePollServiceHTTPHandler("", capURL);
                    m_capsDict2.Remove(agentID);
                }
            }

            lock (m_pollservices2)
                  m_pollservices2.Remove(agentID);
        }
    }
}
