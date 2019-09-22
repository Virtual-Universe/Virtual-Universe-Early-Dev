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
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using log4net;

namespace Universe.Framework.Console
{
    public class ConsoleBase : IConsole
    {
        protected string prompt = "# ";

        public IScene ConsoleScene { get; set; }

        public string DefaultPrompt { get; set; }

        public ConsoleBase(string defaultPrompt)
        {
            DefaultPrompt = defaultPrompt;
        }

        public virtual void LockOutput()
        {
        }

        public virtual void UnlockOutput()
        {
        }

        public void Output(string format, params object[] components)
        {
            Output(format, null, components);
        }

        public virtual void Output(string format, string level, params object[] components)
        {
            System.Console.WriteLine(format, components);
        }

        public string Prompt(string p)
        {
            return Prompt(p, null, null, true);
        }

        public string Prompt(string p, string def)
        {
            return Prompt(p, def, null, true);
        }

        public string Prompt(string p, List<char> excludedCharacters)
        {
            return Prompt(p, null, excludedCharacters, true);
        }

        public virtual string Prompt(string p, string def, List<char> excludedCharacters, bool echo = true)
        {
            bool itisdone = false;
            string ret = String.Empty;

            while (!itisdone)
            {
                itisdone = true;

                if (def != null)
                {
                    ret = ReadLine(String.Format("{0}: ", p), false, echo);
                }
                else
                {
                    ret = ReadLine(String.Format("{0} [{1}]: ", p, def), false, echo);
                }

                if (ret == String.Empty && def != null)
                {
                    ret = def;
                }
                else
                {
                    if (excludedCharacters != null)
                    {
                        foreach (char c in excludedCharacters)
                        {
                            if (ret.Contains(c.ToString()))
                            {
                                System.Console.WriteLine("The character \"" + c.ToString() + "\" is not permitted.");
                                itisdone = false;
                            }
                        }
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Displays a command prompt and returns
        /// a default value, user may only enter 1 or 
        /// 2 options
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="defaultresponse"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public virtual string Prompt(string prompt, string defaultresponse, List<string> options)
        {
            bool itisdone = false;
            string optstr = String.Empty;

            foreach (string s in options)
            {
                optstr += " " + s;
            }

            string temp = Prompt(prompt, defaultresponse);

            while (itisdone == false)
            {
                if (options.Contains(temp))
                {
                    itisdone = true;
                }
                else
                {
                    System.Console.WriteLine("Valid options are" + optstr);
                    temp = Prompt(prompt, defaultresponse);
                }
            }

            return temp;
        }

        public virtual string ReadLine(string p, bool isCommand, bool e)
        {
            System.Console.Write("{0}", p);
            string cmdinput = System.Console.ReadLine();
            return cmdinput;
        }
    }
}
