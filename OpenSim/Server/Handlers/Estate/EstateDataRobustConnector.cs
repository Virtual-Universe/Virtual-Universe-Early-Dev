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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Net;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers
{
    public class EstateDataRobustConnector : ServiceConnector
    {
        private string m_ConfigName = "EstateService";

        public EstateDataRobustConnector(IConfigSource config, IHttpServer server, string configName) :
            base(config, server, configName)
        {
            IConfig serverConfig = config.Configs[m_ConfigName];

            if (serverConfig == null)
            {
                throw new Exception(String.Format("No section {0} in config file", m_ConfigName));
            }

            string service = serverConfig.GetString("LocalServiceModule", String.Empty);

            if (service == String.Empty)
            {
                throw new Exception("No LocalServiceModule in config file");
            }

            Object[] args = new Object[] { config };
            IEstateDataService e_service = ServerUtils.LoadPlugin<IEstateDataService>(service, args);

            IServiceAuth auth = ServiceAuth.Create(config, m_ConfigName); ;

            server.AddStreamHandler(new EstateServerGetHandler(e_service, auth));
            server.AddStreamHandler(new EstateServerPostHandler(e_service, auth));
        }
    }

    public class EstateServerGetHandler : BaseStreamHandler
    {
        IEstateDataService m_EstateService;

        public EstateServerGetHandler(IEstateDataService service, IServiceAuth auth) : base("GET", "/estates", auth)
        {
            m_EstateService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> data = null;

            string[] p = SplitParams(path);

            if (p.Length == 0)
            {
                data = GetEstates(httpRequest, httpResponse);
            }
            else
            {
                string resource = p[0];

                if ("estate".Equals(resource))
                {
                    data = GetEstate(httpRequest, httpResponse);
                }
                else if ("regions".Equals(resource))
                {
                    data = GetRegions(httpRequest, httpResponse);
                }
            }

            if (data == null)
            {
                data = new Dictionary<string, object>();
            }

            string xmlString = ServerUtils.BuildXmlResponse(data);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        private Dictionary<string, object> GetEstates(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> data = null;
            string name = (string)httpRequest.Query["name"];
            string owner = (string)httpRequest.Query["owner"];

            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(owner))
            {
                List<int> estateIDs = null;

                if (!string.IsNullOrEmpty(name))
                {
                    estateIDs = m_EstateService.GetEstates(name);
                }
                else if (!string.IsNullOrEmpty(owner))
                {
                    UUID ownerID = UUID.Zero;

                    if (UUID.TryParse(owner, out ownerID))
                    {
                        estateIDs = m_EstateService.GetEstatesByOwner(ownerID);
                    }
                }

                if (estateIDs == null || (estateIDs != null && estateIDs.Count == 0))
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    httpResponse.ContentType = "text/xml";
                    data = new Dictionary<string, object>();
                    int i = 0;

                    foreach (int id in estateIDs)
                    {
                        data["estate" + i++] = id;
                    }
                }
            }
            else
            {
                List<EstateSettings> estates = m_EstateService.LoadEstateSettingsAll();

                if (estates == null || estates.Count == 0)
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    httpResponse.ContentType = "text/xml";
                    data = new Dictionary<string, object>();
                    int i = 0;

                    foreach (EstateSettings es in estates)
                    {
                        data["estate" + i++] = es.ToMap();
                    }
                }
            }

            return data;
        }

        private Dictionary<string, object> GetEstate(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> data = null;
            string region = (string)httpRequest.Query["region"];
            string eid = (string)httpRequest.Query["eid"];

            EstateSettings estate = null;

            if (!string.IsNullOrEmpty(region))
            {
                UUID regionID = UUID.Zero;

                if (UUID.TryParse(region, out regionID))
                {
                    string create = (string)httpRequest.Query["create"];
                    bool createYN = false;
                    Boolean.TryParse(create, out createYN);
                    estate = m_EstateService.LoadEstateSettings(regionID, createYN);
                }
            }
            else if (!string.IsNullOrEmpty(eid))
            {
                int id = 0;

                if (Int32.TryParse(eid, out id))
                {
                    estate = m_EstateService.LoadEstateSettings(id);
                }
            }

            if (estate != null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
                httpResponse.ContentType = "text/xml";
                data = estate.ToMap();
            }
            else
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
            }

            return data;
        }

        private Dictionary<string, object> GetRegions(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> data = null;
            string eid = (string)httpRequest.Query["eid"];

            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;

            if (!string.IsNullOrEmpty(eid))
            {
                int id = 0;

                if (Int32.TryParse(eid, out id))
                {
                    List<UUID> regions = m_EstateService.GetRegions(id);

                    if (regions != null && regions.Count > 0)
                    {
                        data = new Dictionary<string, object>();
                        int i = 0;

                        foreach (UUID uuid in regions)
                        {
                            data["region" + i++] = uuid.ToString();
                        }

                        httpResponse.StatusCode = (int)HttpStatusCode.OK;
                        httpResponse.ContentType = "text/xml";
                    }
                }
            }

            return data;
        }
    }

    public class EstateServerPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        IEstateDataService m_EstateService;

        public EstateServerPostHandler(IEstateDataService service, IServiceAuth auth) : base("POST", "/estates", auth)
        {
            m_EstateService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> data = null;

            string[] p = SplitParams(path);

            if (p.Length > 0)
            {
                string resource = p[0];

                if ("estate".Equals(resource))
                {
                    string body;

                    using (StreamReader sr = new StreamReader(request))
                    {
                        body = sr.ReadToEnd();
                    }

                    body = body.Trim();

                    Dictionary<string, object> requestData = ServerUtils.ParseQueryString(body);

                    data = UpdateEstate(requestData, httpRequest, httpResponse);
                }
            }

            if (data == null)
            {
                data = new Dictionary<string, object>();
            }

            string xmlString = ServerUtils.BuildXmlResponse(data);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        private Dictionary<string, object> UpdateEstate(Dictionary<string, object> requestData, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            string eid = (string)httpRequest.Query["eid"];
            string region = (string)httpRequest.Query["region"];

            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;

            if (string.IsNullOrEmpty(eid) && string.IsNullOrEmpty(region) &&
                requestData.ContainsKey("OP") && requestData["OP"] != null && "STORE".Equals(requestData["OP"]))
            {
                EstateSettings es = new EstateSettings(requestData);
                m_EstateService.StoreEstateSettings(es);
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
                result["Result"] = true;
            }
            else if (!string.IsNullOrEmpty(region) && !string.IsNullOrEmpty(eid) &&
                requestData.ContainsKey("OP") && requestData["OP"] != null && "LINK".Equals(requestData["OP"]))
            {
                int id = 0;
                UUID regionID = UUID.Zero;

                if (UUID.TryParse(region, out regionID) && Int32.TryParse(eid, out id))
                {
                    m_log.DebugFormat("[Estate Server Post Handler]: Link region {0} to estate {1}", regionID, id);
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    result["Result"] = m_EstateService.LinkRegion(regionID, id);
                }
            }
            else
            {
                m_log.WarnFormat("[Estate Server Post Handler]: something wrong with POST request {0}", httpRequest.RawUrl);
            }

            return result;
        }
    }
}
