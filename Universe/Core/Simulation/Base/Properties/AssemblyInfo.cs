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

using System.Reflection;
using System.Runtime.InteropServices;
using System.Resources;

/// <summary>
/// General Information
/// 
///     THe general information about an
///     assembly is controlled through the
///     following set of attributes.  We 
///     change these attribute values to
///     modify the information associated
///     with an assembly.
/// </summary>
[assembly: AssemblyTitle("Universe.Core.Simulation.Base")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Virtual World Research Inc.")]
[assembly: AssemblyProduct("Universe.Core.Simulation.Base")]
[assembly: AssemblyCopyright("Copyright (c) 2020-2028")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

/// <summary>
/// ComVisible Settings
/// 
///     Setting ComVisible to false makes
///     the types in this assembly not visible
///     to COM components.  If we want to
///     change access a type a type in this
///     assembly from COM, then we set the
///     ComVisible attribute to true on that
///     type.
/// </summary>
[assembly: ComVisible(false)]

/// <summary>
/// GUID Information
/// 
///     The following GUID is for the ID of
///     the typelib if this project is exposed
///     to COM.
/// </summary>
[assembly: Guid("9c59d6a6-8d7b-44f1-9cab-9440540c7d9d")]

/// <summary>
/// Version Information
/// 
///     These are the version information settings
///     for an assembly.  Version information 
///     consists of the following four values:
///     
///         Major Version
///         Minor Version
///         Build Number
///         Revision
///         
///     We can specify all four values or we can
///     default the Build and Revision numbers by
///     using the "*" as shown below:
///     
///         [assembly: AssemblyVersion("2.0.1.*")]
///         
///     However you should be careful here.  This
///     is generally overriden by the verison number
///     settings in:
///        
///         Universe.Core.Framework.Utilities.VersionInfo.tt
/// </summary>
[assembly: AssemblyVersion("2.0.1.*")]
[assembly: NeutralResourcesLanguageAttribute("en")]
