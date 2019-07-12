﻿/// <license>
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
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Server.Handlers.Simulation
{
    public class AgentHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private ISimulationService m_SimulationService;

        public AgentHandler() { }

        public AgentHandler(ISimulationService sim)
        {
            m_SimulationService = sim;
        }

        public Hashtable Handler(Hashtable request)
        {
            Hashtable responsedata = new Hashtable();
            responsedata["content_type"] = "text/html";
            responsedata["keepalive"] = false;

            UUID agentID;
            UUID regionID;
            string action;

            if (!Utils.GetParams((string)request["uri"], out agentID, out regionID, out action))
            {
                m_log.InfoFormat("[Agent Handler]: Invalid parameters for agent message {0}", request["uri"]);
                responsedata["int_response_code"] = 404;
                responsedata["str_response_string"] = "false";

                return responsedata;
            }

            // Next, let's parse the verb
            string method = (string)request["http-method"];

            if (method.Equals("DELETE"))
            {
                string auth_token = string.Empty;

                if (request.ContainsKey("auth"))
                {
                    auth_token = request["auth"].ToString();
                }

                DoAgentDelete(request, responsedata, agentID, action, regionID, auth_token);
                return responsedata;
            }
            else if (method.Equals("QUERYACCESS"))
            {
                DoQueryAccess(request, responsedata, agentID, regionID);
                return responsedata;
            }
            else
            {
                m_log.ErrorFormat("[Agent Handler]: method {0} not supported in agent message {1} (caller is {2})", method, (string)request["uri"], Util.GetCallerIP(request));
                responsedata["int_response_code"] = HttpStatusCode.MethodNotAllowed;
                responsedata["str_response_string"] = "Method not allowed";

                return responsedata;
            }
        }

        protected virtual void DoQueryAccess(Hashtable request, Hashtable responsedata, UUID agentID, UUID regionID)
        {
            if (m_SimulationService == null)
            {
                m_log.Debug("[Agent Handler]: Agent QUERY called. Harmless but useless.");
                responsedata["content_type"] = "application/json";
                responsedata["int_response_code"] = HttpStatusCode.NotImplemented;
                responsedata["str_response_string"] = string.Empty;

                return;
            }

            OSDMap args = Utils.GetOSDMap((string)request["body"]);

            bool viaTeleport = true;

            if (args.ContainsKey("viaTeleport"))
            {
                viaTeleport = args["viaTeleport"].AsBoolean();
            }

            Vector3 position = Vector3.Zero;

            if (args.ContainsKey("position"))
            {
                position = Vector3.Parse(args["position"].AsString());
            }

            string agentHomeURI = null;

            if (args.ContainsKey("agent_home_uri"))
            {
                agentHomeURI = args["agent_home_uri"].AsString();
            }

            string theirVersion = string.Empty;

            if (args.ContainsKey("my_version"))
            {
                theirVersion = args["my_version"].AsString();
            }

            GridRegion destination = new GridRegion();
            destination.RegionID = regionID;

            string reason;
            string version;
            bool result = m_SimulationService.QueryAccess(destination, agentID, agentHomeURI, viaTeleport, position, theirVersion, out version, out reason);

            responsedata["int_response_code"] = HttpStatusCode.OK;

            OSDMap resp = new OSDMap(3);

            resp["success"] = OSD.FromBoolean(result);
            resp["reason"] = OSD.FromString(reason);
            resp["version"] = OSD.FromString(version);

            // We must preserve defaults here, otherwise a false "success" will not be put into the JSON map!
            responsedata["str_response_string"] = OSDParser.SerializeJsonString(resp, true);
        }

        protected void DoAgentDelete(Hashtable request, Hashtable responsedata, UUID id, string action, UUID regionID, string auth_token)
        {
            if (string.IsNullOrEmpty(action))
            {
                m_log.DebugFormat("[Agent Handler]: >>> DELETE <<< RegionID: {0}; from: {1}; auth_code: {2}", regionID, Util.GetCallerIP(request), auth_token);
            }
            else
            {
                m_log.DebugFormat("[Agent Handler]: Release {0} to RegionID: {1}", id, regionID);
            }

            GridRegion destination = new GridRegion();
            destination.RegionID = regionID;

            if (action.Equals("release"))
            {
                ReleaseAgent(regionID, id);
            }
            else
            {
                Util.FireAndForget(o => m_SimulationService.CloseAgent(destination, id, auth_token), null, "AgentHandler.DoAgentDelete");
            }

            responsedata["int_response_code"] = HttpStatusCode.OK;
            responsedata["str_response_string"] = "OpenSim agent " + id.ToString();
        }

        protected virtual void ReleaseAgent(UUID regionID, UUID id)
        {
            m_SimulationService.ReleaseAgent(regionID, id, "");
        }
    }

    public class AgentPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private ISimulationService m_SimulationService;
        protected bool m_Proxy = false;

        public AgentPostHandler(ISimulationService service) : base("POST", "/agent")
        {
            m_SimulationService = service;
        }

        public AgentPostHandler(string path) : base("POST", path)
        {
            m_SimulationService = null;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Hashtable keysvals = new Hashtable();
            Hashtable headervals = new Hashtable();

            string[] querystringkeys = httpRequest.QueryString.AllKeys;
            string[] rHeaders = httpRequest.Headers.AllKeys;

            keysvals.Add("uri", httpRequest.RawUrl);
            keysvals.Add("content-type", httpRequest.ContentType);
            keysvals.Add("http-method", httpRequest.HttpMethod);

            foreach (string queryname in querystringkeys)
            {
                keysvals.Add(queryname, httpRequest.QueryString[queryname]);
            }

            foreach (string headername in rHeaders)
            {
                headervals[headername] = httpRequest.Headers[headername];
            }

            keysvals.Add("headers", headervals);
            keysvals.Add("querystringkeys", querystringkeys);

            httpResponse.StatusCode = 200;
            httpResponse.ContentType = "text/html";
            httpResponse.KeepAlive = false;
            Encoding encoding = Encoding.UTF8;

            if (httpRequest.ContentType != "application/json")
            {
                httpResponse.StatusCode = 406;
                return encoding.GetBytes("false");
            }

            string requestBody;

            Stream inputStream = request;
            Stream innerStream = null;

            try
            {
                if ((httpRequest.ContentType == "application/x-gzip" || httpRequest.Headers["Content-Encoding"] == "gzip") || (httpRequest.Headers["X-Content-Encoding"] == "gzip"))
                {
                    innerStream = inputStream;
                    inputStream = new GZipStream(innerStream, CompressionMode.Decompress);
                }

                using (StreamReader reader = new StreamReader(inputStream, encoding))
                {
                    requestBody = reader.ReadToEnd();
                }
            }
            finally
            {
                if (innerStream != null)
                {
                    innerStream.Dispose();
                }

                inputStream.Dispose();
            }

            keysvals.Add("body", requestBody);

            Hashtable responsedata = new Hashtable();

            UUID agentID;
            UUID regionID;
            string action;

            if (!Utils.GetParams((string)keysvals["uri"], out agentID, out regionID, out action))
            {
                m_log.InfoFormat("[Agent Handler]: Invalid parameters for agent message {0}", keysvals["uri"]);

                httpResponse.StatusCode = 404;

                return encoding.GetBytes("false");
            }

            DoAgentPost(keysvals, responsedata, agentID);

            httpResponse.StatusCode = (int)responsedata["int_response_code"];
            return encoding.GetBytes((string)responsedata["str_response_string"]);
        }

        protected void DoAgentPost(Hashtable request, Hashtable responsedata, UUID id)
        {
            OSDMap args = Utils.GetOSDMap((string)request["body"]);

            if (args == null)
            {
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            AgentDestinationData data = CreateAgentDestinationData();
            UnpackData(args, data, request);

            GridRegion destination = new GridRegion();
            destination.RegionID = data.uuid;
            destination.RegionLocX = data.x;
            destination.RegionLocY = data.y;
            destination.RegionName = data.name;

            GridRegion gatekeeper = ExtractGatekeeper(data);

            AgentCircuitData aCircuit = new AgentCircuitData();

            try
            {
                aCircuit.UnpackAgentCircuitData(args);
            }
            catch (Exception ex)
            {
                m_log.InfoFormat("[Agent Handler]: exception on unpacking ChildCreate message {0}", ex.Message);
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            GridRegion source = null;

            if (args.ContainsKey("source_uuid"))
            {
                source = new GridRegion();
                source.RegionLocX = Int32.Parse(args["source_x"].AsString());
                source.RegionLocY = Int32.Parse(args["source_y"].AsString());
                source.RegionName = args["source_name"].AsString();
                source.RegionID = UUID.Parse(args["source_uuid"].AsString());

                if (args.ContainsKey("source_server_uri"))
                {
                    source.RawServerURI = args["source_server_uri"].AsString();
                }
                else
                {
                    source.RawServerURI = null;
                }
            }

            OSDMap resp = new OSDMap(2);
            string reason = String.Empty;

            // This is the meaning of POST agent
            bool result = CreateAgent(source, gatekeeper, destination, aCircuit, data.flags, data.fromLogin, out reason);

            resp["reason"] = OSD.FromString(reason);
            resp["success"] = OSD.FromBoolean(result);

            // Let's also send out the IP address of the caller back to the caller (HG 1.5)
            resp["your_ip"] = OSD.FromString(GetCallerIP(request));

            // TODO: add reason if not String.Empty?
            responsedata["int_response_code"] = HttpStatusCode.OK;
            responsedata["str_response_string"] = OSDParser.SerializeJsonString(resp);
        }

        protected virtual AgentDestinationData CreateAgentDestinationData()
        {
            return new AgentDestinationData();
        }

        protected virtual void UnpackData(OSDMap args, AgentDestinationData data, Hashtable request)
        {
            // retrieve the input arguments
            if (args.ContainsKey("destination_x") && args["destination_x"] != null)
            {
                Int32.TryParse(args["destination_x"].AsString(), out data.x);
            }
            else
            {
                m_log.WarnFormat("  -- request didn't have destination_x");
            }

            if (args.ContainsKey("destination_y") && args["destination_y"] != null)
            {
                Int32.TryParse(args["destination_y"].AsString(), out data.y);
            }
            else
            {
                m_log.WarnFormat("  -- request didn't have destination_y");
            }

            if (args.ContainsKey("destination_uuid") && args["destination_uuid"] != null)
            {
                UUID.TryParse(args["destination_uuid"].AsString(), out data.uuid);
            }

            if (args.ContainsKey("destination_name") && args["destination_name"] != null)
            {
                data.name = args["destination_name"].ToString();
            }

            if (args.ContainsKey("teleport_flags") && args["teleport_flags"] != null)
            {
                data.flags = args["teleport_flags"].AsUInteger();
            }
        }

        protected virtual GridRegion ExtractGatekeeper(AgentDestinationData data)
        {
            return null;
        }

        protected string GetCallerIP(Hashtable request)
        {
            if (!m_Proxy)
            {
                return Util.GetCallerIP(request);
            }

            // We're behind a proxy
            Hashtable headers = (Hashtable)request["headers"];

            string xff = "X-Forwarded-For";

            if (headers.ContainsKey(xff.ToLower()))
            {
                xff = xff.ToLower();
            }

            if (!headers.ContainsKey(xff) || headers[xff] == null)
            {
                m_log.WarnFormat("[Agent Handler]: No XFF header");
                return Util.GetCallerIP(request);
            }

            m_log.DebugFormat("[Agent Handler]: XFF is {0}", headers[xff]);

            IPEndPoint ep = Util.GetClientIPFromXFF((string)headers[xff]);

            if (ep != null)
            {
                return ep.Address.ToString();
            }

            // Oops
            return Util.GetCallerIP(request);
        }

        // subclasses can override this
        protected virtual bool CreateAgent(GridRegion source, GridRegion gatekeeper, GridRegion destination,
            AgentCircuitData aCircuit, uint teleportFlags, bool fromLogin, out string reason)
        {
            return m_SimulationService.CreateAgent(source, destination, aCircuit, teleportFlags, out reason);
        }
    }

    public class AgentPutHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private ISimulationService m_SimulationService;
        protected bool m_Proxy = false;

        public AgentPutHandler(ISimulationService service) : base("PUT", "/agent")
        {
            m_SimulationService = service;
        }

        public AgentPutHandler(string path) : base("PUT", path)
        {
            m_SimulationService = null;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Hashtable keysvals = new Hashtable();
            Hashtable headervals = new Hashtable();

            string[] querystringkeys = httpRequest.QueryString.AllKeys;
            string[] rHeaders = httpRequest.Headers.AllKeys;

            keysvals.Add("uri", httpRequest.RawUrl);
            keysvals.Add("content-type", httpRequest.ContentType);
            keysvals.Add("http-method", httpRequest.HttpMethod);

            foreach (string queryname in querystringkeys)
            {
                keysvals.Add(queryname, httpRequest.QueryString[queryname]);
            }

            foreach (string headername in rHeaders)
            {
                headervals[headername] = httpRequest.Headers[headername];
            }

            keysvals.Add("headers", headervals);
            keysvals.Add("querystringkeys", querystringkeys);

            String requestBody;
            Encoding encoding = Encoding.UTF8;

            Stream inputStream = request;
            Stream innerStream = null;

            try
            {
                if ((httpRequest.ContentType == "application/x-gzip" || httpRequest.Headers["Content-Encoding"] == "gzip") || (httpRequest.Headers["X-Content-Encoding"] == "gzip"))
                {
                    innerStream = inputStream;
                    inputStream = new GZipStream(innerStream, CompressionMode.Decompress);
                }

                using (StreamReader reader = new StreamReader(inputStream, encoding))
                {
                    requestBody = reader.ReadToEnd();
                }
            }
            finally
            {
                if (innerStream != null)
                {
                    innerStream.Dispose();
                }

                inputStream.Dispose();
            }

            keysvals.Add("body", requestBody);

            httpResponse.StatusCode = 200;
            httpResponse.ContentType = "text/html";
            httpResponse.KeepAlive = false;

            Hashtable responsedata = new Hashtable();

            UUID agentID;
            UUID regionID;
            string action;

            if (!Utils.GetParams((string)keysvals["uri"], out agentID, out regionID, out action))
            {
                m_log.InfoFormat("[Agent Handler]: Invalid parameters for agent message {0}", keysvals["uri"]);

                httpResponse.StatusCode = 404;

                return encoding.GetBytes("false");
            }

            DoAgentPut(keysvals, responsedata);

            httpResponse.StatusCode = (int)responsedata["int_response_code"];
            return encoding.GetBytes((string)responsedata["str_response_string"]);
        }

        protected void DoAgentPut(Hashtable request, Hashtable responsedata)
        {
            OSDMap args = Utils.GetOSDMap((string)request["body"]);

            if (args == null)
            {
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            // retrieve the input arguments
            int x = 0, y = 0;
            UUID uuid = UUID.Zero;
            string regionname = string.Empty;

            if (args.ContainsKey("destination_x") && args["destination_x"] != null)
            {
                Int32.TryParse(args["destination_x"].AsString(), out x);
            }

            if (args.ContainsKey("destination_y") && args["destination_y"] != null)
            {
                Int32.TryParse(args["destination_y"].AsString(), out y);
            }

            if (args.ContainsKey("destination_uuid") && args["destination_uuid"] != null)
            {
                UUID.TryParse(args["destination_uuid"].AsString(), out uuid);
            }

            if (args.ContainsKey("destination_name") && args["destination_name"] != null)
            {
                regionname = args["destination_name"].ToString();
            }

            GridRegion destination = new GridRegion();
            destination.RegionID = uuid;
            destination.RegionLocX = x;
            destination.RegionLocY = y;
            destination.RegionName = regionname;

            string messageType;

            if (args["message_type"] != null)
            {
                messageType = args["message_type"].AsString();
            }
            else
            {
                m_log.Warn("[Agent Handler]: Agent Put Message Type not found. ");
                messageType = "AgentData";
            }

            bool result = true;

            if ("AgentData".Equals(messageType))
            {
                AgentData agent = new AgentData();

                try
                {
                    agent.Unpack(args, m_SimulationService.GetScene(destination.RegionID));
                }
                catch (Exception ex)
                {
                    m_log.InfoFormat("[Agent Handler]: exception on unpacking ChildAgentUpdate message {0}", ex.Message);
                    responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                    responsedata["str_response_string"] = "Bad request";
                    return;
                }

                // This is one of the meanings of PUT agent
                result = UpdateAgent(destination, agent);
            }
            else if ("AgentPosition".Equals(messageType))
            {
                AgentPosition agent = new AgentPosition();

                try
                {
                    agent.Unpack(args, m_SimulationService.GetScene(destination.RegionID));
                }
                catch (Exception ex)
                {
                    m_log.InfoFormat("[Agent Handler]: exception on unpacking ChildAgentUpdate message {0}", ex.Message);
                    return;
                }

                // This is one of the meanings of PUT agent
                result = m_SimulationService.UpdateAgent(destination, agent);
            }

            responsedata["int_response_code"] = HttpStatusCode.OK;
            responsedata["str_response_string"] = result.ToString();
        }

        // subclasses can override this
        protected virtual bool UpdateAgent(GridRegion destination, AgentData agent)
        {
            return m_SimulationService.UpdateAgent(destination, agent);
        }
    }

    public class AgentDestinationData
    {
        public int x;
        public int y;
        public string name;
        public UUID uuid;
        public uint flags;
        public bool fromLogin;
    }
}
