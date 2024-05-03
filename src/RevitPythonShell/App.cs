using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RevitPythonShell.RevitCommands;
using RpsRuntime;

namespace RevitPythonShell
{
    [Regeneration(RegenerationOption.Manual)]
    [Transaction(TransactionMode.Manual)]
    internal class App : IExternalApplication
    {
        private const string AppName = "RevitPythonShell";
        private static string _versionNumber;
        private static string _dllFolder;

        /// <summary>
        /// Hook into Revit to allow starting a command.
        /// </summary>
        Result IExternalApplication.OnStartup(UIControlledApplication application)
        {

            try
            {
                _versionNumber = application.ControlledApplication.VersionNumber;

                _dllFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                var assemblyName = "CommandLoaderAssembly";
                var dllFullPath = Path.Combine(_dllFolder!, assemblyName + ".dll");

                var settings = GetSettings();

                CreateCommandLoaderAssembly(settings, _dllFolder, assemblyName);
                BuildRibbonPanel(application, dllFullPath);                

                ExecuteStartupScript(application);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                var td = new TaskDialog("Error setting up RevitPythonShell");
                td.MainInstruction = ex.Message;
                td.ExpandedContent = ex.ToString();
                td.Show();
                return Result.Failed;
            }
        }

        private static void ExecuteStartupScript(UIControlledApplication uiControlledApplication)
        {
            // we need a UIApplication object to assign as `__revit__` in python...
            var applicationVersionNumber = uiControlledApplication.ControlledApplication.VersionNumber;
            var fieldName = int.Parse(applicationVersionNumber) >= 2017 ? "m_uiapplication": "m_application";
            var fi = uiControlledApplication.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var uiApplication = (Autodesk.Revit.UI.UIApplication)fi?.GetValue(uiControlledApplication);  
            
            // execute StartupScript
            var startupScript = GetStartupScript();
            if (startupScript == null) return;
            var executor = new ScriptExecutor(GetConfig(), uiApplication, uiControlledApplication);
            var result = executor.ExecuteScript(startupScript, GetStartupScriptPath());
            if (result == (int)Result.Failed)
            {
                TaskDialog.Show("RevitPythonShell - StartupScript", executor.Message);
            }
        }

        private static void BuildRibbonPanel(UIControlledApplication application, string dllfullpath)
        {
            var assembly = typeof(App).Assembly;
            var smallImage = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Python-16.png");
            var largeImage = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Python-32.png");
            

            RibbonPanel ribbonPanel = application.CreateRibbonPanel(AppName);
            var splitButton = ribbonPanel.AddItem(new SplitButtonData("splitButtonRevitPythonShell", AppName)) as SplitButton;

            PushButtonData pbdOpenPythonShell = new PushButtonData(
                            AppName, 
                            "Interactive\nPython Shell", 
                            assembly.Location, 
                            typeof(IronPythonConsoleCommand).FullName)
            {
                Image = smallImage,
                LargeImage = largeImage,
                AvailabilityClassName = typeof(IronPythonConsoleCommandAvail).FullName
            };
            splitButton?.AddPushButton(pbdOpenPythonShell);

            PushButtonData pbdOpenNonModalShell = new PushButtonData(
                            "NonModalRevitPythonShell",
                            "Non-modal\nShell",
                            assembly.Location,
                           typeof(NonModalConsoleCommand).FullName)
            {
                Image = smallImage,
                LargeImage = largeImage,
                AvailabilityClassName = typeof(IronPythonConsoleCommandAvail).FullName
            };
            splitButton?.AddPushButton(pbdOpenNonModalShell);

            PushButtonData pbdConfigure = new PushButtonData(
                            "Configure", 
                            "Configure...", 
                            assembly.Location, 
                            typeof(ConfigureCommand).FullName)
            {
                Image = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Settings-16.png"),
                LargeImage = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Settings-32.png"),
                AvailabilityClassName = typeof(IronPythonConsoleCommandAvail).FullName
            };
            splitButton?.AddPushButton(pbdConfigure);

            PushButtonData pbdDeployRpsAddin = new PushButtonData(
                "DeployRpsAddin",
                "Deploy RpsAddin",
                assembly.Location,
               typeof(DeployRpsAddinCommand).FullName)
            {
                Image = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Deployment-16.png"),
                LargeImage = GetEmbeddedPng(assembly, "RevitPythonShell.Resources.Deployment-32.png"),
                AvailabilityClassName = typeof(IronPythonConsoleCommandAvail).FullName
            };
            splitButton?.AddPushButton(pbdDeployRpsAddin);

            var commands = GetCommands(GetSettings()).ToList();
            AddGroupedCommands(dllfullpath, ribbonPanel, commands.Where(c => !string.IsNullOrEmpty(c.Group)).GroupBy(c => c.Group));
            AddUngroupedCommands(dllfullpath, ribbonPanel, commands.Where(c => string.IsNullOrEmpty(c.Group)).ToList());
        }



