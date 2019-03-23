﻿/*
 * Copyright (c) Contributors, https://virtual-planets.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Virtual Universe Project nor the
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

using System.Collections;

namespace OpenSim.Framework.Servers.HttpServer
{
    public class RestHTTPHandler : BaseHTTPHandler
    {
        private GenericHTTPMethod m_dhttpMethod;

        public GenericHTTPMethod Method
        {
            get { return m_dhttpMethod; }
        }

        public RestHTTPHandler(string httpMethod, string path, GenericHTTPMethod dhttpMethod)
            : base(httpMethod, path)
        {
            m_dhttpMethod = dhttpMethod;
        }

        public RestHTTPHandler(
            string httpMethod, string path, GenericHTTPMethod dhttpMethod, string name, string description)
            : base(httpMethod, path, name, description)
        {
            m_dhttpMethod = dhttpMethod;
        }

        public override Hashtable Handle(string path, Hashtable request)
        {
            string param = GetParam(path);
            request.Add("param", param);
            request.Add("path", path);
            return m_dhttpMethod(request);
        }
    }
}
