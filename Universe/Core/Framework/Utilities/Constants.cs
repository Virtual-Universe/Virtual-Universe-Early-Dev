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
using OpenMetaverse;

namespace Universe.Core.Framework.Utilities
{
	public class Constants
	{
		/// <summary>
		/// Default Directories
		/// 
		///		These are the default directories
		///		where Virtual Universe stores
		///		certian things such as configuration
		///		files, logs, user archives, region
		///		archives, etc.  These directories
		///		are defined here and can be overridden
		///		in the ini files.
		/// </summary>
		// Default asset cache directory.  I am not sure if we will
		// or will not need this yet so adding it for the time being
		// - NoahStarfinder - February 18, 2020
		public const string DEFAULT_ASSETCACHE_DIR = "../Data/AssetCache";

		// Default Configuration Directory
		public const string DEFAULT_CONFIG_DIR = "../Configuration";

		// Default Crash Logs storage directory
		public const string DEFAULT_CRASH_DIR = "../Data/Logs/Crashes";

		// Default Logs directory (Logs for grid and region servers
		// are stored in this directory)
		public const string DEFAULT_LOGS_DIR = "../Data/Logs";

		// Default directory to store MapTile assets in.
		public const string DEFAULT_MAPTILE_DIR = "../Data/MapTiles";

		// Default Region Archive storage directory (OARS)
		public const string DEFAULT_REGION_ARCHIVES_DIR = "../Data/RegionArchives";

		// Default directory to store the script dlls.  I am not sure if we will
		// or will not need this yet so adding it for the time being
		// - NoahStarfinder - February 18, 2020
		public const string DEFAULT_SCRIPTENGINE_DIR = "../Data/ScriptEngines";

		// Default User Archive storage directory (IARS)
		public const string DEFAULT_USER_ARCHIVES_DIR = "../Data/UserArchives";

		/// <summary>
		/// Default Textures
		/// 
		///		These are the default textures
		///		either required by the viewer
		///		or specific to our architecture
		///		in order to make sure things don't
		///		start looking awful if a texture
		///		goes missing.
		/// </summary>
		// Default texture, this appears to be required by the viewer
		public const string DefaultTexture = "89556747-24cb-43ed-920b-47caed15465f";

		// When all else fails and textures cannot be located for whatever
		// the reason might be, we will use this texture to temporarily fill
		// the need.
		public const string MISSING_TEXTURE_ID = "41fcdbb9-0896-495d-8889-1eb6fad88da3";


	}
}