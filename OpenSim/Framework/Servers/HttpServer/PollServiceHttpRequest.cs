﻿/*
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
using System.Text;
using HttpServer;
using log4net;
using OpenMetaverse;

namespace OpenSim.Framework.Servers.HttpServer
{
    public class PollServiceHttpRequest
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public readonly PollServiceEventArgs PollServiceArgs;
        public readonly IHttpClientContext HttpContext;
        public readonly IHttpRequest Request;
        public readonly int RequestTime;
        public readonly UUID RequestID;
        public int  contextHash;

/*
        private void GenContextHash()
        {

            Random rnd = new Random();
            contextHash = 0;
            if (Request.Headers["remote_addr"] != null)
                contextHash = (Request.Headers["remote_addr"]).GetHashCode() << 16;
            else
                contextHash = rnd.Next() << 16;
            if (Request.Headers["remote_port"] != null)
            {
                string[] strPorts = Request.Headers["remote_port"].Split(new char[] { ',' });
                contextHash += Int32.Parse(strPorts[0]);
            }
            else
                contextHash += rnd.Next() & 0xffff;

        }
*/
        public PollServiceHttpRequest(
            PollServiceEventArgs pPollServiceArgs, IHttpClientContext pHttpContext, IHttpRequest pRequest)
        {
            PollServiceArgs = pPollServiceArgs;
            HttpContext = pHttpContext;
            Request = pRequest;
            RequestTime = System.Environment.TickCount;
            RequestID = UUID.Random();
//            GenContextHash();
            contextHash = HttpContext.contextID;
        }

        internal void DoHTTPGruntWork(Hashtable responsedata)
        {
            OSHttpResponse response
                = new OSHttpResponse(new HttpResponse(HttpContext, Request), HttpContext);

            byte[] buffer = srvDoHTTPGruntWork(responsedata, response);

            if(Request.Body.CanRead)
                Request.Body.Dispose();

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            try
            {
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Send();
                buffer = null;
            }
            catch (Exception ex)
            {
                if(ex is System.Net.Sockets.SocketException)
                {
                    // only mute connection reset by peer so we are not totally blind for now
                    if(((System.Net.Sockets.SocketException)ex).SocketErrorCode != System.Net.Sockets.SocketError.ConnectionReset)
                         m_log.Warn("[POLL SERVICE WORKER THREAD]: Error ", ex);
                }
                else
                    m_log.Warn("[POLL SERVICE WORKER THREAD]: Error ", ex);
            }

            PollServiceArgs.RequestsHandled++;
        }

        internal byte[] srvDoHTTPGruntWork(Hashtable responsedata, OSHttpResponse response)
        {
            int responsecode;
            string responseString = String.Empty;
            byte[] responseData = null;
            string contentType;

            if (responsedata == null)
            {
                responsecode = 500;
                responseString = "No response could be obtained";
                contentType = "text/plain";
                responsedata = new Hashtable();
            }
            else
            {
                try
                {
                    //m_log.Info("[BASE HTTP SERVER]: Doing HTTP Grunt work with response");
                    responsecode = (int)responsedata["int_response_code"];
                    if (responsedata["bin_response_data"] != null)
                        responseData = (byte[])responsedata["bin_response_data"];
                    else
                        responseString = (string)responsedata["str_response_string"];
                    contentType = (string)responsedata["content_type"];
                    if (responseString == null)
                        responseString = String.Empty;
                }
                catch
                {
                    responsecode = 500;
                    responseString = "No response could be obtained";
                    contentType = "text/plain";
                    responsedata = new Hashtable();
                }
            }

            if (responsedata.ContainsKey("error_status_text"))
            {
                response.StatusDescription = (string)responsedata["error_status_text"];
            }
            if (responsedata.ContainsKey("http_protocol_version"))
            {
                response.ProtocolVersion = (string)responsedata["http_protocol_version"];
            }

            if (responsedata.ContainsKey("keepalive"))
            {
                bool keepalive = (bool)responsedata["keepalive"];
                response.KeepAlive = keepalive;
            }

            // Cross-Origin Resource Sharing with simple requests
            if (responsedata.ContainsKey("access_control_allow_origin"))
                response.AddHeader("Access-Control-Allow-Origin", (string)responsedata["access_control_allow_origin"]);

            //Even though only one other part of the entire code uses HTTPHandlers, we shouldn't expect this
            //and should check for NullReferenceExceptions

            if (string.IsNullOrEmpty(contentType))
            {
                contentType = "text/html";
            }

            // The client ignores anything but 200 here for web login, so ensure that this is 200 for that

            response.StatusCode = responsecode;

            if (responsecode == (int)OSHttpStatusCode.RedirectMovedPermanently)
            {
                response.RedirectLocation = (string)responsedata["str_redirect_location"];
                response.StatusCode = responsecode;
            }

            response.AddHeader("Content-Type", contentType);
            if (responsedata.ContainsKey("headers"))
            {
                Hashtable headerdata = (Hashtable)responsedata["headers"];

                foreach (string header in headerdata.Keys)
                    response.AddHeader(header, headerdata[header].ToString());
            }

            byte[] buffer;

            if (responseData != null)
            {
                buffer = responseData;
            }
            else
            {
                if (!(contentType.Contains("image")
                    || contentType.Contains("x-shockwave-flash")
                    || contentType.Contains("application/x-oar")
                    || contentType.Contains("application/vnd.ll.mesh")))
                {
                    // Text
                    buffer = Encoding.UTF8.GetBytes(responseString);
                }
                else
                {
                    // Binary!
                    buffer = Convert.FromBase64String(responseString);
                }

                response.SendChunked = false;
                response.ContentLength64 = buffer.Length;
                response.ContentEncoding = Encoding.UTF8;
            }

            return buffer;
        }
        internal void DoHTTPstop()
        {
            OSHttpResponse response
                = new OSHttpResponse(new HttpResponse(HttpContext, Request), HttpContext);

            if(Request.Body.CanRead)
                Request.Body.Dispose();

            response.ContentLength64 = 0;
            response.ContentEncoding = Encoding.UTF8;
            response.KeepAlive = false;
            response.SendChunked = false;
            response.StatusCode = 503;

            try
            {
                response.Send();
            }
            catch
            {
            }
        }
    }
}