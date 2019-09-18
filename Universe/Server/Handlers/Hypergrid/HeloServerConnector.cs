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
using System.Net;
using System.Reflection;
using log4net;
using Nini.Config;
using Universe.Framework.Servers.HttpServer;
using Universe.Server.Base;
using Universe.Server.Handlers.Base;
using Universe.Services.Interfaces;

namespace Universe.Server.Handlers.Hypergrid
{
    public class HeloServiceInConnector : ServiceConnector
    {
        public HeloServiceInConnector(IConfigSource config, IHttpServer server, string configName) : base(config, server, configName)
        {
#pragma warning disable 0612
            server.AddStreamHandler(new HeloServerGetHandler("universe-robust"));
#pragma warning restore 0612
            server.AddStreamHandler(new HeloServerHeadHandler("universe-robust"));
        }
    }

    [Obsolete]
    public class HeloServerGetHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_HandlersType;

        public HeloServerGetHandler(string handlersType) : base("GET", "/helo")
        {
            m_HandlersType = handlersType;
        }

        public override byte[] Handle(string path, Stream requestData, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            return OKResponse(httpResponse);
        }

        private byte[] OKResponse(IOSHttpResponse httpResponse)
        {
            m_log.Debug("[Helo]: hi, GET was called");
            httpResponse.AddHeader("X-Handlers-Provided", m_HandlersType);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
            httpResponse.StatusDescription = "OK";
            return new byte[0];
        }
    }

    public class HeloServerHeadHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_HandlersType;

        public HeloServerHeadHandler(string handlersType) : base("HEAD", "/helo")
        {
            m_HandlersType = handlersType;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            return OKResponse(httpResponse);
        }

        private byte[] OKResponse(IOSHttpResponse httpResponse)
        {
            m_log.Debug("[Helo]: hi, HEAD was called");
            httpResponse.AddHeader("X-Handlers-Provided", m_HandlersType);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
            httpResponse.StatusDescription = "OK";
            return new byte[0];
        }
    }
}
