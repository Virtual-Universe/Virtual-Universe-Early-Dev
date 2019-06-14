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
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using log4net;
using Nwc.XmlRpc;
using OpenSim.Framework;

namespace NSL.Network.XmlRpc
{
    public class NSLXmlRpcRequest : XmlRpcRequest
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Encoding _encoding = new ASCIIEncoding();
        private XmlRpcRequestSerializer _serializer = new XmlRpcRequestSerializer();
        private XmlRpcResponseDeserializer _deserializer = new XmlRpcResponseDeserializer();

        public NSLXmlRpcRequest()
        {
            _params = new ArrayList();
        }

        public NSLXmlRpcRequest(String methodName, IList parameters)
        {
            MethodName = methodName;
            _params = parameters;
        }

        public XmlRpcResponse certSend(String url, X509Certificate2 clientCert, Int32 timeout)
        {
            m_log.InfoFormat("[Virtual Universe Economy]: XmlRpcResponse certSend: connect to {0}", url);

            ServicePointManagerTimeoutSupport.ResetHosts();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            if (request == null)
            {
                throw new XmlRpcException(XmlRpcErrorCodes.TRANSPORT_ERROR, XmlRpcErrorCodes.TRANSPORT_ERROR_MSG + ": Could not create request with " + url);
            }

            request.Method = "POST";
            request.ContentType = "text/xml";
            request.AllowWriteStreamBuffering = true;
            request.Timeout = timeout;
            request.UserAgent = "NSLXmlRpcRequest";

            if (clientCert != null)
            {
                request.ClientCertificates.Add(clientCert); // 自身の証明書
            }

            Stream stream = request.GetRequestStream();
            XmlTextWriter xml = new XmlTextWriter(stream, _encoding);
            _serializer.Serialize(xml, this);
            xml.Flush();
            xml.Close();

            HttpWebResponse response = null;

            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[Virtual Universe Economy]: XmlRpcResponse certSend: GetResponse Error: {0}", ex);
                throw;
            }

            StreamReader input = new StreamReader(response.GetResponseStream());

            string inputXml = input.ReadToEnd();
            XmlRpcResponse resp = (XmlRpcResponse)_deserializer.Deserialize(inputXml);

            input.Close();
            response.Close();
            return resp;
        }
    }
}