        private static ImageSource GetEmbeddedBmp(System.Reflection.Assembly app, string imageName)
        {
            var file = app.GetManifestResourceStream(imageName);
            var source = BitmapDecoder.Create(file!, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            return source.Frames[0];
        }

        private static ImageSource GetEmbeddedPng(Assembly app, string imageName)
        {
            var file = app.GetManifestResourceStream(imageName);
            var source = BitmapDecoder.Create(file!, BitmapCreateOptions.None, BitmapCacheOption.None);
            return source.Frames[0];
        }

        private static void AddGroupedCommands(string dllFullPath, RibbonPanel ribbonPanel, IEnumerable<IGrouping<string, Command>> groupedCommands)
        {
            foreach (var group in groupedCommands)
            {
                SplitButtonData splitButtonData = new SplitButtonData(group.Key, group.Key);
                var splitButton = ribbonPanel.AddItem(splitButtonData) as SplitButton;
                foreach (var command in group)
                {
                    var pbd = new PushButtonData(command.Name, command.Name, dllFullPath, "Command" + command.Index)
                        {
                            Image = command.SmallImage,
                            LargeImage = command.LargeImage
                        };
                    splitButton?.AddPushButton(pbd);
                }
            }
        }


        private static void AddUngroupedCommands(string dllFullPath, RibbonPanel ribbonPanel, List<Command> commands)
        {
            // add canned commands as stacked push-buttons (try to pack 3 commands per push-button, then 2)            
            while (commands.Count > 4 || commands.Count == 3)
            {
                // remove the first three commands from the list
                var command0 = commands[0];
                var command1 = commands[1];
                var command2 = commands[2];
                commands.RemoveAt(0);
                commands.RemoveAt(0);
                commands.RemoveAt(0);

                PushButtonData pbdA = new PushButtonData(command0.Name, command0.Name, dllFullPath, "Command" + command0.Index)
                    {
                        Image = command0.SmallImage,
                        LargeImage = command0.LargeImage
                    };

                PushButtonData pbdB = new PushButtonData(command1.Name, command1.Name, dllFullPath, "Command" + command1.Index)
                    {
                        Image = command1.SmallImage,
                        LargeImage = command1.LargeImage
                    };

                PushButtonData pbdC = new PushButtonData(command2.Name, command2.Name, dllFullPath, "Command" + command2.Index)
                    {
                        Image = command2.SmallImage,
                        LargeImage = command2.LargeImage
                    };

                ribbonPanel.AddStackedItems(pbdA, pbdB, pbdC);
            }
            if (commands.Count == 4)
            {
                // remove the first two commands from the list
                var command0 = commands[0];
                var command1 = commands[1];
                commands.RemoveAt(0);
                commands.RemoveAt(0);

                PushButtonData pbdA = new PushButtonData(command0.Name, command0.Name, dllFullPath, "Command" + command0.Index)
                    {
                        Image = command0.SmallImage,
                        LargeImage = command0.LargeImage
                    };

                PushButtonData pbdB = new PushButtonData(command1.Name, command1.Name, dllFullPath, "Command" + command1.Index)
                    {
                        Image = command0.SmallImage,
                        LargeImage = command0.LargeImage
                    };

                ribbonPanel.AddStackedItems(pbdA, pbdB);
            }
            if (commands.Count == 2)
            {
                // remove first two commands from the list
                var command0 = commands[0];
                var command1 = commands[1];
                commands.RemoveAt(0);
                commands.RemoveAt(0);
                PushButtonData pbdA = new PushButtonData(command0.Name, command0.Name, dllFullPath, "Command" + command0.Index)
                    {
                        Image = command0.SmallImage,
                        LargeImage = command0.LargeImage
                    };

                PushButtonData pbdB = new PushButtonData(command1.Name, command1.Name, dllFullPath, "Command" + command1.Index)
                    {
                        Image = command1.SmallImage,
                        LargeImage = command1.LargeImage
                    };

                ribbonPanel.AddStackedItems(pbdA, pbdB);
            }

            if (commands.Count != 1) return;
            // only one command defined, show as a big button...
            var command = commands[0];
            PushButtonData pbd = new PushButtonData(command.Name, command.Name, dllFullPath, "Command" + command.Index)
            {
                Image = command.SmallImage,
                LargeImage = command.LargeImage
            };
            ribbonPanel.AddItem(pbd);
        }

        /// <summary>
        /// Creates a dynamic assembly that contains types for starting the canned commands.
        /// </summary>
        private static void CreateCommandLoaderAssembly(XDocument repository, string dllFolder, string dlName)
        {
            var assemblyName = new AssemblyName { Name = dlName + ".dll", Version = new Version(1, 0, 0, 0) };
            var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave, dllFolder);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("CommandLoaderModule", dlName + ".dll");

            foreach (var command in GetCommands(repository))
            {
                var typeBuilder = moduleBuilder.DefineType("Command" + command.Index,
                                                        TypeAttributes.Class | TypeAttributes.Public,
                                                        typeof(CommandLoaderBase));

                // add RegenerationAttribute to type
                var regenerationConstructorInfo = typeof(RegenerationAttribute).GetConstructor(new Type[] { typeof(RegenerationOption) });                
                var regenerationAttributeBuilder = new CustomAttributeBuilder(regenerationConstructorInfo!, new object[] {RegenerationOption.Manual});
                typeBuilder.SetCustomAttribute(regenerationAttributeBuilder);

                // add TransactionAttribute to type
                var transactionConstructorInfo = typeof(TransactionAttribute).GetConstructor(new Type[] { typeof(TransactionMode) });
                var transactionAttributeBuilder = new CustomAttributeBuilder(transactionConstructorInfo!, new object[] { TransactionMode.Manual });
                typeBuilder.SetCustomAttribute(transactionAttributeBuilder);

                // call base constructor with script path
                var ci = typeof(CommandLoaderBase).GetConstructor(new[] { typeof(string) });

                var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[0]);
                var gen = constructorBuilder.GetILGenerator();
                gen.Emit(OpCodes.Ldarg_0);                // Load "this" onto eval stack
                gen.Emit(OpCodes.Ldstr, command.Source);  // Load the path to the command as a string onto stack
                gen.Emit(OpCodes.Call, ci!);               // call base constructor (consumes "this" and the string)
                gen.Emit(OpCodes.Nop);                    // Fill some space - this is how it is generated for equivalent C# code
                gen.Emit(OpCodes.Nop);
                gen.Emit(OpCodes.Nop);
                gen.Emit(OpCodes.Ret);                    // return from constructor
                typeBuilder.CreateType();
            }
            assemblyBuilder.Save(dlName + ".dll");
        }

