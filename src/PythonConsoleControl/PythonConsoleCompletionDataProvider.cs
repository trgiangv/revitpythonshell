// Copyright (c) 2010 Joe Moorhouse

using System.Text;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Scripting.Hosting.Shell;
using Microsoft.Scripting;
using System.Reflection;
using System.Text.RegularExpressions;
using IronPython.Runtime;

namespace PythonConsoleControl
{
    /// <summary>
    /// Provides code completion for the Python Console window.
    /// </summary>
    public class PythonConsoleCompletionDataProvider(CommandLine commandLine)
    {
        internal volatile bool AutocompletionInProgress;

        private bool _excludeCallables;
        public bool ExcludeCallables { get => _excludeCallables;
            set => _excludeCallables = value;
        }

        //IMemberProvider memberProvider)

        /// <summary>
        /// Generates completion data for the specified text. The text should be everything before
        /// the dot character that triggered the completion. The text can contain the command line prompt
        /// '>>>' as this will be ignored.
        /// </summary>
        public Tuple<ICompletionData[], string, string> GenerateCompletionData(string line)
        {
            List<PythonCompletionData> items = []; //DefaultCompletionData

            string objectName = string.Empty;
            string memberName = string.Empty;

            int lastDelimiterIndex = FindLastDelimiter(line);

            string name = line.Substring(lastDelimiterIndex + 1);

            // A very simple test of callables!
            bool isCallable = name.Contains(')');

            if (_excludeCallables && isCallable) return null;

            System.IO.Stream stream = commandLine.ScriptScope.Engine.Runtime.IO.OutputStream;
            try
            {
                AutocompletionInProgress = true;
                // Another possibility:
                //commandLine.ScriptScope.Engine.Runtime.IO.SetOutput(new System.IO.MemoryStream(), Encoding.UTF8);
                //object value = commandLine.ScriptScope.Engine.CreateScriptSourceFromString(name, SourceCodeKind.Expression).Execute(commandLine.ScriptScope);
                //IList<string> members = commandLine.ScriptScope.Engine.Operations.GetMemberNames(value);

                var lastWord = GetLastWord(name);
                var beforeLastWord = name.Substring(0, name.Length - lastWord.Length);
                if (beforeLastWord.EndsWith("."))
                {
                    objectName = beforeLastWord.Substring(0, beforeLastWord.Length - 1);
                    memberName = lastWord;
                }
                else
                {
                    objectName = string.Empty;
                    memberName = lastWord;
                }

                Type type = TryGetType(objectName);

                // Use Reflection for everything except in-built Python types and COM pbjects. 
                if (type != null && type.Namespace != "IronPython.Runtime" && !type.FullName!.Contains("IronPython.NewTypes") && (type.Name != "__ComObject"))
                {
                    PopulateFromClrType(items, type, objectName);
                }
                else
                {
                    PopulateFromPythonType(items, objectName);
                    AutocompletionInProgress = false;
                }
            }
            catch (ThreadAbortException tae)
            {
                if (tae.ExceptionState is KeyboardInterruptException) Thread.ResetAbort();
            }
            catch
            {
                // Do nothing.
            }
            commandLine.ScriptScope.Engine.Runtime.IO.SetOutput(stream, Encoding.UTF8);
            AutocompletionInProgress = false;
            return Tuple.Create(items.Cast<ICompletionData>().ToArray(), objectName, memberName);
        }

        private Type TryGetType(string name)
        {
            string tryGetType = name + ".GetType()";
            object type = null;
            try
            {
                type = commandLine.ScriptScope.Engine.CreateScriptSourceFromString(tryGetType, SourceCodeKind.Expression).Execute(commandLine.ScriptScope);
            }
            catch (ThreadAbortException tae)
            {
                if (tae.ExceptionState is KeyboardInterruptException) Thread.ResetAbort();
            }
            catch
            {
                // Do nothing.
            }
            return type as Type;
        }

