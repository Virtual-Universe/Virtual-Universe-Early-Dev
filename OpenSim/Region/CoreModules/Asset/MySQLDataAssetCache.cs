/* 7 March 2019 ( Nani added this :D )
 * 
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyrightlol
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
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenSim.Data.MySQL;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Monitoring;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

//[assembly: Addin("MySQLDataAssetCache", "1.1")]
//[assembly: AddinDependency("OpenSim", "0.8.1")]

namespace OpenSim.Region.CoreModules.Asset
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "MySQLDataAssetCache")]
    public class MySQLDataCache : ISharedRegionModule, IAssetCache, IAssetService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;

        private const string m_ModuleName = "MySQLDataAssetCache";

        private static ulong m_Requests = 0;
        private static ulong m_DatabaseHits = 0;
        private static ulong m_weakRefHits = 0;

        private static HashSet<string> m_inDBQueue = new HashSet<string>();
        private static NConcurrentQueue<string> m_dbQueue = new NConcurrentQueue<string>();
 
        private static volatile bool m_Running = false;

        // half an hour
        private static int m_timerStartTime  = 1800000;
        // an hour
        private static int m_timerRepeatTime = 3600000;
        private static double m_cacheTimeout = 0.0;
        private static int m_timerFlag = 0;

        private MySQLAssetCacheData m_database = null;
        private string m_connectionString = string.Empty;

        private IAssetService m_AssetService;
        private static List<Scene> m_Scenes = new List<Scene>();
        private static int m_TouchFlag = 0;

        private static readonly Dictionary<string,WeakReference> weakAssetReferences = new Dictionary<string, WeakReference>();
        private static object m_WeakLock = new object();

        private static System.Threading.Timer m_expireTimer = null;

        public MySQLDataCache()
        {

        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return m_ModuleName; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];

            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("AssetCaching", string.Empty);

                if (name == Name)
                {
                    m_Enabled = false;

                    m_log.InfoFormat("[DATABASE ASSET CACHE]: {0} enabled", this.Name);

                    IConfig assetConfig = source.Configs["AssetCache"];
                    if (assetConfig == null)
                    {
                        m_log.Debug(
						   "[DATABASE ASSET CACHE]: AssetCache section missing from config (not copied config-include/MySQLDataAssetCache.ini.example?  Using defaults.");
                    }
                    else
                    {                        
                        m_Enabled = assetConfig.GetBoolean("CacheEnabled", m_Enabled);
                        if (m_Enabled)
                        {
                            m_connectionString = assetConfig.GetString("ConnectionString", m_connectionString);

                            if (m_connectionString == string.Empty)
                            {
                                m_Enabled = false;
                            }
                            else try
                            {
                                m_database = new MySQLAssetCacheData();
                                m_database.Initialise(m_connectionString);

                                m_cacheTimeout = assetConfig.GetDouble("CacheTimeout", m_cacheTimeout);
                            }
                            catch
                            {
                                m_Enabled = false;
                            }
                        }
                    }

                    MainConsole.Instance.Commands.AddCommand("Assets", true, "dbcache status", "dbcache status", "Display cache status", HandleConsoleCommand);
                    MainConsole.Instance.Commands.AddCommand("Assets", true, "dbcache assets", "dbcache assets", "Attempt a deep scan and cache of all assets in all scenes", HandleConsoleCommand);
                    MainConsole.Instance.Commands.AddCommand("Assets", true, "dbcache expire", "dbcache expire <hours>", "Purge cached assets older then the specified hours", HandleConsoleCommand);
                }
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            try
            {
                m_dbQueue.Destroy();
                weakAssetReferences.Clear();
            }
            catch { }
        }

        // Run only in m_dbQueueSync lock.
        private void StartHandlerThreads()
        {
            if (!m_Running)
            {
                m_Running = true;

                Util.FireAndForget(
                       delegate { HandleDatabaseCacheRequests(this); }, null,
                       "MySQLAssetCache.HandleDatabaseCacheRequests_1", false);

                Util.FireAndForget(
                       delegate { HandleDatabaseCacheRequests(this); }, null,
                       "MySQLAssetCache.HandleDatabaseCacheRequests_2", false);


                if (m_cacheTimeout > 0.0)
                    m_expireTimer = new System.Threading.Timer(
                                    delegate { HandleExpireTimer(this); },
                                               null, m_timerStartTime, Timeout.Infinite);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (m_Enabled)
            {
                scene.RegisterModuleInterface<IAssetCache>(this);
                m_Scenes.Add(scene);
            }
        }

        public void RemoveRegion(Scene scene)
        {
            if (m_Enabled)
            {
                scene.UnregisterModuleInterface<IAssetCache>(this);
                m_Scenes.Remove(scene);

                if (m_Scenes.Count <= 0)
                {
                    m_Running = false;

                    m_dbQueue.CancelWait();
                    Thread.Sleep(50);

                    lock (m_inDBQueue)
                    {
                        m_inDBQueue.Clear();
                    }

                    try
                    {
                        m_expireTimer.Dispose();
                        m_expireTimer = null;
                    }
                    catch { }

                    Thread.Sleep(50);

                    // Clean Up the queue.
                    m_dbQueue.Clear();
                }
            }
        }

        public void RegionLoaded(Scene scene)
        {
            if (m_Enabled)
            {
                if (m_AssetService == null)
                    m_AssetService = scene.RequestModuleInterface<IAssetService>();

                StartHandlerThreads();

                RunTouchAllAssetsScan(true);
            }
        }

        ////////////////////////////////////////////////////////////
        // IAssetCache
        //

        public void Cache(AssetBase asset)
        {
            if (asset == null)
                return;

            try
            {
                lock (m_WeakLock)
                      weakAssetReferences[asset.ID] = new WeakReference(asset); 

                lock (m_inDBQueue)
                {
                    if (!m_inDBQueue.Add(asset.ID))
                    {
                        // was already in the queue,
                        // some other thread must have snuck it in.
                        // Which is impressive but not impossible.
                        return;
                    }
                }

                m_dbQueue.Enqueue(asset.ID);

                StartHandlerThreads();
            }
            catch { }
        }

        public void CacheNegative(string id)
        {
            // We do not use a negative cache.
        }

        private AssetBase GetFromWeakReference(string id)
        {
            AssetBase asset = null;
            WeakReference aref;

            lock (m_WeakLock)
            {
                if (weakAssetReferences.TryGetValue(id, out aref))
                {
                    try
                    {
                        if (aref.IsAlive)
                        {
                            asset = aref.Target as AssetBase;
                            if (asset == null)
                                weakAssetReferences.Remove(id);
                            else
                                m_weakRefHits++;
                        }
                        else
                        {
                            weakAssetReferences.Remove(id);
                        }
                    }
                    catch
                    {
                        asset = null;
                    }
                }
            }
            return asset;
        }

        private AssetBase GetFromDatabaseCache(string id)
        {
            AssetBase asset = m_database.GetAsset(id);
            if (asset != null)			
                m_DatabaseHits++;
            
            return asset;
        }

        private bool CheckFromDatabaseCache(string id)
        {
            return m_database.AssetExists(id);
        }

        // Used by HandleDatabaseCacheRequests
        public bool WriteAssetDataToDatabase(AssetBase asset)
        {
            try
            { 
                if (m_database.AssetExists(asset.ID))
                    return true;    

                return m_database.StoreAsset(asset);    
            }
            finally
            {
                // Even if the write fails with an exception, we need to make sure
                // that we release the lock on that asset, otherwise it'll never get
                // cached
                lock (m_inDBQueue)
                {
                    // we use key instead of filename now because key is shorter
                    // and just as unique.
                    m_inDBQueue.Remove(asset.ID);
                }
            }
        }

        // Used by HandleExpireTimer
        public void RemoveExpiredAssets()
        {
            try
            {
                m_database.Expire(m_cacheTimeout);
            }
            catch { }
        }

        // For IAssetService
        public AssetBase Get(string id)
        {
            AssetBase asset;
            Get(id, out asset);
            return asset;
        }

        public bool Get(string id, out AssetBase asset)
        {
            m_Requests++;

            asset = GetFromWeakReference(id);
            if (asset == null)
            {
                asset = GetFromDatabaseCache(id);
			}
			return true; // Asset can still be null, but we always return true.
        }

        public bool Check(string id)
        {
            return CheckFromDatabaseCache(id);
        }

        public AssetBase GetCached(string id)
        {
            AssetBase asset;
            Get(id, out asset);
            return asset;
        }

        public void Expire(string id)
        {
            try
            {
                m_database.Delete(id);
                lock (m_WeakLock)
                      weakAssetReferences.Remove(id);
            }
            catch { }
        }

        public void Clear()
        {
            // We do not clear the database since it can be shared by multiple simulators.
            lock (m_WeakLock)
                  weakAssetReferences.Clear();
        }

        /// <summary>
        /// Iterates through all Scenes, doing a deep scan through assets
        /// to update the access time of all assets present in the scene or referenced by assets
        /// in the scene.
        /// </summary>
        /// <returns>Number of distinct asset references found in the scene.</returns>
        private int TouchAllSceneAssets(bool RestartScripts)
        {
            UuidGatherer gatherer = new UuidGatherer(m_AssetService);

            HashSet<UUID> assetsFound = new HashSet<UUID>();

            foreach (Scene s in m_Scenes)
            {
                if (RestartScripts)
                    s.HardRestartScripts();

                s.ForEachSOG(delegate(SceneObjectGroup e)
                {
                    gatherer.AddForInspection(e);
                    gatherer.GatherAll();

                    foreach (UUID assetID in gatherer.GatheredUuids.Keys)
                    {
                        if (!assetsFound.Contains(assetID))
                        {
                            string ID = assetID.ToString();

                            if (m_database.AssetExists(ID))
                            { 
                                m_database.UpdateAccessTime(ID);
                                assetsFound.Add(assetID);
                            }
                            else
                            {
								// And HERE we fall back on the asset service (so basicly the grid)
								// to te the asset. This will also cache the newly gotten asset.
								try
								{
									AssetBase cachedAsset = m_AssetService.Get(ID);
									if (cachedAsset == null && gatherer.GatheredUuids[assetID] != (sbyte)AssetType.Unknown)
										continue;

									assetsFound.Add(assetID);
								} catch { }
                            }
                        }
                    }
                    gatherer.GatheredUuids.Clear();
                });
            }
            return assetsFound.Count;
        }

        /// <summary>
        /// Deletes all cache contents
        /// </summary>

        private List<string> GenerateCacheHitReport()
        {
            List<string> outputLines = new List<string>();

            // Check if flag is set (1). 
            if (1 == Interlocked.CompareExchange(ref m_TouchFlag, 0, 0))
            {
                outputLines.Add("Beware, assets are being cached at this moment.");
            }

            double invReq = (m_Requests > 0) ? 100.0 / m_Requests : 1;

            double weakHitRate = m_weakRefHits * invReq;
            int weakEntries = weakAssetReferences.Count;

            double TotalHitRate = weakHitRate;

            outputLines.Add(
                string.Format("Total requests: {0}", m_Requests));

            outputLines.Add(
                string.Format("unCollected Hit Rate: {0}% ({1} entries)", weakHitRate.ToString("0.00"),weakEntries));

            double HitRate = m_DatabaseHits * invReq;
            outputLines.Add(
                string.Format("Database Hit Rate: {0}%", HitRate.ToString("0.00")));

            TotalHitRate += HitRate;

            outputLines.Add(
                string.Format("Total Hit Rate: {0}%", TotalHitRate.ToString("0.00")));

            return outputLines;
        }

        #region Console Commands
 
        private void RunTouchAllAssetsScan(bool wait)
        {
            ICommandConsole con = MainConsole.Instance;

            // Set the flag 
            if (0 == Interlocked.CompareExchange(ref m_TouchFlag, 1, 0))
            {
                // We could set the lock so it was not already set.
                try
                {
                    con.Output("Ensuring assets are cached for all scenes.");
                    WorkManager.RunInThreadPool(delegate
                    {
                        int assetReferenceTotal = 0;
                        for (int i = 0; i < 3; i++)
                        {
                            // we only wait long at region load start to make sure all
                            // (or most) assets were loaded.
                            if (wait)
                            {
                                Thread.Sleep(30000);
                            }
                            else
                            {
                                // just to allow the calling thread to release the flag.
                                Thread.Sleep(1000);
                            }

                            // Set the flag
                            if (0 == Interlocked.CompareExchange(ref m_TouchFlag, 1, 0))
                            {
                                try
                                {
                                    assetReferenceTotal = TouchAllSceneAssets(wait);
                                    if (assetReferenceTotal > 0 || !wait)
                                    {
                                        break;
                                    }
                                }
                                finally
                                {
                                    // Release the flag.
                                    Interlocked.Exchange( ref m_TouchFlag, 0 );
                                }
                            }
                        }
                        GC.Collect();
                        
                        con.OutputFormat("Completed check with {0} assets.", assetReferenceTotal);
                    }, null, "TouchAllSceneAssets", false);
                }
                finally
                {
                    // Release the flag
                    Interlocked.Exchange(ref m_TouchFlag, 0);
                }
            }
            else
            { 
                // we could not set the lock so it was already set.
                con.OutputFormat("Database assets cache check already running");
            }
        }

        private void HandleConsoleCommand(string module, string[] cmdparams)
        {
            ICommandConsole con = MainConsole.Instance;

            if (cmdparams.Length >= 2)
            {
                string cmd = cmdparams[1];

                switch (cmd)
                {
                    case "status":
                        GenerateCacheHitReport().ForEach(l => con.Output(l));
                        break;

                    case "assets":
                        RunTouchAllAssetsScan(false);
                        break;

                    case "expire":
                        try { }
                        finally // a finally block can not to interrupted.
                        {
                            // Check if flag is set (1). 
                            if (1 == Interlocked.CompareExchange(ref m_TouchFlag, 0, 0))
                            {
                                con.OutputFormat("Please wait until all assets are cached.", cmd);
                            }
                        }    
                        if (cmdparams.Length < 3)
                        {
                            con.OutputFormat("Invalid parameters for Expire, please specify a valid date & time", cmd);
                            break;
                        }

                        double hours = 0;
                        if (!Double.TryParse( cmdparams[2], out hours ))
                        { 
                            con.OutputFormat("{0} is not a valid number of hours", cmd);
                            break;
                        }

                        Util.FireAndForget(
                            delegate
                            {
                                try
                                {
                                    m_database.Expire(hours);
                                } catch { }
                            },null,"ExpireDatabaseCache",false);

                        break;
                    default:
                        con.OutputFormat("Unknown command {0}", cmd);
                        break;
                }
            }
            else if (cmdparams.Length == 1)
            {
                con.Output("dbcache assets - Attempt a deep cache of all assets in all scenes");
                con.Output("dbcache expire <hours> - Purge assets older then the specified number of hours.");
                con.Output("dbcache status - Display cache status");
            }
        }

        #endregion

        #region IAssetService Members

        public AssetMetadata GetMetadata(string id)
        {
            AssetBase asset;
            Get(id, out asset);
			if (asset == null)
				return null;

			return asset.Metadata;
        }

        public byte[] GetData(string id)
        {
            AssetBase asset;
            Get(id, out asset);
			if (asset == null)
				return new byte[0];

            return asset.Data;
        }

        public bool Get(string id, object sender, AssetRetrieved handler)
        {
			AssetBase asset;
			Get(id, out asset);
			handler(id, sender, asset); // asset can be null
			return true;
		}

		public bool[] AssetsExist(string[] ids)
        {
            bool[] exist = new bool[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                exist[i] = Check(ids[i]);
            }

            return exist;
        }

        public string Store(AssetBase asset)
        {
            if (asset.FullID == UUID.Zero)
            {
                asset.FullID = UUID.Random();
            }

            Cache(asset);

            return asset.ID;
        }

        public bool UpdateContent(string id, byte[] data)
        {
            AssetBase asset;
			Get(id, out asset);
			if (asset == null) // preventing an error.
                return false;
            asset.Data = data;
            Cache(asset);
            return true;
        }

        public bool Delete(string id)
        {
            Expire(id);
            return true;
        }

        #endregion

        private static void HandleDatabaseCacheRequests(MySQLDataCache CacheHandler)
        {
            while (m_Running)
            {
                try
                {
					string id;
                    if (m_dbQueue.TryDequeue( out id ) && m_Running)
                    {
                        AssetBase asset = CacheHandler.GetFromWeakReference(id);
                        if (asset != null)
                        {
                            CacheHandler.WriteAssetDataToDatabase(asset);
                        }
                    }
                }
                catch { }
            }
        }

        private static void HandleExpireTimer(MySQLDataCache CacheHandler)
        {
            try
            {
                if (m_Running) 
                {
                    // Set the flag.
                    if (0 == Interlocked.CompareExchange(ref m_timerFlag, 1, 0))
                    {
                        try
                        {
                            CacheHandler.RemoveExpiredAssets();
                        }
                        finally
                        {
                            // Release the flag.
                            Interlocked.Exchange(ref m_timerFlag, 0);

                            if (m_Running)
                                m_expireTimer.Change(m_timerRepeatTime, 0);
                        }
                    }
                }
            } catch { }
        }
    }
}
