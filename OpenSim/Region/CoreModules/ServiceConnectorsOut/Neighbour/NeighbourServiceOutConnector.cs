/*
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

using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Services.Connectors;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.ServiceConnectorsOut.Neighbour
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "NeighbourServicesOutConnector")]
    public class NeighbourServicesOutConnector : NeighbourServicesConnector, ISharedRegionModule, INeighbourService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "NeighbourServicesOutConnector"; }
        }

        public void Initialize(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];

            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("NeighbourServices");

                if (name == Name)
                {
                    m_Enabled = true;
                    m_log.Info("[Neighbour Connector]: Neighbour out connector enabled");
                }
            }
        }

        public void PostInitialize()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
            {
                return;
            }

            m_Scenes.Add(scene);
            scene.RegisterModuleInterface<INeighbourService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            // Always remove
            if (m_Scenes.Contains(scene))
            {
                m_Scenes.Remove(scene);
            }
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
            {
                return;
            }

            m_GridService = scene.GridService;
            m_log.InfoFormat("[Neighbour Connector]: Enabled out neighbours for region {0}", scene.RegionInfo.RegionName);
        }

        #region INeighbourService

        public override GridRegion HelloNeighbour(ulong regionHandle, RegionInfo thisRegion)
        {
            if (!m_Enabled)
            {
                return null;
            }

            foreach (Scene s in m_Scenes)
            {
                if (s.RegionInfo.RegionHandle == regionHandle)
                {
                    return s.IncomingHelloNeighbour(thisRegion);
                }
            }

            return base.HelloNeighbour(regionHandle, thisRegion);
        }

        #endregion INeighbourService
    }
}
