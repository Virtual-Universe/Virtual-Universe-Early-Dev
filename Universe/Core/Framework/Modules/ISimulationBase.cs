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
using Nini.Config;
using Universe.Core.Framework.Configuration;
using Universe.Core.Framework.Servers.HttpServer.Interfaces;
using Universe.Core.Framework.Services.ClassHelpers.Other;

namespace Universe.Core.Framework.Modules
{
	/// <summary>
	/// Simulation Framework Interface
	/// 
	///		This is the framework that handles
	///		the backend pieces of the simulation
	/// </summary>
	public interface ISimulationBase
	{
		/// <summary>
		/// Get the configuration settings
		/// </summary>
		IConfigSource configSource { get; set; }

		/// <summary>
		/// Now we get the base instance
		/// of the application (Module) registry
		/// </summary>
		IRegistryCore ApplicationRegistry { get; }

		/// <summary>
		/// We also need to get the time and
		/// date this instance was started
		/// </summary>
		DateTime StartupTime { get; }

		/// <summary>
		/// Now we hook up the event manager
		/// for the simulation base
		/// </summary>
		EventManager EventManager { get; }

		/// <summary>
		/// Version String
		/// 
		///		Now we get the version string
		///		for Virtual Universe
		/// </summary>
		string Version { get; }
	}
}