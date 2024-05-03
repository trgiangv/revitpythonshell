﻿using System.Diagnostics;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using RevitPythonShell.Helpers;
using RevitPythonShell.Views;
using RpsRuntime;

namespace RevitPythonShell.RevitCommands
{
    /// <summary>
    /// An object of this class is instantiated every time the user clicks on the
    /// button for opening the shell.
    /// </summary>
    /// 
    [Regeneration(RegenerationOption.Manual)]
    [Transaction(TransactionMode.Manual)]
    public class NonModalConsoleCommand : IExternalCommand
    {
        /// <summary>
        /// Open a window to let the user enter python code.
        /// </summary>
        /// <returns></returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var messageCopy = message;
            var gui = new IronPythonConsole();
            gui.ConsoleControl.WithConsoleHost((host) =>
            {
                // now that the console is created and initialized, the script scope should
                // be accessible...
                new ScriptExecutor(App.GetConfig(), commandData, messageCopy, elements)
                    .SetupEnvironment(host.Engine, host.Console.ScriptScope);

                host.Console.ScriptScope.SetVariable("__window__", gui);

                // run the init script
                var initScript = App.GetInitScript();
                if (initScript == null) return;
                var scriptSource = host.Engine.CreateScriptSourceFromString(initScript, SourceCodeKind.Statements);
                scriptSource.Execute(host.Console.ScriptScope);
            });
            var commandCompletedEvent = new AutoResetEvent(false);
            var externalEventHandler = new IronPythonExternalEventDispatcher(gui, commandCompletedEvent);
            var externalEvent = ExternalEvent.Create(externalEventHandler);
            gui.ConsoleControl.WithConsoleHost((host) =>
            {
                host.Console.GetCommandDispatcher();
                host.Console.SetCommandDispatcher((command) =>
                {
                    //externalEventHandler.Enqueue(() => oldDispatcher(command));                    
                    externalEventHandler.Enqueue(command);
                    externalEvent.Raise();
                    commandCompletedEvent.WaitOne();
                });

                host.Editor.SetCompletionDispatcher((command) =>
                {
                    externalEventHandler.Enqueue(command);
                    externalEvent.Raise();
                    commandCompletedEvent.WaitOne();                    
                });
            });
            gui.Title = gui.Title.Replace("RevitPythonShell", "RevitPythonShell (non-modal)");
            gui.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            gui.SetRevitAsWindowOwner();
            gui.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Make sure commands are executed in a RevitAPI context for non-modal RPS interactive shells.
    /// </summary>
    public class IronPythonExternalEventDispatcher(IronPythonConsole gui, AutoResetEvent commandCompletedEvent)
        : IExternalEventHandler
    {
        private readonly Queue<Action> _commands = new();

        public void Enqueue(Action command)
        {
            _commands.Enqueue(command);
        }

        public void Execute(UIApplication app)
        {
            while (_commands.Count > 0)
            {
                var command = _commands.Dequeue();
                try
                {
                    command();
                }
                catch (Exception ex)
                {
                    try
                    {
                        gui.ConsoleControl.WithConsoleHost((host) =>
                        {
                            ExceptionOperations eo;
                            eo = host.Engine.GetService<ExceptionOperations>();
                            var error = eo.FormatException(ex);
                            host.Console.WriteLine(error, Microsoft.Scripting.Hosting.Shell.Style.Error);
                            //TaskDialog.Show("Error", error);
                        });
                    }
                    catch (Exception exception)
                    {                       
                        Debugger.Launch();
                        Trace.WriteLine(exception.ToString());
                    }
                }
                finally
                {
                    commandCompletedEvent.Set();
                }
            }
        }

        public string GetName()
        {
            return "IronPythonExternalEventDispatcher";
        }
    }
}
