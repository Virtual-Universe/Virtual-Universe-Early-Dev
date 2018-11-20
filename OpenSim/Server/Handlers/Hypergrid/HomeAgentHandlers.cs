

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Server.Handlers.Simulation;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Utils = OpenSim.Server.Handlers.Simulation.Utils;

namespace OpenSim.Server.Handlers.Hypergrid
{
    public class HomeAgentHandler : AgentPostHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private IUserAgentService m_UserAgentService;

        private string m_LoginServerIP;

        public HomeAgentHandler(IUserAgentService userAgentService, string loginServerIP, bool proxy) : base("/homeagent")
        {
            m_UserAgentService = userAgentService;
            m_LoginServerIP = loginServerIP;
            m_Proxy = proxy;
        }

        protected override AgentDestinationData CreateAgentDestinationData()
        {
            return new ExtendedAgentDestinationData();
        }

        protected override void UnpackData(OSDMap args, AgentDestinationData d, Hashtable request)
        {
            base.UnpackData(args, d, request);
            ExtendedAgentDestinationData data = (ExtendedAgentDestinationData)d;

            try
            {
                if (args.ContainsKey("gatekeeper_host") && args["gatekeeper_host"] != null)
                {
                    data.host = args["gatekeeper_host"].AsString();
                }

                if (args.ContainsKey("gatekeeper_port") && args["gatekeeper_port"] != null)
                {
                    Int32.TryParse(args["gatekeeper_port"].AsString(), out data.port);
                }

                if (args.ContainsKey("gatekeeper_serveruri") && args["gatekeeper_serveruri"] != null)
                {
                    data.gatekeeperServerURI = args["gatekeeper_serveruri"];
                }

                if (args.ContainsKey("destination_serveruri") && args["destination_serveruri"] != null)
                {
                    data.destinationServerURI = args["destination_serveruri"];
                }
            }
            catch (InvalidCastException)
            {
                m_log.ErrorFormat("[Home Agent Handler]: Bad cast in UnpackData");
            }

            string callerIP = GetCallerIP(request);

            // Verify if this call came from the login server
            if (callerIP == m_LoginServerIP)
            {
                data.fromLogin = true;
            }
        }

        protected override GridRegion ExtractGatekeeper(AgentDestinationData d)
        {
            if (d is ExtendedAgentDestinationData)
            {
                ExtendedAgentDestinationData data = (ExtendedAgentDestinationData)d;
                GridRegion gatekeeper = new GridRegion();
                gatekeeper.ServerURI = data.gatekeeperServerURI;
                gatekeeper.ExternalHostName = data.host;
                gatekeeper.HttpPort = (uint)data.port;
                gatekeeper.InternalEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0);

                return gatekeeper;
            }
            else
            {
                m_log.WarnFormat("[Home Agent Handler]: Wrong data type");
            }

            return null;
        }

        protected override bool CreateAgent(GridRegion source, GridRegion gatekeeper, GridRegion destination,
            AgentCircuitData aCircuit, uint teleportFlags, bool fromLogin, EntityTransferContext ctx, out string reason)
        {
            return m_UserAgentService.LoginAgentToGrid(source, aCircuit, gatekeeper, destination, fromLogin, out reason);
        }
    }

    public class ExtendedAgentDestinationData : AgentDestinationData
    {
        public string host;
        public int port;
        public string gatekeeperServerURI;
        public string destinationServerURI;
    }
}
