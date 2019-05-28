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
using System.Data;
using System.Reflection;
using log4net;
using Mysql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;

namespace OpenSim.Data.MySQL
{
    public class MySQLFSAssetData : IFSAssetDataPlugin
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected string m_ConnectionString;
        protected string m_Table;

        /// <summary>
        ///     Number of days that must pass before we 
        ///     update the access time on an asset when 
        ///     it has been fetched Config option to 
        ///     change this is:
        ///         "DaysBetweenAccessTimeUpdates"
        /// </summary>
        private int DaysBetweenAccessTimeUpdates = 0;

        protected virtual Assembly Assembly
        {
            get { return GetType().Assembly; }
        }

        public MySQLFSAssetData()
        {
        }

        #region IPlugin Members

        public string Version
        {
            get { return "1.0.0.0"; }
        }

        /// <summary>
        ///     Loads and initializes the MySQL storage 
        ///     plugin and checks for migrations
        /// </summary>
        /// <param name="connect"></param>
        /// <param name="realm"></param>
        /// <param name="UpdateAccessTime"></param>
        public void Initialize(string connect, string realm, int UpdateAccessTime)
        {
            m_ConnectionString = connect;
            m_Table = realm;

            DaysBetweenAccessTimeUpdates = UpdateAccessTime;

            try
            {
                using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
                {
                    conn.Open();
                    Migration m = new Migration(conn, Assembly, "FSAssetStore");
                    m.Update();
                    conn.Close();
                }
            }
            catch (MySqlException e)
            {
                m_log.ErrorFormat("[FS Assets]: Can't connect to database: {0}", e.Message.ToString());
            }
        }

        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public void Dispose() { }

        public string Name
        {
            get { return "MySQL FSAsset storage engine"; }
        }

        #endregion

        private bool ExecuteNonQuery(MySqlCommand cmd)
        {
            using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Database open failed with {0}", e.ToString());
                    return false;
                }

