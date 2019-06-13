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

using System;
using System.IO;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using Microsoft.CSharp;
using NUnit.Framework;
using OpenSim.Region.ScriptEngine.Shared.CodeTools;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Tests.Common;

namespace OpenSim.Region.ScriptEngine.Shared.CodeTools.Tests
{
    /// <summary>
    /// Tests the LSL compiler. Among other things, test that error messages
    /// generated by the C# compiler can be mapped to prper lines/columns in
    /// the LSL source.
    /// </summary>
    [TestFixture]
    public class CompilerTest : OpenSimTestCase
    {
        private string m_testDir;
        private CSharpCodeProvider m_CSCodeProvider;
        private CompilerParameters m_compilerParameters;
        // private CompilerResults m_compilerResults;
        private ResolveEventHandler m_resolveEventHandler;

        /// <summary>
        /// Creates a temporary directory where build artifacts are stored.
        /// </summary>
        [TestFixtureSetUp]
        public void Init()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "opensim_compilerTest_" + Path.GetRandomFileName());

            if (!Directory.Exists(m_testDir))
            {
                // Create the temporary directory for housing build artifacts.
                Directory.CreateDirectory(m_testDir);
            }
        }

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            // Create a CSCodeProvider and CompilerParameters.
            m_CSCodeProvider = new CSharpCodeProvider();
            m_compilerParameters = new CompilerParameters();

            string rootPath = System.AppDomain.CurrentDomain.BaseDirectory;

            m_resolveEventHandler = new ResolveEventHandler(AssemblyResolver.OnAssemblyResolve);

            System.AppDomain.CurrentDomain.AssemblyResolve += m_resolveEventHandler;
                
            m_compilerParameters.ReferencedAssemblies.Add(Path.Combine(rootPath, "OpenSim.Region.ScriptEngine.Shared.dll"));
            m_compilerParameters.ReferencedAssemblies.Add(Path.Combine(rootPath, "OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll"));
            m_compilerParameters.ReferencedAssemblies.Add(Path.Combine(rootPath, "OpenMetaverseTypes.dll"));
            m_compilerParameters.GenerateExecutable = false;
        }

        /// <summary>
        /// Removes the temporary build directory and any build artifacts
        /// inside it.
        /// </summary>
        [TearDown]
        public void CleanUp()
        {
            System.AppDomain.CurrentDomain.AssemblyResolve -= m_resolveEventHandler;

            if (Directory.Exists(m_testDir))
            {
                // Blow away the temporary directory with artifacts.
                Directory.Delete(m_testDir, true);
            }
        }

        private CompilerResults CompileScript(
            string input, out Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>> positionMap)
        {
            m_compilerParameters.OutputAssembly = Path.Combine(m_testDir, Path.GetRandomFileName() + ".dll");

            CSCodeGenerator cg = new CSCodeGenerator();
            string output = cg.Convert(input);

            output = Compiler.CreateCSCompilerScript(output, "script1", typeof(ScriptBaseClass).FullName, null);         
            //            System.Console.WriteLine(output);

            positionMap = cg.PositionMap;

            CompilerResults compilerResults = m_CSCodeProvider.CompileAssemblyFromSource(m_compilerParameters, output);

            //            foreach (KeyValuePair<int, int> key in positionMap.Keys)
            //            {
            //                KeyValuePair<int, int> val = positionMap[key];
            //
            //                System.Console.WriteLine("{0},{1} => {2},{3}", key.Key, key.Value, val.Key, val.Value);
            //            }
            //
            //            foreach (CompilerError compErr in m_compilerResults.Errors)
            //            {
            //                System.Console.WriteLine("Error: {0},{1} => {2}", compErr.Line, compErr.Column, compErr);
            //            }

            return compilerResults;
        }

        /// <summary>
        /// Test that line number errors are resolved as expected when preceding code contains a jump.
        /// </summary>
        [Test]
        public void TestJumpAndSyntaxError()
        {
            TestHelpers.InMethod();

            Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>> positionMap;

            CompilerResults compilerResults = CompileScript(
@"default
{
    state_entry()
    {
        jump l;
        @l;
        i = 1;
    }
}", out positionMap);           

            Assert.AreEqual(
                new KeyValuePair<int, int>(7, 9),
                positionMap[new KeyValuePair<int, int>(compilerResults.Errors[0].Line, compilerResults.Errors[0].Column)]);
        }

        /// <summary>
        /// Test the C# compiler error message can be mapped to the correct
        /// line/column in the LSL source when an undeclared variable is used.
        /// </summary>
        [Test]
        public void TestUseUndeclaredVariable()
        {
            TestHelpers.InMethod();

            Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>> positionMap;

            CompilerResults compilerResults = CompileScript(
@"default
{
    state_entry()
    {
        integer y = x + 3;
    }
}", out positionMap);

            Assert.AreEqual(
                new KeyValuePair<int, int>(5, 21),
                positionMap[new KeyValuePair<int, int>(compilerResults.Errors[0].Line, compilerResults.Errors[0].Column)]);
        }

        /// <summary>
        /// Test that a string can be cast to string and another string
        /// concatenated.
        /// </summary>
        [Test]
        public void TestCastAndConcatString()
        {
            TestHelpers.InMethod();

            Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>> positionMap;

            CompilerResults compilerResults = CompileScript(
@"string s = "" a string"";

default
{
    state_entry()
    {
        key gAvatarKey = llDetectedKey(0);
        string tmp = (string) gAvatarKey + s;
        llSay(0, tmp);
    }
}", out positionMap);

            Assert.AreEqual(0, compilerResults.Errors.Count);
        }
    }
}