﻿using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Microsoft.Scripting;
using RevitPythonShell.Helpers;
using RevitPythonShell.Views;
using RpsRuntime;

namespace RevitPythonShell.RevitCommands
{
    /// <summary>
    /// Start an interactive shell in a modal window.
    /// </summary>
    [Regeneration(RegenerationOption.Manual)]
    [Transaction(TransactionMode.Manual)]
    public class IronPythonConsoleCommand : IExternalCommand
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

                // run the initscript
                var initScript = App.GetInitScript();
                if (initScript != null)
                {
                    var scriptSource = host.Engine.CreateScriptSourceFromString(initScript, SourceCodeKind.Statements);
                    scriptSource.Execute(host.Console.ScriptScope);
                }                
            });

            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            gui.ConsoleControl.WithConsoleHost((host) =>
            {                
                host.Console.SetCommandDispatcher((command) =>
                {
                    if (command == null) return;
                    // Slightly involved form to enable keyboard interrupt to work.
                    var executing = true;
                    var operation = dispatcher?.BeginInvoke(DispatcherPriority.Normal, command);
                    while (executing)
                    {
                        if (operation!.Status != DispatcherOperationStatus.Completed)
                            operation.Wait(TimeSpan.FromSeconds(1));
                        if (operation.Status == DispatcherOperationStatus.Completed)
                            executing = false;
                    }
                });
                host.Editor.SetCompletionDispatcher((command) =>
                {
                    var executing = true;
                    var operation = dispatcher?.BeginInvoke(DispatcherPriority.Normal, command);
                    while (executing)
                    {
                        if (operation!.Status != DispatcherOperationStatus.Completed)
                            operation.Wait(TimeSpan.FromSeconds(1));
                        if (operation.Status == DispatcherOperationStatus.Completed)
                            executing = false;
                    }
                });
            });
            gui.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            gui.SetRevitAsWindowOwner();
            gui.ShowDialog();
            return Result.Succeeded;
        }
    }

    public class IronPythonConsoleCommandAvail : IExternalCommandAvailability {
        public bool IsCommandAvailable(UIApplication uiApp, CategorySet selectedCategories) {
            return true;
        }
    }
}