        private void PopulateFromClrType(List<PythonCompletionData> items, Type type, string name)
        {
            List<string> completionsList = [];
            MethodInfo[] methodInfo = type.GetMethods();
            PropertyInfo[] propertyInfo = type.GetProperties();
            FieldInfo[] fieldInfo = type.GetFields();
            foreach (MethodInfo methodInfoItem in methodInfo)
            {
                if ((methodInfoItem.IsPublic)
                    && (methodInfoItem.Name.IndexOf("get_", StringComparison.Ordinal) != 0) && (methodInfoItem.Name.IndexOf("set_", StringComparison.Ordinal) != 0)
                    && (methodInfoItem.Name.IndexOf("add_", StringComparison.Ordinal) != 0) && (methodInfoItem.Name.IndexOf("remove_", StringComparison.Ordinal) != 0)
                    && (methodInfoItem.Name.IndexOf("__", StringComparison.Ordinal) != 0))
                    completionsList.Add(methodInfoItem.Name);
            }

            completionsList.AddRange(propertyInfo.Select(propertyInfoItem => propertyInfoItem.Name));
            completionsList.AddRange(fieldInfo.Select(fieldInfoItem => fieldInfoItem.Name));
            completionsList.Sort();
            string last = "";
            for (int i = completionsList.Count - 1; i > 0; --i)
            {
                if (completionsList[i] == last) completionsList.RemoveAt(i);
                else last = completionsList[i];
            }

            items.AddRange(completionsList.Select(completion => new PythonCompletionData(completion, name, commandLine, true)));
        }

        private void PopulateFromPythonType(List<PythonCompletionData> items, string name)
        {
            //string dirCommand = "dir(" + objectName + ")";
            string dirCommand = "sorted([m for m in dir(" + name + ") if not m.startswith('__')], key = str.lower) + sorted([m for m in dir(" + name + ") if m.startswith('__')])";
            object value = commandLine.ScriptScope.Engine.CreateScriptSourceFromString(dirCommand, SourceCodeKind.Expression).Execute(commandLine.ScriptScope);
            foreach (object member in ((PythonList)value))
            {
                bool isInstance = false;

                if (name == string.Empty) // Special case for globals
                {
                    isInstance = TryGetType((string)member) != null;
                }

                items.Add(new PythonCompletionData((string)member, name, commandLine, isInstance));
            }
        }

        /// <summary>
        /// Generates completion data for the specified text. The text should be everything before
        /// the dot character that triggered the completion. The text can contain the command line prompt
        /// '>>>' as this will be ignored.
        /// </summary>
        public void GenerateDescription(string stub, string item, DescriptionUpdateDelegate updateDescription, bool isInstance)
        {
            System.IO.Stream stream = commandLine.ScriptScope.Engine.Runtime.IO.OutputStream;
            string description = "";
            if (String.IsNullOrEmpty(item)) return;
            try
            {
                AutocompletionInProgress = true;
                // Another possibility:
                //commandLine.ScriptScope.Engine.Runtime.IO.SetOutput(new System.IO.MemoryStream(), Encoding.UTF8);
                //object value = commandLine.ScriptScope.Engine.CreateScriptSourceFromString(item, SourceCodeKind.Expression).Execute(commandLine.ScriptScope);
                //description = commandLine.ScriptScope.Engine.Operations.GetDocumentation(value);
                string docCommand = "";

                if (isInstance)
                {
                    if (stub != string.Empty)
                    {
                        docCommand = "type(" + stub + ")" + "." + item + ".__doc__";
                    }
                    else
                    {
                        docCommand = "type(" + item + ")" + ".__doc__";
                    }
                }
                else
                {
                    if (stub != string.Empty)
                    {
                        docCommand = stub + "." + item + ".__doc__";
                    }
                    else
                    {
                        docCommand = item + ".__doc__";
                    }
                }

                object value = commandLine.ScriptScope.Engine.CreateScriptSourceFromString(docCommand, SourceCodeKind.Expression).Execute(commandLine.ScriptScope);
                description = (string)value;
                AutocompletionInProgress = false;
            }
            catch (ThreadAbortException tae)
            {
                if (tae.ExceptionState is KeyboardInterruptException) Thread.ResetAbort();
                AutocompletionInProgress = false;
            }
            catch
            {
                AutocompletionInProgress = false;
                // Do nothing.
            }
            commandLine.ScriptScope.Engine.Runtime.IO.SetOutput(stream, Encoding.UTF8);
            updateDescription(description);
        }

