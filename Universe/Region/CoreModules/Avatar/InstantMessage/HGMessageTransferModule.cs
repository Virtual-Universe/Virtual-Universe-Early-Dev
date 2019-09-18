/// <license>
///     Copyright (c) Contributors, https://virtual-planets.org/
///     See CONTRIBUTORS.TXT for a full list of copyright holders.
///     For an explanation of the license of each contributor and the content it
///     covers please see the Licenses directory.
///
///     Redistribution and use in source and binary forms, with or without
///     modification, are permitted provided that the following conditions are met:
///         * Redistributions of source code must retain the above copyright
///         notice, this list of conditions and the following disclaimer.
///         * Redistributions in binary form must reproduce the above copyright
///         notice, this list of conditions and the following disclaimer in the
///         documentation and/or other materials provided with the distribution.
///         * Neither the name of the Virtual Universe Project nor the
///         names of its contributors may be used to endorse or promote products
///         derived from this software without specific prior written permission.
///
///     THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
///     EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
///     WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
///     DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
///     DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
///     (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
///     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
///     ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
///     (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
///     SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
/// </license>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using Universe.Framework;
using Universe.Framework.Servers;
using Universe.Region.Framework.Interfaces;
using Universe.Region.Framework.Scenes;
using Universe.Server.Handlers.Hypergrid;
using Universe.Services.Connectors.Hypergrid;
using Universe.Services.Connectors.InstantMessage;
using Universe.Services.Interfaces;
using GridRegion = Universe.Services.Interfaces.GridRegion;
using PresenceInfo = Universe.Services.Interfaces.PresenceInfo;

namespace Universe.Region.CoreModules.Avatar.InstantMessage
{
    [Extension(Path = "/Universe/RegionModules", NodeName = "RegionModule", Id = "HGMessageTransferModule")]
    public class HGMessageTransferModule : ISharedRegionModule, IMessageTransferModule, IInstantMessageSimConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled = false;
        protected List<Scene> m_Scenes = new List<Scene>();

        protected IInstantMessage m_IMService;
        protected Dictionary<UUID, object> m_UserLocationMap = new Dictionary<UUID, object>();

        public event UndeliveredMessage OnUndeliveredMessage;

        IUserManagement m_uMan;
        IUserManagement UserManagementModule
        {
            get
            {
                if (m_uMan == null)
                {
                    m_uMan = m_Scenes[0].RequestModuleInterface<IUserManagement>();
                }

                return m_uMan;
            }
        }