        Result IExternalApplication.OnShutdown(UIControlledApplication application)
        {
            // FIXME: deallocate the python shell...
            return Result.Succeeded;
        }
        
        public static IRpsConfig GetConfig()
        {           
            return new RpsConfig(GetSettingsFile());
        }

        /// <summary>
        /// Returns a handle to the settings file.
        /// </summary>
        /// <returns></returns>
        public static XDocument GetSettings()
        {
            string settingsFile = GetSettingsFile();
            return XDocument.Load(settingsFile);
        }

        private static string GetSettingsFile()
        {
            string folder = GetSettingsFolder();
            return Path.Combine(folder, "RevitPythonShell.xml");
        }

        /// <summary>
        /// Returns the name of the folder with the settings file. This folder
        /// is also the default folder for relative paths in StartupScript and InitScript tags.
        /// </summary>
        private static string GetSettingsFolder()
        {

            return _dllFolder;
        }

        /// <summary>
        /// Returns a list of commands as defined in the repository file.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<Command> GetCommands(XDocument repository)
        {
            int i = 0;
            foreach (var commandNode in repository.Root?.Descendants("Command") ?? new List<XElement>())
            {
                var addinAssembly = typeof(RpsExternalApplicationBase).Assembly;
                var commandName = commandNode.Attribute("name")?.Value;
                var commandSrc = commandNode.Attribute("src")?.Value;
                var group = commandNode.Attribute("group") == null ? "" : commandNode.Attribute("group")?.Value;
                
                ImageSource largeImage;
                if (IsValidPath(commandNode.Attribute("largeImage")))
                {
                    var largeImagePath = GetAbsolutePath(commandNode.Attribute("largeImage")?.Value);
                    largeImage = BitmapDecoder.Create(File.OpenRead(largeImagePath), BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0];
                }
                else
                {
                    largeImage = GetEmbeddedPng(addinAssembly, "RpsRuntime.Resources.PythonScript32x32.png");
                }

                ImageSource smallImage;
                if (IsValidPath(commandNode.Attribute("smallImage")))
                {
                    var smallImagePath = GetAbsolutePath(commandNode.Attribute("smallImage")?.Value);
                    smallImage = BitmapDecoder.Create(File.OpenRead(smallImagePath), BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0];
                }
                else
                {
                    smallImage = GetEmbeddedPng(addinAssembly, "RpsRuntime.Resources.PythonScript16x16.png");
                }
                
                yield return new Command { 
                        Name = commandName, 
                        Source = commandSrc, 
                        Group = group,
                        LargeImage = largeImage,
                        SmallImage = smallImage,
                        Index = i++
                };
            }
        }