        private static readonly Regex MatchAllWord = new(@"^\w+$");
        private static readonly Regex MatchLastWord = new(@"\w+$");

        private static readonly char[] DelimitingChars = [',', '\t', ' ', ':', ';', '+', '-', '=', '*', '/', '&', '|', '^', '%', '~', '<', '>'
        ];

        private static string GetLastWord(string text)
        {
            return MatchLastWord.Match(text).Value;
        }

        private static int FindLastDelimiter(string text)
        {
            int lastDelimitingIndex = -1;

            // TODO: handle balanced but malformed cases such as '( [ ) ]'
            int lastUnbalancedParenthesisIndex = FindLastUnbalancedChar(text, '(', ')');
            int lastUnbalancedBracketIndex = FindLastUnbalancedChar(text, '[', ']');

            lastDelimitingIndex = Math.Max(lastUnbalancedParenthesisIndex, lastUnbalancedBracketIndex);

            bool insideDoubleQuotedString = false;
            bool insideSingleQuotedString = false;

            for (int i = (lastDelimitingIndex + 1); i < text.Length; i++)
            {
                char c = text[i];
                
                // NOTE: rudimentary string detection (doesn't handle escaped quotes or triple quotes!)
                if (c == '"' && !insideSingleQuotedString)
                {
                    insideDoubleQuotedString = !insideDoubleQuotedString;
                }
                else if (c == '\'' && !insideDoubleQuotedString)
                {
                    insideSingleQuotedString = !insideSingleQuotedString;
                }
                else if (!insideDoubleQuotedString && !insideSingleQuotedString)
                {
                    if (c == '(')
                    {
                        int lastClosed = FindLastUnbalancedChar(text.Substring(i+1), '(', ')');
                        i = i + 1 + lastClosed;
                    }
                    else if (c == '[')
                    {
                        int lastClosed = FindLastUnbalancedChar(text.Substring(i+1), '[', ']');
                        i = i + 1 + lastClosed;
                    }
                    else if (DelimitingChars.Contains(c))
                    {
                        lastDelimitingIndex = i;
                    }
                }
            }

            return lastDelimitingIndex;
        }

        private static int FindLastUnbalancedChar(string text, char openedChar, char closedChar)
        {
            int lastIndex = -1;

            bool insideDoubleQuotedString = false;
            bool insideSingleQuotedString = false;
            var unbalancedIndices = new Stack<int>();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                
                // NOTE: rudimentary string detection (doesn't handle escaped quotes or triple quotes!)
                if (c == '"' && !insideSingleQuotedString)
                {
                    insideDoubleQuotedString = !insideDoubleQuotedString;
                }
                else if (c == '\'' && !insideDoubleQuotedString)
                {
                    insideSingleQuotedString = !insideSingleQuotedString;
                }
                else if (!insideDoubleQuotedString && !insideSingleQuotedString)
                {
                    if (c == openedChar)
                    {
                        unbalancedIndices.Push(i);
                    }
                    else if (c == closedChar)
                    {
                        if (unbalancedIndices.Count == 0)
                        {
                            lastIndex = i;
                        }
                        else
                        {
                            unbalancedIndices.Pop();
                        }
                    }
                }
            }

            if (unbalancedIndices.Count > 0)
            {
                lastIndex = unbalancedIndices.Pop();
            }

            return lastIndex;
        }
    }
}