        public virtual void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];

            if (cnf != null && cnf.GetString("MessageTransferModule", "MessageTransferModule") != Name)
            {
                m_log.Debug("[Hyper Grid Message Transfer]: Disabled by configuration");
                return;
            }

            InstantMessageServerConnector imServer = new InstantMessageServerConnector(config, MainServer.Instance, this);
            m_IMService = imServer.GetService();
            m_Enabled = true;
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_Enabled)
            {
                return;
            }

            lock (m_Scenes)
            {
                m_log.DebugFormat("[Hyper Grid Message Transfer]: Message transfer module {0} active", Name);
                scene.RegisterModuleInterface<IMessageTransferModule>(this);
                m_Scenes.Add(scene);
            }
        }

        public virtual void PostInitialise()
        {
            if (!m_Enabled)
            {
                return;
            }
        }

        public virtual void RegionLoaded(Scene scene)
        {
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
            {
                return;
            }

            lock (m_Scenes)
            {
                m_Scenes.Remove(scene);
            }
        }

        public virtual void Close()
        {
        }

        public virtual string Name
        {
            get { return "HGMessageTransferModule"; }
        }

        public virtual Type ReplaceableInterface
        {
            get { return null; }
        }

        public void SendInstantMessage(GridInstantMessage im, MessageResultNotification result)
        {
            UUID toAgentID = new UUID(im.toAgentID);

            // Try root avatar only first
            foreach (Scene scene in m_Scenes)
            {
                ScenePresence sp = scene.GetScenePresence(toAgentID);

                if (sp != null && !sp.IsChildAgent && !sp.IsDeleted)
                {
                    // Local message
                    sp.ControllingClient.SendInstantMessage(im);

                    // Message sent
                    result(true);
                    return;
                }
            }

            // Is the user a local user?
            string url = string.Empty;
            bool foreigner = false;

            if (UserManagementModule != null && !UserManagementModule.IsLocalGridUser(toAgentID)) // foreign user
            {
                url = UserManagementModule.GetUserServerURL(toAgentID, "IMServerURI");
                foreigner = true;
            }

            Util.FireAndForget(delegate
            {
                bool success = false;

                if (foreigner && url == string.Empty) // we don't know about this user
                {
                    string recipientUUI = TryGetRecipientUUI(new UUID(im.fromAgentID), toAgentID);
                    m_log.DebugFormat("[Hyper Grid Message Transfer]: Got UUI {0}", recipientUUI);

                    if (recipientUUI != string.Empty)
                    {
                        UUID id; string u = string.Empty, first = string.Empty, last = string.Empty, secret = string.Empty;

                        if (Util.ParseUniversalUserIdentifier(recipientUUI, out id, out u, out first, out last, out secret))
                        {
                            success = m_IMService.OutgoingInstantMessage(im, u, true);

                            if (success)
                            {
                                UserManagementModule.AddUser(toAgentID, u + ";" + first + " " + last);
                            }
                        }
                    }
                }
                else
                {
                    success = m_IMService.OutgoingInstantMessage(im, url, foreigner);
                }

                if (!success && !foreigner)
                {
                    HandleUndeliverableMessage(im, result);
                }
                else
                {
                    result(success);
                }
            }, null, "HGMessageTransferModule.SendInstantMessage");

            return;
        }

        protected bool SendIMToScene(GridInstantMessage gim, UUID toAgentID)
        {
            bool successful = false;

            foreach (Scene scene in m_Scenes)
            {
                ScenePresence sp = scene.GetScenePresence(toAgentID);

                if (sp != null && !sp.IsChildAgent && !sp.IsDeleted)
                {
                    scene.EventManager.TriggerIncomingInstantMessage(gim);
                    successful = true;
                }
            }

            if (!successful)
            {
                // If the message can't be delivered to an agent, it
                // is likely to be a group IM. On a group IM, the
                // imSessionID = toAgentID = group id. Raise the
                // unhandled IM event to give the groups module
                // a chance to pick it up. We raise that in a random
                // scene, since the groups module is shared.
                m_Scenes[0].EventManager.TriggerUnhandledInstantMessage(gim);
            }

            return successful;
        }

        public void HandleUndeliverableMessage(GridInstantMessage im, MessageResultNotification result)
        {
            UndeliveredMessage handlerUndeliveredMessage = OnUndeliveredMessage;

            // If this event has handlers, then an IM from an agent will be
            // considered delivered. This will suppress the error message.
            if (handlerUndeliveredMessage != null)
            {
                handlerUndeliveredMessage(im);

                if (im.dialog == (byte)InstantMessageDialog.MessageFromAgent)
                {
                    result(true);
                }
                else
                {
                    result(false);
                }

                return;
            }

            result(false);
        }

        private string TryGetRecipientUUI(UUID fromAgent, UUID toAgent)
        {
            // Let's call back the fromAgent's user agent service
            // Maybe that service knows about the toAgent
            IClientAPI client = LocateClientObject(fromAgent);

            if (client != null)
            {
                AgentCircuitData circuit = m_Scenes[0].AuthenticateHandler.GetAgentCircuitData(client.AgentId);

                if (circuit != null)
                {
                    if (circuit.ServiceURLs.ContainsKey("HomeURI"))
                    {
                        string uasURL = circuit.ServiceURLs["HomeURI"].ToString();
                        m_log.DebugFormat("[Hyper Grid Message Transfer]: getting UUI of user {0} from {1}", toAgent, uasURL);
                        UserAgentServiceConnector uasConn = new UserAgentServiceConnector(uasURL);

                        string agentUUI = string.Empty;

                        try
                        {
                            agentUUI = uasConn.GetUUI(fromAgent, toAgent);
                        }
                        catch (Exception e)
                        {
                            m_log.Debug("[Hyper Grid Message Transfer]: GetUUI call failed ", e);
                        }

                        return agentUUI;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Find the root client for a ID
        /// </summary>
        public IClientAPI LocateClientObject(UUID agentID)
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes)
                {
                    ScenePresence presence = scene.GetScenePresence(agentID);

                    if (presence != null && !presence.IsChildAgent && !presence.IsDeleted)
                    {
                        return presence.ControllingClient;
                    }
                }
            }

            return null;
        }

        #region IInstantMessageSimConnector

        public bool SendInstantMessage(GridInstantMessage im)
        {
            UUID agentID = new UUID(im.toAgentID);
            return SendIMToScene(im, agentID);
        }

        #endregion
    }
}
