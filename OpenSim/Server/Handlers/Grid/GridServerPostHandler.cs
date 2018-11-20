/// <license>
///     Copyright (c) Contributors, http://virtual-planets.org/
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
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Server.Handlers.Grid
{
    public class GridServerPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

#pragma warning disable 414
        private static string LogHeader = "[GRID HANDLER]";
#pragma warning restore 414

        private IGridService m_GridService;

        public GridServerPostHandler(IGridService service, IServiceAuth auth) : base("POST", "/grid", auth)
        {
            m_GridService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string body;

            using (StreamReader sr = new StreamReader(requestData))
            {
                body = sr.ReadToEnd();
            }

            body = body.Trim();

            try
            {
                Dictionary<string, object> request = ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                {
                    return FailureResult();
                }

                string method = request["METHOD"].ToString();

                switch (method)
                {
                    case "register":
                        return Register(request);

                    case "deregister":
                        return Deregister(request);

                    case "get_neighbours":
                        return GetNeighbours(request);

                    case "get_region_by_uuid":
                        return GetRegionByUUID(request);

                    case "get_region_by_position":
                        return GetRegionByPosition(request);

                    case "get_region_by_name":
                        return GetRegionByName(request);

                    case "get_regions_by_name":
                        return GetRegionsByName(request);

                    case "get_region_range":
                        return GetRegionRange(request);

                    case "get_default_regions":
                        return GetDefaultRegions(request);

                    case "get_default_hypergrid_regions":
                        return GetDefaultHypergridRegions(request);

                    case "get_fallback_regions":
                        return GetFallbackRegions(request);

                    case "get_hyperlinks":
                        return GetHyperlinks(request);

                    case "get_region_flags":
                        return GetRegionFlags(request);

                    case "get_grid_extra_features":
                        return GetGridExtraFeatures(request);
                }

                m_log.DebugFormat("[Grid Handler]: unknown method request {0}", method);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[Grid Handler]: Exception {0} {1}", e.Message, e.StackTrace);
            }

            return FailureResult();
        }

        #region Method-specific handlers

        byte[] Register(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to register region");
            }

            int versionNumberMin = 0, versionNumberMax = 0;

            if (request.ContainsKey("VERSIONMIN"))
            {
                Int32.TryParse(request["VERSIONMIN"].ToString(), out versionNumberMin);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no minimum protocol version in request to register region");
            }

            if (request.ContainsKey("VERSIONMAX"))
            {
                Int32.TryParse(request["VERSIONMAX"].ToString(), out versionNumberMax);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no maximum protocol version in request to register region");
            }

            if ((versionNumberMin > ProtocolVersions.ServerProtocolVersionMax || versionNumberMax < ProtocolVersions.ServerProtocolVersionMin))
            {
                // Can't do, there is no overlap in the acceptable ranges
                return FailureResult();
            }

            Dictionary<string, object> rinfoData = new Dictionary<string, object>();
            GridRegion rinfo = null;

            try
            {
                foreach (KeyValuePair<string, object> kvp in request)
                {
                    rinfoData[kvp.Key] = kvp.Value.ToString();
                }

                rinfo = new GridRegion(rinfoData);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[Grid Handler]: exception unpacking region data: {0}", e);
            }

            string result = "Error communicating with grid service";

            if (rinfo != null)
            {
                result = m_GridService.RegisterRegion(scopeID, rinfo);
            }

            if (result == String.Empty)
            {
                return SuccessResult();
            }
            else
            {
                return FailureResult(result);
            }
        }

        byte[] Deregister(Dictionary<string, object> request)
        {
            UUID regionID = UUID.Zero;

            if (request.ContainsKey("REGIONID"))
            {
                UUID.TryParse(request["REGIONID"].ToString(), out regionID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no regionID in request to deregister region");
            }

            bool result = m_GridService.DeregisterRegion(regionID);

            if (result)
            {
                return SuccessResult();
            }
            else
            {
                return FailureResult();
            }
        }

        byte[] GetNeighbours(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get neighbours");
            }

            UUID regionID = UUID.Zero;

            if (request.ContainsKey("REGIONID"))
            {
                UUID.TryParse(request["REGIONID"].ToString(), out regionID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no regionID in request to get neighbours");
            }

            List<GridRegion> rinfos = m_GridService.GetNeighbours(scopeID, regionID);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionByUUID(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get neighbours");
            }

            UUID regionID = UUID.Zero;

            if (request.ContainsKey("REGIONID"))
            {
                UUID.TryParse(request["REGIONID"].ToString(), out regionID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no regionID in request to get neighbours");
            }

            GridRegion rinfo = m_GridService.GetRegionByUUID(scopeID, regionID);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if (rinfo == null)
            {
                result["result"] = "null";
            }
            else
            {
                result["result"] = rinfo.ToKeyValuePairs();
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionByPosition(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get region by position");
            }

            int x = 0, y = 0;

            if (request.ContainsKey("X"))
            {
                Int32.TryParse(request["X"].ToString(), out x);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no X in request to get region by position");
            }

            if (request.ContainsKey("Y"))
            {
                Int32.TryParse(request["Y"].ToString(), out y);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no Y in request to get region by position");
            }

            GridRegion rinfo = m_GridService.GetRegionByPosition(scopeID, x, y);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if (rinfo == null)
            {
                result["result"] = "null";
            }
            else
            {
                result["result"] = rinfo.ToKeyValuePairs();
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionByName(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get region by name");
            }

            string regionName = string.Empty;

            if (request.ContainsKey("NAME"))
            {
                regionName = request["NAME"].ToString();
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no name in request to get region by name");
            }

            GridRegion rinfo = m_GridService.GetRegionByName(scopeID, regionName);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if (rinfo == null)
            {
                result["result"] = "null";
            }
            else
            {
                result["result"] = rinfo.ToKeyValuePairs();
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionsByName(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get regions by name");
            }

            string regionName = string.Empty;

            if (request.ContainsKey("NAME"))
            {
                regionName = request["NAME"].ToString();
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no NAME in request to get regions by name");
            }

            int max = 0;

            if (request.ContainsKey("MAX"))
            {
                Int32.TryParse(request["MAX"].ToString(), out max);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no MAX in request to get regions by name");
            }

            List<GridRegion> rinfos = m_GridService.GetRegionsByName(scopeID, regionName, max);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionRange(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get region range");
            }

            int xmin = 0, xmax = 0, ymin = 0, ymax = 0;

            if (request.ContainsKey("XMIN"))
            {
                Int32.TryParse(request["XMIN"].ToString(), out xmin);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no XMIN in request to get region range");
            }

            if (request.ContainsKey("XMAX"))
            {
                Int32.TryParse(request["XMAX"].ToString(), out xmax);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no XMAX in request to get region range");
            }

            if (request.ContainsKey("YMIN"))
            {
                Int32.TryParse(request["YMIN"].ToString(), out ymin);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no YMIN in request to get region range");
            }

            if (request.ContainsKey("YMAX"))
            {
                Int32.TryParse(request["YMAX"].ToString(), out ymax);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no YMAX in request to get region range");
            }

            List<GridRegion> rinfos = m_GridService.GetRegionRange(scopeID, xmin, xmax, ymin, ymax);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetDefaultRegions(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get region range");
            }

            List<GridRegion> rinfos = m_GridService.GetDefaultRegions(scopeID);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetDefaultHypergridRegions(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get region range");
            }

            List<GridRegion> rinfos = m_GridService.GetDefaultHypergridRegions(scopeID);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetFallbackRegions(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get fallback regions");
            }

            int x = 0, y = 0;

            if (request.ContainsKey("X"))
            {
                Int32.TryParse(request["X"].ToString(), out x);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no X in request to get fallback regions");
            }

            if (request.ContainsKey("Y"))
            {
                Int32.TryParse(request["Y"].ToString(), out y);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no Y in request to get fallback regions");
            }

            List<GridRegion> rinfos = m_GridService.GetFallbackRegions(scopeID, x, y);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetHyperlinks(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get linked regions");
            }

            List<GridRegion> rinfos = m_GridService.GetHyperlinks(scopeID);

            Dictionary<string, object> result = new Dictionary<string, object>();

            if ((rinfos == null) || ((rinfos != null) && (rinfos.Count == 0)))
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;

                foreach (GridRegion rinfo in rinfos)
                {
                    Dictionary<string, object> rinfoDict = rinfo.ToKeyValuePairs();
                    result["region" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetRegionFlags(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;

            if (request.ContainsKey("SCOPEID"))
            {
                UUID.TryParse(request["SCOPEID"].ToString(), out scopeID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no scopeID in request to get RegionFlags");
            }

            UUID regionID = UUID.Zero;

            if (request.ContainsKey("REGIONID"))
            {
                UUID.TryParse(request["REGIONID"].ToString(), out regionID);
            }
            else
            {
                m_log.WarnFormat("[Grid Handler]: no regionID in request to get RegionFlags");
            }

            int flags = m_GridService.GetRegionFlags(scopeID, regionID);

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["result"] = flags.ToString();

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetGridExtraFeatures(Dictionary<string, object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            Dictionary<string, object> extraFeatures = m_GridService.GetExtraFeatures();

            foreach (string key in extraFeatures.Keys)
            {
                result[key] = extraFeatures[key];
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        #endregion

        #region Misc

        private byte[] SuccessResult()
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "Result", "");
            result.AppendChild(doc.CreateTextNode("Success"));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
        }

        private byte[] FailureResult()
        {
            return FailureResult(String.Empty);
        }

        private byte[] FailureResult(string msg)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "Result", "");
            result.AppendChild(doc.CreateTextNode("Failure"));

            rootElement.AppendChild(result);

            XmlElement message = doc.CreateElement("", "Message", "");
            message.AppendChild(doc.CreateTextNode(msg));

            rootElement.AppendChild(message);

            return Util.DocToBytes(doc);
        }

        #endregion
    }
}