        /// <summary>
        /// True, if the contents of the attribute is a valid absolute path (or relative path to the assembly) is
        /// an existing path.
        /// </summary>
        private static bool IsValidPath(XAttribute pathAttribute)
        {
            if (pathAttribute != null && !string.IsNullOrEmpty(pathAttribute.Value))
            {
                return File.Exists(GetAbsolutePath(pathAttribute.Value));
            }
            return false;
        }

        /// <summary>
        /// Return an absolute path for an input path, with relative paths seen as
        /// relative to the assembly location. No guarantees are made 
        /// whether the path exists or not.
        /// </summary>
        private static string GetAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            else
            {
                var assembly = typeof(App).Assembly;
                return Path.Combine(Path.GetDirectoryName(assembly.Location)!, path);
            }
        }

        /// <summary>
        /// Returns a string to be executed, whenever the interactive shell is started.
        /// If this is not specified in the XML file (under /RevitPythonShell/InitScript),
        /// then null is returned.
        /// </summary>
        public static string GetInitScript()
        {
            var path = GetInitScriptPath();
            if (File.Exists(path))
            {
                using var reader = File.OpenText(path);
                var source = reader.ReadToEnd();
                return source;
            }

            // backwards compatibility: InitScript used to have a CDATA section directly
            // embedded in the settings xml file
            var initScriptTags = GetSettings().Root?.Descendants("InitScript") ?? new List<XElement>();
            var scriptTags = initScriptTags as XElement[] ?? initScriptTags.ToArray();
            if (!scriptTags.Any())
            {
                return null;
            }
            var firstScript = scriptTags.First();
            // backwards compatibility: InitScript used to be included as CDATA in the config file
            return firstScript.Value.Trim();
        }

        /// <summary>
        /// Returns the path to the InitScript as configured in the settings file or "" if not
        /// configured. This is used in the ConfigureCommandsForm.
        /// </summary>
        public static string GetInitScriptPath()
        {
            return GetScriptPath("InitScript");
        }


        /// <summary>
        /// Returns the path to the StartupScript as configured in the settings file or "" if not
        /// configured. This is used in the ConfigureCommandsForm.
        /// </summary>
        public static string GetStartupScriptPath()
        {
            return GetScriptPath("StartupScript");
        }

        /// <summary>
        /// Returns the value of the "src" attribute for the tag "tagName" in the settings file
        /// or "" if not configured.
        /// </summary>        
        private static string GetScriptPath(string tagName)
        {
            var tags = GetSettings().Root?.Descendants(tagName) ?? new List<XElement>();
            var xElements = tags as XElement[] ?? tags.ToArray();
            if (!xElements.Any())
            {
                return "";
            }
            var firstScript = xElements.First();
            if (firstScript.Attribute("src") == null) return "";
            var path = firstScript.Attribute("src")?.Value;
            return Path.IsPathRooted(path) ? path : Path.Combine(GetSettingsFolder(), path!);
        }

        /// <summary>
        /// Returns a string to be executed, whenever the revit is started.
        /// If this is not specified as a path to an existing file in the XML file (under /RevitPythonShell/StartupScript/@src),
        /// then null is returned.
        /// </summary>
        private static string GetStartupScript()
        {
            var path = GetStartupScriptPath();
            if (!File.Exists(path)) return null;
            using var reader = File.OpenText(path);
            var source = reader.ReadToEnd();
            return source;
            // no startup script found
        }

        /// <summary>
        /// Writes settings to the settings file, replacing the old commands.
        /// </summary>
        public static void WriteSettings(
            IEnumerable<Command> commands,
            IEnumerable<string> searchPaths, 
            IEnumerable<KeyValuePair<string, string>> variables,
            string initScript,
            string startupScript)
        {
            var doc = GetSettings();
            var settingsFolder = GetSettingsFolder();

            // clean out current stuff
            foreach (var xmlExistingCommands in (doc.Root?.Descendants("Commands") ?? new List<XElement>()).ToList())
            {
                xmlExistingCommands.Remove();
            }
            foreach (var xmlExistingSearchPaths in doc.Root?.Descendants("SearchPaths").ToList()!)
            {
                xmlExistingSearchPaths.Remove();
            }
            foreach (var xmlExistingVariables in doc.Root?.Descendants("Variables").ToList()!)
            {
                xmlExistingVariables.Remove();
            }
            foreach (var xmlExistingInitScript in doc.Root?.Descendants("InitScript").ToList()!)
            {
                xmlExistingInitScript.Remove();
            }
            foreach (var xmlExistingStartupScript in doc.Root?.Descendants("StartupScript").ToList()!)
            {
                xmlExistingStartupScript.Remove();
            }

            // add commands
            var xmlCommands = new XElement("Commands");
            foreach (var command in commands)
            {
                xmlCommands.Add(new XElement(
                    "Command", 
                        new XAttribute("name", command.Name), 
                        new XAttribute("src", command.Source),
                        new XAttribute("group", command.Group)));

            }
            doc.Root?.Add(xmlCommands);            

            // add search paths
            var xmlSearchPaths = new XElement("SearchPaths");
            var enumerable = searchPaths as string[] ?? searchPaths.ToArray();
            foreach (var path in enumerable)
            {
                xmlSearchPaths.Add(new XElement(
                    "SearchPath",
                        new XAttribute("name", path)));

            }
            // ensure the settings directory is added to the search paths
            if (!enumerable.Contains(settingsFolder)) {
                xmlSearchPaths.Add(new XElement(
                    "SearchPath",
                        new XAttribute("name", settingsFolder)));

            }
            doc.Root?.Add(xmlSearchPaths);

            // add variables
            var xmlVariables = new XElement("Variables");
            foreach (var variable in variables)
            {
                xmlVariables.Add(new XElement(
                    "StringVariable",
                        new XAttribute("name", variable.Key),
                        new XAttribute("value", variable.Value)));

            }
            doc.Root?.Add(xmlVariables);

            // add init script
            var xmlInitScript = new XElement("InitScript");
            xmlInitScript.Add(new XAttribute("src", initScript));
            doc.Root?.Add(xmlInitScript);

            // add startup script
            var xmlStartupScript = new XElement("StartupScript");
            xmlStartupScript.Add(new XAttribute("src", startupScript));
            doc.Root?.Add(xmlStartupScript);

            doc.Save(GetSettingsFile());
        }
    }

    /// <summary>
    /// A simple structure to hold information about canned commands.
    /// </summary>
    internal class Command
    {
        public string Name;
        public string Group;
        public string Source;
        public int Index;
        public ImageSource LargeImage;
        public ImageSource SmallImage;        

        public override string ToString()
        {
            return Name;
        }
    }
}
