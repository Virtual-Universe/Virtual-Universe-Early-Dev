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
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Timers;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using Universe.Framework;
using Universe.Framework.Monitoring;
using Universe.Region.Framework.Interfaces;
using Universe.Region.Framework.Scenes;
using Universe.Services.Interfaces;
using PermissionMask = Universe.Framework.PermissionMask;

namespace Universe.Region.CoreModules.Avatar.AvatarFactory
{
    [Extension(Path = "/Universe/RegionModules", NodeName = "RegionModule", Id = "AvatarFactoryModule")]
    public class AvatarFactoryModule : IAvatarFactoryModule, INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const string BAKED_TEXTURES_REPORT_FORMAT = "{0,-9}  {1}";

        private Scene m_scene = null;

        private int m_savetime = 5; // seconds to wait before saving changed appearance
        private int m_sendtime = 2; // seconds to wait before sending changed appearance
        private bool m_reusetextures = false;

        private int m_checkTime = 500; // milliseconds to wait between checks for appearance updates
        private System.Timers.Timer m_updateTimer = new System.Timers.Timer();
        private ConcurrentDictionary<UUID, long> m_savequeue = new ConcurrentDictionary<UUID, long>();
        private ConcurrentDictionary<UUID, long> m_sendqueue = new ConcurrentDictionary<UUID, long>();
        private object m_updatesLock = new object();
        private int m_updatesbusy = 0;

        private object m_setAppearanceLock = new object();

        #region Region Module interface

        public void Initialise(IConfigSource config)
        {

            IConfig appearanceConfig = config.Configs["Appearance"];

            if (appearanceConfig != null)
            {
                m_savetime = Convert.ToInt32(appearanceConfig.GetString("DelayBeforeAppearanceSave", Convert.ToString(m_savetime)));
                m_sendtime = Convert.ToInt32(appearanceConfig.GetString("DelayBeforeAppearanceSend", Convert.ToString(m_sendtime)));
                m_reusetextures = appearanceConfig.GetBoolean("ReuseTextures", m_reusetextures);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (m_scene == null)
            {
                m_scene = scene;
            }

            scene.RegisterModuleInterface<IAvatarFactoryModule>(this);
            scene.EventManager.OnNewClient += SubscribeToClientEvents;
        }

        public void RemoveRegion(Scene scene)
        {
            if (scene == m_scene)
            {
                scene.UnregisterModuleInterface<IAvatarFactoryModule>(this);
                scene.EventManager.OnNewClient -= SubscribeToClientEvents;
            }

            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            m_updateTimer.Enabled = false;
            m_updateTimer.AutoReset = true;
            m_updateTimer.Interval = m_checkTime; // 500 milliseconds wait to start async ops
            m_updateTimer.Elapsed += new ElapsedEventHandler(HandleAppearanceUpdateTimer);
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "Default Avatar Factory"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void SubscribeToClientEvents(IClientAPI client)
        {
            client.OnRequestWearables += Client_OnRequestWearables;
            client.OnSetAppearance += Client_OnSetAppearance;
            client.OnAvatarNowWearing += Client_OnAvatarNowWearing;
        }

        #endregion

        #region IAvatarFactoryModule

        /// <summary>
        /// </summary>
        /// <param name="sp"></param>
        /// <param name="texture"></param>
        /// <param name="visualParam"></param>
        public void SetAppearance(IScenePresence sp, AvatarAppearance appearance, WearableCacheItem[] cacheItems)
        {
            SetAppearance(sp, appearance.Texture, appearance.VisualParams, cacheItems);
        }

        public void SetAppearance(IScenePresence sp, Primitive.TextureEntry textureEntry, byte[] visualParams, Vector3 avSize, WearableCacheItem[] cacheItems)
        {
            float oldoff = sp.Appearance.AvatarFeetOffset;
            Vector3 oldbox = sp.Appearance.AvatarBoxSize;

            SetAppearance(sp, textureEntry, visualParams, cacheItems);
            sp.Appearance.SetSize(avSize);

            float off = sp.Appearance.AvatarFeetOffset;
            Vector3 box = sp.Appearance.AvatarBoxSize;

            if (oldoff != off || oldbox != box)
            {
                ((ScenePresence)sp).SetSize(box, off);
            }
        }

        /// <summary>
        /// Set appearance data (texture asset IDs and slider settings)
        /// </summary>
        /// <param name="sp"></param>
        /// <param name="texture"></param>
        /// <param name="visualParam"></param>
        public void SetAppearance(IScenePresence sp, Primitive.TextureEntry textureEntry, byte[] visualParams, WearableCacheItem[] cacheItems)
        {
            /// <summary>
            /// TODO:
            /// THis is probably not necessary any longer,
            /// just assume the textureEntry set implies
            /// that the appearance transition is complete
            /// </summary>
            bool changed = false;

            /// <summary>
            /// Process the texture entry transactionally,
            /// this does not guarantee that Appearance is
            /// going to be handled correctly but it does
            /// serialize the updates to the appearance.
            /// </summary>
            lock (m_setAppearanceLock)
            {
                /// <summary>
                /// Process the visual params,
                /// this may change height as well
                /// </summary>
                if (visualParams != null)
                {
                    changed = sp.Appearance.SetVisualParams(visualParams);
                }

                /// <summary>
                /// Process the baked texture array
                /// </summary>
                if (textureEntry != null)
                {
                    m_log.DebugFormat("[Avatar Factory]: Received texture update for {0} {1}", sp.Name, sp.UUID);

                    changed = sp.Appearance.SetTextureEntries(textureEntry) || changed;

                    UpdateBakedTextureCache(sp, cacheItems);

                    /// <summary>
                    /// This appears to be set only in the
                    /// final stages of the appearance update
                    /// transaction.  In theory, we should be able
                    /// to do an immediate appearance send and save here.
                    /// </summary>
                }

                /// <summary>
                /// NPC should send to clients immediately
                /// and skip saving appearance
                /// </summary>
                if (((ScenePresence)sp).PresenceType == PresenceType.Npc)
                {
                    SendAppearance((ScenePresence)sp);
                    return;
                }

                /// <summary>
                /// Save only if there were changes,
                /// send no matter what (it does not hurt
                /// to send twice)
                /// </summary>
                if (changed)
                {
                    QueueAppearanceSave(sp.ControllingClient.AgentId);
                }

                QueueAppearanceSend(sp.ControllingClient.AgentId);
            }
        }

        private void SendAppearance(ScenePresence sp)
        {
            /// <summary>
            /// Send the appearance to everyone in the scene
            /// </summary>
            sp.SendAppearanceToAllOtherAgents();

            /// <summary>
            /// Send animations back to the avatar as well
            /// </summary>
            if (sp.Animator != null)
            {
                sp.Animator.SendAnimPack();
            }
        }

        public bool SendAppearance(UUID agentId)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentId);

            if (sp == null || sp.IsDeleted)
            {
                return false;
            }

            SendAppearance(sp);
            return true;
        }

