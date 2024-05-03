// Copyright (c) 2010 Joe Moorhouse

using System.Text;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Hosting.Shell;

namespace PythonConsoleControl
{
    public delegate void ConsoleCreatedEventHandler(object sender, EventArgs e);
    

    /// <summary>
    /// Hosts the python console.
    /// </summary>
    public sealed class PythonConsoleHost(PythonTextEditor textEditor) : ConsoleHost, IDisposable
    {
        private Thread _thread;
        private PythonConsole _pythonConsole;       

        public event ConsoleCreatedEventHandler ConsoleCreated;

        public PythonConsole Console => _pythonConsole;

        public PythonTextEditor Editor => textEditor;

        protected override Type Provider => typeof(PythonContext);

        /// <summary>
        /// Runs the console host in its own thread.
        /// </summary>
        public void Run()
        {
            _thread = new Thread(RunConsole);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Dispose()
        {
            if (_pythonConsole != null)
            {
                _pythonConsole.Dispose();
            }

            if (_thread != null)
            {
                _thread.Join();
            }
        }


        protected override CommandLine CreateCommandLine()
        {
            return new PythonCommandLine();
        }

        protected override OptionsParser CreateOptionsParser()
        {
            return new PythonOptionsParser();
        }

        /// <remarks>
        /// After the engine is created, the standard output is replaced with our custom Stream class so we
        /// can redirect the stdout to the text editor window.
        /// This can be done in this method since the Runtime object will have been created before this method
        /// is called.
        /// </remarks>
        protected override IConsole CreateConsole(ScriptEngine engine, CommandLine commandLine, ConsoleOptions options)
        {
            SetOutput(new PythonOutputStream(textEditor));
            _pythonConsole = new PythonConsole(textEditor, commandLine);
            if (ConsoleCreated != null) ConsoleCreated(this, EventArgs.Empty);
            return _pythonConsole;
        }

        public void WhenConsoleCreated(Action<PythonConsoleHost> action)
        {            
            if (_pythonConsole != null)
            {
                _pythonConsole.WhenConsoleInitialized(() => action(this));
            }
            else
            {
                ConsoleCreated += (sender, args) => WhenConsoleCreated(action);
            }
        }

        private void SetOutput(PythonOutputStream stream)
        {
            Runtime.IO.SetOutput(stream, Encoding.UTF8);
        }

        /// <summary>
        /// Runs the console.
        /// </summary>
        private void RunConsole()
        {
            Run(["-X:FullFrames"]);
        }

        protected override ScriptRuntimeSetup CreateRuntimeSetup()
        {
            ScriptRuntimeSetup srs = ScriptRuntimeSetup.ReadConfiguration();
            foreach (var langSetup in srs.LanguageSetups)
            {
                if (langSetup.FileExtensions.Contains(".py"))
                {
                    langSetup.Options["SearchPaths"] = new string[0];
                }
            }
            return srs;
        }

        protected override void ParseHostOptions(string/*!*/[]/*!*/ args)
        {
            // Python doesn't want any of the DLR base options.
            foreach (string s in args)
            {
                Options.IgnoredArgs.Add(s);
            }
        }

        protected override void ExecuteInternal()
        {
            var pc = HostingHelpers.GetLanguageContext(Engine) as PythonContext;
            pc?.SetModuleState(typeof(ScriptEngine), Engine);
            base.ExecuteInternal();
        }
    }
}