                cmd.Connection = conn;

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException e)
                {
                    cmd.Connection = null;
                    conn.Close();
                    m_log.ErrorFormat("[FS Assets]: Query {0} failed with {1}", cmd.CommandText, e.ToString());
                    return false;
                }

                conn.Close();
                cmd.Connection = null;
            }

            return true;
        }

        #region IFSAssetDataPlugin Members

        public AssetMetadata Get(string id, out string hash)
        {
            hash = String.Empty;

            AssetMetadata meta = new AssetMetadata();

            using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Database open failed with {0}", e.ToString());
                    return null;
                }

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = String.Format("select id, name, description, type, hash, create_time, asset_flags, access_time from {0} where id = ?id", m_Table);
                    cmd.Parameters.AddWithValue("?id", id);

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        hash = reader["hash"].ToString();

                        meta.ID = id;
                        meta.FullID = new UUID(id);

                        meta.Name = reader["name"].ToString();
                        meta.Description = reader["description"].ToString();
                        meta.Type = (sbyte)Convert.ToInt32(reader["type"]);
                        meta.ContentType = SLUtil.SLAssetTypeToContentType(meta.Type);
                        meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));
                        meta.Flags = (AssetFlags)Convert.ToInt32(reader["asset_flags"]);

                        int AccessTime = Convert.ToInt32(reader["access_time"]);
                        UpdateAccessTime(id, AccessTime);
                    }
                }

                conn.Close();
            }

            return meta;
        }

        private void UpdateAccessTime(string AssetID, int AccessTime)
        {
            /// <summary>
            ///     Reduce database work by only updating access
            ///     time if asset has not recently been accessed
            ///     0 by default. config option is: 
            ///         "DaysBetweenAccessTImeUpdates"
            /// </summary>
            if (DaysBetweenAccessTimeUpdates > 0 && (DateTime.UtcNow - Utils.UnixTimeToDateTime(AccessTime)).TotalDays < DaysBetweenAccessTimeUpdates)
            {
                return;
            }

            using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Database open failed with {0}", e.ToString());
                    return;
                }

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = String.Format("UPDATE {0} SET `access_time` = UNIX_TIMESTAMP() WHERE `id` = ?id", m_Table);
                    cmd.Parameters.AddWithValue("?id", AssetID);
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }
        }

        public bool Store(AssetMetadata meta, string hash)
        {
            try
            {
                string oldhash;
                AssetMetadata existingAsset = Get(meta.ID, out oldhash);

                using (MySqlCommand cmd = new MySqlCommand())
                {
                    cmd.Parameters.AddWithValue("?id", meta.ID);
                    cmd.Parameters.AddWithValue("?name", meta.Name.Substring(0, Math.Min(meta.Name.Length, 64)));
                    cmd.Parameters.AddWithValue("?description", meta.Description.Substring(0, Math.Min(meta.Name.Length, 128)))
                    cmd.Parameters.AddWithValue("?type", meta.Type);
                    cmd.Parameters.AddWithValue("?hash", hash);
                    cmd.Parameters.AddWithValue("?asset_flags", meta.Flags);

                    if (existingAsset == null)
                    {
                        cmd.CommandText = String.Format("insert into {0} (id, name, description, type, hash, asset_flags, create_time, access_time) values ( ?id, ?name, ?description, ?type, ?hash, ?asset_flags, UNIX_TIMESTAMP(), UNIX_TIMESTAMP())", m_Table);
                        ExecuteNonQuery(cmd);
                        return true;
                    }
                }

                /// <summary>
                ///     If the asset already exists
                ///     then we assume it was already correctly
                ///     stored or regions will keep retrying
                /// </summary>
                return true;
            }
            catch(Exception e)
            {
                m_log.Error("[FS Assets]: Failed to store asset with ID " + meta.ID);
                m_log.Error(e.ToString());
                return false;
            }
        }

        /// <summary>
        ///     Check if the assets exist in the database.
        /// </summary>
        /// <param name="uuids">The asset UUID's</param>
        /// <returns>For each asset: true if it exists, false otherwise</returns>
        public bool[] AssetsExist(UUID[] uuids)
        {
            if (uuids.Length == 0)
            {
                return new bool[0];
            }

            bool[] results = new bool[uuids.Length];

            for (int i = 0; i < uuids.Length; i++)
            {
                results[i] = false;
            }

            HashSet<UUID> exists = new HashSet<UUID>();

            string ids = "'" + string.Join("','", uuids) + "'";
            string sql = string.Format("select id from {1} where id in ({0})", ids, m_Table);

            using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Failed to open database: {0}", e.ToString());
                    return results;
                }

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;

                    using (MySqlDataReader dbReader = cmd.ExecuteReader())
                    {
                        while (dbReader.Read())
                        {
                            UUID id = DBGuid.FromDB(dbReader["ID"]);
                            exists.Add(id);
                        }
                    }
                }

                conn.Close();
            }

            for (int i = 0; i < uuids.Length; i++)
            {
                results[i] = exists.Contains(uuids[i]);
            }

            return results;
        }

        public int Count()
        {
            int count = 0;

            using (MySqlConnection conn = new MySqlConnection(m_ConnectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Failed to open database: {0}", e.ToString());
                    return 0;
                }

                using(MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = String.Format("select count(*) as count from {0}",m_Table);

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        count = Convert.ToInt32(reader["count"]);
                    }
                }

                conn.Close();
            }

            return count;
        }

        public bool Delete(string id)
        {
            using(MySqlCommand cmd = new MySqlCommand())
            {
                cmd.CommandText = String.Format("delete from {0} where id = ?id",m_Table);
                cmd.Parameters.AddWithValue("?id", id);
                ExecuteNonQuery(cmd);
            }

            return true;
        }

        public void Import(string conn, string table, int start, int count, bool force, FSStoreDelegate store)
        {
            int imported = 0;

            using (MySqlConnection importConn = new MySqlConnection(conn))
            {
                try
                {
                    importConn.Open();
                }
                catch (MySqlException e)
                {
                    m_log.ErrorFormat("[FS Assets]: Can't connect to database: {0}", e.Message.ToString());
                    return;
                }

                using (MySqlCommand cmd = importConn.CreateCommand())
                {
                    string limit = String.Empty;

                    if (count != -1)
                    {
                        limit = String.Format(" limit {0},{1}", start, count);
                    }

                    cmd.CommandText = String.Format("select * from {0}{1}", table, limit);

                    MainConsole.Instance.Output("Querying database");

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        MainConsole.Instance.Output("Reading data");

                        while (reader.Read())
                        {
                            if ((imported % 100) == 0)
                            {
                                MainConsole.Instance.Output(String.Format("{0} assets imported so far", imported));
                            }

                            AssetBase asset = new AssetBase();
                            AssetMetadata meta = new AssetMetadata();

                            meta.ID = reader["id"].ToString();
                            meta.FullID = new UUID(meta.ID);

                            meta.Name = reader["name"].ToString();
                            meta.Description = reader["description"].ToString();
                            meta.Type = (sbyte)Convert.ToInt32(reader["assetType"]);
                            meta.ContentType = SLUtil.SLAssetTypeToContentType(meta.Type);
                            meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));

                            asset.Metadata = meta;
                            asset.Data = (byte[])reader["data"];

                            store(asset, force);

                            imported++;
                        }
                    }
                }

                importConn.Close();
            }

            MainConsole.Instance.Output(String.Format("Import done, {0} assets imported", imported));
        }

        #endregion
    }
}