        public Dictionary<BakeType, Primitive.TextureEntryFace> GetBakedTextureFaces(UUID agentId)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentId);

            if (sp == null)
            {
                return new Dictionary<BakeType, Primitive.TextureEntryFace>();
            }

            return GetBakedTextureFaces(sp);
        }

        public WearableCacheItem[] GetCachedItems(UUID agentId)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentId);
            WearableCacheItem[] items = sp.Appearance.WearableCacheItems;
            return items;
        }

        public bool SaveBakedTextures(UUID agentId)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentId);

            if (sp == null)
            {
                return false;
            }

            m_log.DebugFormat(
                "[Avatar Factory]: Permanently saving baked textures for {0} in {1}",
                sp.Name, m_scene.RegionInfo.RegionName);

            Dictionary<BakeType, Primitive.TextureEntryFace> bakedTextures = GetBakedTextureFaces(sp);

            if (bakedTextures.Count == 0)
            {
                return false;
            }

            IAssetCache cache = sp.Scene.RequestModuleInterface<IAssetCache>();

            if (cache == null)
            {
                return true; // no baked local caching so nothing to do
            }

            foreach (BakeType bakeType in bakedTextures.Keys)
            {
                Primitive.TextureEntryFace bakedTextureFace = bakedTextures[bakeType];

                if (bakedTextureFace == null)
                {
                    continue;
                }

                AssetBase asset;
                cache.Get(bakedTextureFace.TextureID.ToString(), out asset);

                if (asset != null && asset.Local)
                {
                    /// <summary>
                    /// Cache does not update asset contents
                    /// </summary>
                    cache.Expire(bakedTextureFace.TextureID.ToString());

                    /// <summary>
                    /// Replace an HG ID with the simple asset
                    /// ID so that we can persist textures for
                    /// foreign HG avatars
                    /// </summary>
                    asset.ID = asset.FullID.ToString();

                    asset.Temporary = false;
                    asset.Local = false;
                    m_scene.AssetService.Store(asset);
                }

                if (asset == null)
                {
                    m_log.WarnFormat(
                        "[Avatar Factory]: Baked texture id {0} not found for bake {1} for avatar {2} in {3} when trying to save permanently",
                        bakedTextureFace.TextureID, bakeType, sp.Name, m_scene.RegionInfo.RegionName);
                }
            }

            return true;
        }

        /// <summary>
        /// Queue up a request to send appearance.
        /// </summary>
        /// <remarks>
        /// Makes it possible to accumulate changes 
        /// without sending out each one separately.
        /// </remarks>
        /// <param name="agentId"></param>
        public void QueueAppearanceSend(UUID agentid)
        {
            // 10000 ticks per millisecond, 1000 milliseconds per second
            long timestamp = DateTime.Now.Ticks + Convert.ToInt64(m_sendtime * 1000 * 10000);
            m_sendqueue[agentid] = timestamp;
            m_updateTimer.Start();
        }

        public void QueueAppearanceSave(UUID agentid)
        {
            // 10000 ticks per millisecond, 1000 milliseconds per second
            long timestamp = DateTime.Now.Ticks + Convert.ToInt64(m_savetime * 1000 * 10000);
            m_savequeue[agentid] = timestamp;
            m_updateTimer.Start();
        }

        // called on textures update
        public bool UpdateBakedTextureCache(IScenePresence sp, WearableCacheItem[] cacheItems)
        {
            if (cacheItems == null || cacheItems.Length == 0)
            {
                return false;
            }

            // npcs dont have baked cache
            if (((ScenePresence)sp).IsNPC)
            {
                return true;
            }

            // uploaded baked textures will be in assets local cache
            IAssetCache cache = m_scene.RequestModuleInterface<IAssetCache>();

            int validDirtyBakes = 0;
            int hits = 0;

            // our main cacheIDs mapper is p.Appearance.WearableCacheItems
            bool hadSkirt = false;

            WearableCacheItem[] wearableCache = sp.Appearance.WearableCacheItems;

            if (wearableCache == null)
            {
                wearableCache = WearableCacheItem.GetDefaultCacheItem();
            }
            else
            {
                hadSkirt = (wearableCache[19].TextureID != UUID.Zero);
            }

            HashSet<uint> updatedFaces = new HashSet<uint>();
            List<UUID> missing = new List<UUID>();

            // Process received baked textures
            for (int i = 0; i < cacheItems.Length; i++)
            {
                uint idx = cacheItems[i].TextureIndex;

                if (idx >= AvatarAppearance.TEXTURE_COUNT)
                {
                    hits++;
                    continue;
                }

                updatedFaces.Add(idx);

                wearableCache[idx].TextureAsset = null; // just in case
                Primitive.TextureEntryFace face = sp.Appearance.Texture.FaceTextures[idx];

                if (face == null || face.TextureID == UUID.Zero || face.TextureID == AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                {
                    wearableCache[idx].CacheId = UUID.Zero;
                    wearableCache[idx].TextureID = UUID.Zero;

                    if (idx == 19)
                    {
                        hits++;

                        if (hadSkirt)
                        {
                            validDirtyBakes++;
                        }
                    }

                    continue;
                }

                if (cache != null)
                {
                    AssetBase asb = null;
                    cache.Get(face.TextureID.ToString(), out asb);
                    wearableCache[idx].TextureAsset = asb;
                }

                if (wearableCache[idx].TextureAsset != null)
                {
                    if (wearableCache[idx].TextureID != face.TextureID || wearableCache[idx].CacheId != cacheItems[i].CacheId)
                    {
                        validDirtyBakes++;
                    }

                    wearableCache[idx].TextureID = face.TextureID;
                    wearableCache[idx].CacheId = cacheItems[i].CacheId;
                    hits++;
                }
                else
                {
                    wearableCache[idx].CacheId = UUID.Zero;
                    wearableCache[idx].TextureID = UUID.Zero;
                    missing.Add(face.TextureID);
                    continue;
                }
            }

            // this may be a current fs bug
            for (int i = AvatarAppearance.BAKES_COUNT_PV7; i < AvatarAppearance.BAKE_INDICES.Length; i++)
            {
                uint idx = AvatarAppearance.BAKE_INDICES[i];

                if (updatedFaces.Contains(idx))
                {
                    continue;
                }

                sp.Appearance.Texture.FaceTextures[idx] = null;

                wearableCache[idx].CacheId = UUID.Zero;
                wearableCache[idx].TextureID = UUID.Zero;
                wearableCache[idx].TextureAsset = null;
            }

            sp.Appearance.WearableCacheItems = wearableCache;

            if (missing.Count > 0)
            {
                foreach (UUID id in missing)
                {
                    sp.ControllingClient.SendRebakeAvatarTextures(id);
                }
            }

            bool changed = false;

            if (validDirtyBakes > 0 && hits == cacheItems.Length)
            {
                /// <summary>
                /// If we got a full set of baked textures
                /// save all in BakedTextureModule
                /// </summary>
                IBakedTextureModule m_BakedTextureModule = m_scene.RequestModuleInterface<IBakedTextureModule>();

                if (m_BakedTextureModule != null)
                {
                    m_log.DebugFormat("[Update Baked Cache]: Uploading to Bakes Server: cache hits: {0} changed entries: {1} rebakes {2}",
                        hits.ToString(), validDirtyBakes.ToString(), missing.Count);

                    m_BakedTextureModule.Store(sp.UUID, wearableCache);
                    changed = true;
                }
            }
            else
            {
                m_log.DebugFormat("[Update Baked Cache]: cache hits: {0} changed entries: {1} rebakes {2}",
                    hits.ToString(), validDirtyBakes.ToString(), missing.Count);
            }

            for (int iter = 0; iter < AvatarAppearance.BAKE_INDICES.Length; iter++)
            {
                int j = AvatarAppearance.BAKE_INDICES[iter];
                sp.Appearance.WearableCacheItems[j].TextureAsset = null;
            }

            return changed;
        }

        // called when we get a new root avatar
        public bool ValidateBakedTextureCache(IScenePresence sp)
        {
            if (((ScenePresence)sp).IsNPC)
            {
                return true;
            }

            int hits = 0;

            IAssetCache cache = m_scene.RequestModuleInterface<IAssetCache>();

            if (cache == null)
            {
                return false;
            }

            IBakedTextureModule bakedModule = m_scene.RequestModuleInterface<IBakedTextureModule>();

            lock (m_setAppearanceLock)
            {
                WearableCacheItem[] wearableCache = sp.Appearance.WearableCacheItems;

                bool wearableCacheValid = false;

                if (wearableCache == null)
                {
                    wearableCache = WearableCacheItem.GetDefaultCacheItem();
                }
                else
                {
                    wearableCacheValid = true;
                    Primitive.TextureEntryFace face;

                    for (int i = 0; i < AvatarAppearance.BAKE_INDICES.Length; i++)
                    {
                        int idx = AvatarAppearance.BAKE_INDICES[i];
                        face = sp.Appearance.Texture.FaceTextures[idx];

                        if (face == null || face.TextureID == AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                        {
                            wearableCache[idx].CacheId = UUID.Zero;
                            wearableCache[idx].TextureID = AppearanceManager.DEFAULT_AVATAR_TEXTURE;
                            hits++;
                            continue;
                        }

                        if (face.TextureID == wearableCache[idx].TextureID && face.TextureID != UUID.Zero)
                        {
                            if (cache.Check((wearableCache[idx].TextureID).ToString()))
                            {
                                hits++;
                                continue;
                            }
                        }

                        wearableCache[idx].CacheId = UUID.Zero;
                        wearableCache[idx].TextureID = AppearanceManager.DEFAULT_AVATAR_TEXTURE;
                        wearableCacheValid = false;
                    }
                }

                bool checkExternal = false;

                if (!wearableCacheValid)
                {
                    checkExternal = bakedModule != null;
                }

                if (checkExternal)
                {
                    WearableCacheItem[] bakedModuleCache = null;
                    hits = 0;

                    try
                    {
                        bakedModuleCache = bakedModule.Get(sp.UUID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(e.ToString());
                        bakedModuleCache = null;
                    }

                    if (bakedModuleCache != null)
                    {
                        m_log.Debug("[Validate Baked Cache]: got bakedModule " + bakedModuleCache.Length + " cached textures");

                        for (int i = 0; i < bakedModuleCache.Length; i++)
                        {
                            int j = (int)bakedModuleCache[i].TextureIndex;

                            if (j < AvatarAppearance.TEXTURE_COUNT && bakedModuleCache[i].TextureAsset != null)
                            {
                                wearableCache[j].TextureID = bakedModuleCache[i].TextureID;
                                wearableCache[j].CacheId = bakedModuleCache[i].CacheId;
                                wearableCache[j].TextureAsset = bakedModuleCache[i].TextureAsset;
                                bakedModuleCache[i].TextureAsset.Temporary = true;
                                bakedModuleCache[i].TextureAsset.Local = true;
                                cache.Cache(bakedModuleCache[i].TextureAsset);
                            }
                        }

                        // force the ones we got
                        for (int i = 0; i < AvatarAppearance.BAKE_INDICES.Length; i++)
                        {
                            int idx = AvatarAppearance.BAKE_INDICES[i];

                            if (wearableCache[idx].TextureAsset == null)
                            {
                                if (idx == 19)
                                {
                                    sp.Appearance.Texture.FaceTextures[idx] = null;
                                    hits++;
                                }
                                else if (sp.Appearance.Texture.FaceTextures[idx] == null ||
                                    sp.Appearance.Texture.FaceTextures[idx].TextureID == AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                                {
                                    hits++;
                                }

                                wearableCache[idx].TextureID = AppearanceManager.DEFAULT_AVATAR_TEXTURE;
                                wearableCache[idx].CacheId = UUID.Zero;
                                continue;
                            }

                            Primitive.TextureEntryFace face = sp.Appearance.Texture.GetFace((uint)idx);
                            face.TextureID = wearableCache[idx].TextureID;
                            hits++;
                            wearableCache[idx].TextureAsset = null;
                        }
                    }
                }

                sp.Appearance.WearableCacheItems = wearableCache;
            }

            return (hits >= AvatarAppearance.BAKE_INDICES.Length); // skirt is optional
        }

        public int RequestRebake(IScenePresence sp, bool missingTexturesOnly)
        {
            if (((ScenePresence)sp).IsNPC)
            {
                return 0;
            }

            int texturesRebaked = 0;
            IAssetCache cache = m_scene.RequestModuleInterface<IAssetCache>();

            for (int i = 0; i < AvatarAppearance.BAKE_INDICES.Length; i++)
            {
                int idx = AvatarAppearance.BAKE_INDICES[i];
                Primitive.TextureEntryFace face = sp.Appearance.Texture.FaceTextures[idx];

                // if there is no texture entry, skip it
                if (face == null)
                {
                    continue;
                }

                if (face.TextureID == UUID.Zero || face.TextureID == AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                {
                    continue;
                }

                if (missingTexturesOnly)
                {
                    if (cache != null && cache.Check(face.TextureID.ToString()))
                    {
                        continue;
                    }
                    else
                    {
                        m_log.DebugFormat("[Avatar Factory]: Missing baked texture {0} ({1}) for {2}, requesting rebake.",
                            face.TextureID, idx, sp.Name);
                    }
                }
                else
                {
                    m_log.DebugFormat("[Avatar Factory]: Requesting rebake of {0} ({1}) for {2}.", face.TextureID, idx, sp.Name);
                }

                texturesRebaked++;
                sp.ControllingClient.SendRebakeAvatarTextures(face.TextureID);
            }

            return texturesRebaked;
        }

        #endregion

        #region AvatarFactoryModule private methods

        private Dictionary<BakeType, Primitive.TextureEntryFace> GetBakedTextureFaces(ScenePresence sp)
        {
            if (sp.IsChildAgent)
            {
                return new Dictionary<BakeType, Primitive.TextureEntryFace>();
            }

            Dictionary<BakeType, Primitive.TextureEntryFace> bakedTextures = new Dictionary<BakeType, Primitive.TextureEntryFace>();

            AvatarAppearance appearance = sp.Appearance;
            Primitive.TextureEntryFace[] faceTextures = appearance.Texture.FaceTextures;

            foreach (int i in Enum.GetValues(typeof(BakeType)))
            {
                BakeType bakeType = (BakeType)i;

                if (bakeType == BakeType.NumberOfEntries)
                {
                    break;
                }

                if (bakeType == BakeType.Unknown)
                {
                    continue;
                }

                int ftIndex = (int)AppearanceManager.BakeTypeToAgentTextureIndex(bakeType);

                /// <summary>
                /// This will be null if there is
                /// no such baked texture
                /// </summary>
                Primitive.TextureEntryFace texture = faceTextures[ftIndex];
                bakedTextures[bakeType] = texture;
            }

            return bakedTextures;
        }

        private void HandleAppearanceUpdateTimer(object sender, EventArgs ea)
        {
            if (Monitor.TryEnter(m_updatesLock))
            {
                UUID id;
                long now = DateTime.Now.Ticks;

                foreach (KeyValuePair<UUID, long> kvp in m_sendqueue)
                {
                    long sendTime = kvp.Value;

                    if (sendTime > now)
                    {
                        continue;
                    }

                    id = kvp.Key;
                    m_sendqueue.TryRemove(id, out sendTime);
                    SendAppearance(id);
                }

                if (m_updatesbusy == 0)
                {
                    m_updatesbusy = -1;
                    List<UUID> saves = new List<UUID>(m_savequeue.Count);

                    foreach (KeyValuePair<UUID, long> kvp in m_savequeue)
                    {
                        long sendTime = kvp.Value;

                        if (sendTime > now)
                        {
                            continue;
                        }

                        id = kvp.Key;
                        m_savequeue.TryRemove(id, out sendTime);
                        saves.Add(id);
                    }

                    m_updatesbusy = 0;

                    if (saves.Count > 0)
                    {
                        ++m_updatesbusy;
                        WorkManager.RunInThreadPool(
                            delegate
                            {
                                SaveAppearance(saves);
                                saves = null;
                                --m_updatesbusy;
                            }, null, string.Format("SaveAppearance ({0})", m_scene.Name));
                    }
                }

                if (m_savequeue.Count == 0 && m_sendqueue.Count == 0)
                {
                    m_updateTimer.Stop();
                }

                Monitor.Exit(m_updatesLock);
            }
        }

        private void SaveAppearance(List<UUID> ids)
        {
            foreach (UUID id in ids)
            {
                ScenePresence sp = m_scene.GetScenePresence(id);

                if (sp == null)
                {
                    continue;
                }

                /// <summary>
                /// This could take a while since it needs to
                /// pull inventory.  We need to do it at the point
                /// of save so that there is a sufficient delay
                /// for any upload of new body part/ shape assets
                /// and item asset id change to complete.
                /// 
                /// We should not need to worry about doing this
                /// within m_setAppearanceLock since the queueing
                /// avoids multiple save requests.
                /// </summary>
                SetAppearanceAssets(id, sp.Appearance);

                m_scene.AvatarService.SetAppearance(id, sp.Appearance);
            }
        }

        /// <summary>
        /// For a given set of appearance items, check
        /// whether the items are valid and add their asset IDs to
        /// appearance data.
        /// </summary>
        /// <param name='userID'></param>
        /// <param name='appearance'></param>
        private void SetAppearanceAssets(UUID userID, AvatarAppearance appearance)
        {
            IInventoryService invService = m_scene.InventoryService;

            if (invService.GetRootFolder(userID) != null)
            {
                for (int i = 0; i < appearance.Wearables.Length; i++)
                {
                    for (int j = 0; j < appearance.Wearables[i].Count; j++)
                    {
                        if (appearance.Wearables[i][j].ItemID == UUID.Zero)
                        {
                            m_log.WarnFormat(
                                "[Avatar Factory]: Wearable item {0}:{1} for user {2} unexpectedly UUID.Zero.  Ignoring.",
                                i, j, userID);

                            continue;
                        }

                        // Ignore ruth's assets
                        if (i < AvatarWearable.DefaultWearables.Length)
                        {
                            if (appearance.Wearables[i][j].ItemID == AvatarWearable.DefaultWearables[i][0].ItemID)
                            {
                                continue;
                            }
                        }

                        InventoryItemBase baseItem = invService.GetItem(userID, appearance.Wearables[i][j].ItemID);

                        if (baseItem != null)
                        {
                            appearance.Wearables[i].Add(appearance.Wearables[i][j].ItemID, baseItem.AssetID);
                        }
                        else
                        {
                            m_log.WarnFormat(
                                "[Avatar Factory]: Can't find inventory item {0} for {1}, setting to default",
                                appearance.Wearables[i][j].ItemID, (WearableType)i);

                            appearance.Wearables[i].RemoveItem(appearance.Wearables[i][j].ItemID);
                        }
                    }
                }
            }
            else
            {
                m_log.WarnFormat("[Avatar Factory]: user {0} has no inventory, appearance isn't going to work", userID);
            }
        }

        private void TryAndRepairBrokenWearable(WearableType type, IInventoryService invService, UUID userID, AvatarAppearance appearance)
        {
            UUID defaultwearable = GetDefaultItem(type);

            if (defaultwearable != UUID.Zero)
            {
                UUID newInvItem = UUID.Random();
                InventoryItemBase itembase = new InventoryItemBase(newInvItem, userID)
                {
                    AssetID = defaultwearable,
                    AssetType = (int)FolderType.BodyPart,
                    CreatorId = userID.ToString(),
                    Description = "Failed Wearable Replacement",
                    Folder = invService.GetFolderForType(userID, FolderType.BodyPart).ID,
                    Flags = (uint)type,
                    Name = Enum.GetName(typeof(WearableType), type),
                    BasePermissions = (uint)PermissionMask.Copy,
                    CurrentPermissions = (uint)PermissionMask.Copy,
                    EveryOnePermissions = (uint)PermissionMask.Copy,
                    GroupPermissions = (uint)PermissionMask.Copy,
                    NextPermissions = (uint)PermissionMask.Copy
                };

                invService.AddItem(itembase);
                UUID LinkInvItem = UUID.Random();
                itembase = new InventoryItemBase(LinkInvItem, userID)
                {
                    AssetID = newInvItem,
                    AssetType = (int)AssetType.Link,
                    CreatorId = userID.ToString(),
                    InvType = (int)InventoryType.Wearable,
                    Description = "Failed Wearable Replacement",
                    Folder = invService.GetFolderForType(userID, FolderType.CurrentOutfit).ID,
                    Flags = (uint)type,
                    Name = Enum.GetName(typeof(WearableType), type),
                    BasePermissions = (uint)PermissionMask.Copy,
                    CurrentPermissions = (uint)PermissionMask.Copy,
                    EveryOnePermissions = (uint)PermissionMask.Copy,
                    GroupPermissions = (uint)PermissionMask.Copy,
                    NextPermissions = (uint)PermissionMask.Copy
                };

                invService.AddItem(itembase);
                appearance.Wearables[(int)type] = new AvatarWearable(newInvItem, GetDefaultItem(type));
                ScenePresence presence = null;

                if (m_scene.TryGetScenePresence(userID, out presence))
                {
                    m_scene.SendInventoryUpdate(presence.ControllingClient, invService.GetFolderForType(userID, FolderType.CurrentOutfit), false, true);
                }
            }
        }

        private UUID GetDefaultItem(WearableType wearable)
        {
            // These are ruth
            UUID ret = UUID.Zero;

            switch (wearable)
            {
                case WearableType.Eyes:
                    ret = new UUID("4bb6fa4d-1cd2-498a-a84c-95c1a0e745a7");
                    break;
                case WearableType.Hair:
                    ret = new UUID("d342e6c0-b9d2-11dc-95ff-0800200c9a66");
                    break;
                case WearableType.Pants:
                    ret = new UUID("00000000-38f9-1111-024e-222222111120");
                    break;
                case WearableType.Shape:
                    ret = new UUID("66c41e39-38f9-f75a-024e-585989bfab73");
                    break;
                case WearableType.Shirt:
                    ret = new UUID("00000000-38f9-1111-024e-222222111110");
                    break;
                case WearableType.Skin:
                    ret = new UUID("77c41e39-38f9-f75a-024e-585989bbabbb");
                    break;
                case WearableType.Undershirt:
                    ret = new UUID("16499ebb-3208-ec27-2def-481881728f47");
                    break;
                case WearableType.Underpants:
                    ret = new UUID("4ac2e9c7-3671-d229-316a-67717730841d");
                    break;
            }

            return ret;
        }

        #endregion

        #region Client Event Handlers

        /// <summary>
        /// Tell the client for this scene presence
        /// what items it should be wearing now
        /// </summary>
        /// <param name="client"></param>
        private void Client_OnRequestWearables(IClientAPI client)
        {
            Util.FireAndForget(delegate (object x)
            {
                Thread.Sleep(4000);

                ScenePresence sp = m_scene.GetScenePresence(client.AgentId);

                if (sp != null)
                {
                    client.SendWearables(sp.Appearance.Wearables, sp.Appearance.Serial++);
                }
                else
                {
                    m_log.WarnFormat("[Avatar Factory]: Client_OnRequestWearables unable to find presence for {0}", client.AgentId);
                }
            }, null, "AvatarFactoryModule.OnClientRequestWearables");
        }

        /// <summary>
        /// Set appearance data (texture asset IDs
        /// and slider settings) received from a client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="texture"></param>
        /// <param name="visualParam"></param>
        private void Client_OnSetAppearance(IClientAPI client, Primitive.TextureEntry textureEntry, byte[] visualParams, Vector3 avSize, WearableCacheItem[] cacheItems)
        {
            ScenePresence sp = m_scene.GetScenePresence(client.AgentId);

            if (sp != null)
            {
                SetAppearance(sp, textureEntry, visualParams, avSize, cacheItems);
            }
            else
            {
                m_log.WarnFormat("[Avatar Factory]: Client_OnSetAppearance unable to find presence for {0}", client.AgentId);
            }
        }

        /// <summary>
        /// Update what the avatar is wearing using 
        /// an item from their inventory.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="e"></param>
        private void Client_OnAvatarNowWearing(IClientAPI client, AvatarWearingArgs e)
        {
            ScenePresence sp = m_scene.GetScenePresence(client.AgentId);

            if (sp == null)
            {
                m_log.WarnFormat("[Avatar Factory]: Client_OnAvatarNowWearing unable to find presence for {0}", client.AgentId);
                return;
            }

            // operate on a copy of the appearance so we don't have to lock anything yet
            AvatarAppearance avatAppearance = new AvatarAppearance(sp.Appearance, false);

            foreach (AvatarWearingArgs.Wearable wear in e.NowWearing)
            {
                // If the wearable type is larger than the current array, expand it
                if (avatAppearance.Wearables.Length <= wear.Type)
                {
                    int currentLength = avatAppearance.Wearables.Length;
                    AvatarWearable[] wears = avatAppearance.Wearables;
                    Array.Resize(ref wears, wear.Type + 1);

                    for (int i = currentLength; i <= wear.Type; i++)
                    {
                        wears[i] = new AvatarWearable();
                    }

                    avatAppearance.Wearables = wears;
                }

                avatAppearance.Wearables[wear.Type].Add(wear.ItemID, UUID.Zero);
            }

            avatAppearance.GetAssetsFrom(sp.Appearance);

            lock (m_setAppearanceLock)
            {
                /// <summary>
                /// Update only those fields that we have changed.
                /// This is important because the viewer often sends
                /// AvatarIsWearing and SetAppearance packets at once,
                /// and AvatarIsWearing should not overwrite the changes
                /// made in SetAppearance.
                /// </summary>
                sp.Appearance.Wearables = avatAppearance.Wearables;

                /// <summary>
                /// We do not need to send the appearance here
                /// since the "iswearing" will trigger a new set
                /// of visual param and baked texture changes.
                /// 
                /// When those complete, the new appearance will be sent.
                /// </summary>
                QueueAppearanceSave(client.AgentId);
            }
        }

        #endregion

        public void WriteBakedTexturesReport(IScenePresence sp, ReportOutputAction outputAction)
        {
            outputAction("For {0} in {1}", null, sp.Name, m_scene.RegionInfo.RegionName);
            outputAction(BAKED_TEXTURES_REPORT_FORMAT, null, "Bake Type", "UUID");

            Dictionary<BakeType, Primitive.TextureEntryFace> bakedTextures = GetBakedTextureFaces(sp.UUID);

            foreach (BakeType bt in bakedTextures.Keys)
            {
                string rawTextureID;

                if (bakedTextures[bt] == null)
                {
                    rawTextureID = "not set";
                }
                else
                {
                    if (bakedTextures[bt].TextureID == AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                    {
                        rawTextureID = "not set";
                    }
                    else
                    {
                        rawTextureID = bakedTextures[bt].TextureID.ToString();

                        if (m_scene.AssetService.Get(rawTextureID) == null)
                        {
                            rawTextureID += " (not found)";
                        }
                        else
                        {
                            rawTextureID += " (uploaded)";
                        }
                    }
                }

                outputAction(BAKED_TEXTURES_REPORT_FORMAT, null, bt, rawTextureID);
            }

            bool bakedTextureValid = m_scene.AvatarFactory.ValidateBakedTextureCache(sp);
            outputAction("{0} baked appearance texture is {1}", null, sp.Name, bakedTextureValid ? "OK" : "incomplete");
        }

        public void SetPreferencesHoverZ(UUID agentId, float val)
        {
            ScenePresence sp = m_scene.GetScenePresence(agentId);

            if (sp == null || sp.IsDeleted || sp.IsNPC || sp.IsInTransit)
            {
                return;
            }

            float last = sp.Appearance.AvatarPreferencesHoverZ;

            if (val != last)
            {
                sp.Appearance.AvatarPreferencesHoverZ = val;
                QueueAppearanceSend(agentId);
            }
        }
    }
}
