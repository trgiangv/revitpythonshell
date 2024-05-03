// Copyright (c) 2010 Joe Moorhouse

using System.IO;
using Microsoft.Scripting.Hosting.Shell;
using System.Windows.Input;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Document;
using Style = Microsoft.Scripting.Hosting.Shell.Style;
using System.Runtime.Remoting;

namespace PythonConsoleControl
{
    public delegate void ConsoleInitializedEventHandler(object sender, EventArgs e);

    /// <summary>
    /// Custom IronPython console. The command dispatcher runs on a separate UI thread from the REPL
    /// and also from the WPF control.
    /// </summary>
    public class PythonConsole : IConsole, IDisposable
    {
        private bool _allowFullAutocompletion = true;
        public bool AllowFullAutocompletion
        {
            get => _allowFullAutocompletion;
            set => _allowFullAutocompletion = value;
        }

        private readonly bool _disableAutocompletionForCallables = true;

        private bool _allowCtrlSpaceAutocompletion = true;
        public bool AllowCtrlSpaceAutocompletion
        {
            get => _allowCtrlSpaceAutocompletion;
            set => _allowCtrlSpaceAutocompletion = value;
        }

        private readonly PythonTextEditor _textEditor;
        private readonly int _lineReceivedEventIndex = 0; // The index into the waitHandles array where the lineReceivedEvent is stored.
        private readonly ManualResetEvent _lineReceivedEvent = new(false);
        private readonly ManualResetEvent _disposedEvent = new(false);
        private readonly WaitHandle[] _waitHandles;
        private int _promptLength = 4;
        private readonly List<string> _previousLines = [];
        private readonly CommandLine _commandLine;
        private readonly CommandLineHistory _commandLineHistory = new();

        private volatile bool _executing;

        // This is the thread upon which all commands execute unless the dipatcher is overridden.
        private readonly Thread _dispatcherThread;
        private Dispatcher _dispatcher;

        private string _scriptText = String.Empty;
        private bool _consoleInitialized;
        private readonly string _prompt;

        public event ConsoleInitializedEventHandler ConsoleInitialized;

        public ScriptScope ScriptScope => _commandLine.ScriptScope;

        public PythonConsole(PythonTextEditor textEditor, CommandLine commandLine)
        {
            _waitHandles = [_lineReceivedEvent, _disposedEvent];

            _commandLine = commandLine;
            _textEditor = textEditor;
            textEditor.CompletionProvider = new PythonConsoleCompletionDataProvider(commandLine) { ExcludeCallables = _disableAutocompletionForCallables };
            textEditor.PreviewKeyDown += textEditor_PreviewKeyDown;
            textEditor.TextEntering += textEditor_TextEntering;
            _dispatcherThread = new Thread(DispatcherThreadStartingPoint);
            _dispatcherThread.SetApartmentState(ApartmentState.STA);
            _dispatcherThread.IsBackground = true;
            _dispatcherThread.Start();

            // Only required when running outside REP loop.
            _prompt = ">>> ";

            // Set commands:
            _textEditor.TextArea.Dispatcher.Invoke(delegate
            {
                CommandBinding pasteBinding = null;
                CommandBinding copyBinding = null;
                CommandBinding cutBinding = null;
                CommandBinding undoBinding = null;
                CommandBinding deleteBinding = null;
                foreach (CommandBinding commandBinding in (_textEditor.TextArea.CommandBindings))
                {
                    if (commandBinding.Command == ApplicationCommands.Paste) pasteBinding = commandBinding;
                    if (commandBinding.Command == ApplicationCommands.Copy) copyBinding = commandBinding;
                    if (commandBinding.Command == ApplicationCommands.Cut) cutBinding = commandBinding;
                    if (commandBinding.Command == ApplicationCommands.Undo) undoBinding = commandBinding;
                    if (commandBinding.Command == ApplicationCommands.Delete) deleteBinding = commandBinding;
                }
                // Remove current bindings completely from control.These are static, so modifying them will cause other
                // controls' behavior to change too.
                if (pasteBinding != null) _textEditor.TextArea.CommandBindings.Remove(pasteBinding);
                if (copyBinding != null) _textEditor.TextArea.CommandBindings.Remove(copyBinding);
                if (cutBinding != null) _textEditor.TextArea.CommandBindings.Remove(cutBinding);
                if (undoBinding != null) _textEditor.TextArea.CommandBindings.Remove(undoBinding);
                if (deleteBinding != null) _textEditor.TextArea.CommandBindings.Remove(deleteBinding);
                _textEditor.TextArea.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPaste, CanPaste));
                _textEditor.TextArea.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy, PythonEditingCommandHandler.CanCutOrCopy));
                _textEditor.TextArea.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, PythonEditingCommandHandler.OnCut, CanCut));
                _textEditor.TextArea.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, OnUndo, CanUndo));
                _textEditor.TextArea.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, PythonEditingCommandHandler.OnDelete(ApplicationCommands.NotACommand), CanDeleteCommand));

            });            
            // Set dispatcher to run on a UI thread independent of both the Control UI thread and thread running the REPL.
            WhenConsoleInitialized(delegate
            {
                SetCommandDispatcher(DispatchCommand);
            });                       
        }

        public Action<Action> GetCommandDispatcher()
        {
            var languageContext = Microsoft.Scripting.Hosting.Providers.HostingHelpers.GetLanguageContext(_commandLine.ScriptScope.Engine);
            var pythonContext = (IronPython.Runtime.PythonContext)languageContext;
            var result = pythonContext.GetSetCommandDispatcher(null);
            pythonContext.GetSetCommandDispatcher(result);
            return result;
        }

        public void SetCommandDispatcher(Action<Action> newDispatcher)
        {
            var languageContext = Microsoft.Scripting.Hosting.Providers.HostingHelpers.GetLanguageContext(_commandLine.ScriptScope.Engine);
            var pythonContext = (IronPython.Runtime.PythonContext)languageContext;
            pythonContext.GetSetCommandDispatcher(newDispatcher);
        }

        private void DispatchCommand(Delegate command)
        {
            if (command == null) return;
            // Slightly involved form to enable keyboard interrupt to work.
            _executing = true;
            var operation = _dispatcher.BeginInvoke(DispatcherPriority.Normal, command);
            while (_executing)
            {
                if (operation.Status != DispatcherOperationStatus.Completed)
                    operation.Wait(TimeSpan.FromSeconds(1));
                if (operation.Status == DispatcherOperationStatus.Completed)
                    _executing = false;
            }
        }

        /// <summary>
        /// Perform action only after the console was initialized.
        /// </summary>
        public void WhenConsoleInitialized(Action action)
        {
            if (_consoleInitialized)
            {
                action();
            }
            else
            {
                ConsoleInitialized += (_, _) => action();
            }
        }

        private void DispatcherThreadStartingPoint()
        {
            _dispatcher = new Window().Dispatcher;
            while (true)
            {
                try
                {
                    Dispatcher.Run();
                }
                catch (ThreadAbortException tae)
                {
                    if (tae.ExceptionState is KeyboardInterruptException)
                    {
                        Thread.ResetAbort();
                        _executing = false;
                    }
                }
            }
        }

        public void SetDispatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Dispose()
        {
            _disposedEvent.Set();
            _textEditor.PreviewKeyDown -= textEditor_PreviewKeyDown;
            _textEditor.TextEntering -= textEditor_TextEntering;
        }

        public TextWriter Output
        {
            get => null;
            set { }
        }

        public TextWriter ErrorOutput
        {
            get => null;
            set { }
        }

        #region CommandHandling

        private void CanPaste(object target, CanExecuteRoutedEventArgs args)
        {
            args.CanExecute = !IsInReadOnlyRegion;
        }

        private void CanCut(object target, CanExecuteRoutedEventArgs args)
        {
            if (!CanDelete)
            {
                args.CanExecute = false;
            }
            else
                PythonEditingCommandHandler.CanCutOrCopy(target, args);
        }

        private void CanDeleteCommand(object target, CanExecuteRoutedEventArgs args)
        {
            if (!CanDelete)
            {
                args.CanExecute = false;
            }
            else
                PythonEditingCommandHandler.CanDelete(target, args);
        }

        private void CanUndo(object target, CanExecuteRoutedEventArgs args)
        {
            args.CanExecute = false;
        }

        private void OnPaste(object target, ExecutedRoutedEventArgs args)
        {
            if (!target.Equals(_textEditor.TextArea)) return;
            TextArea textArea = _textEditor.TextArea;
            if (textArea is not { Document: not null }) return;

            // convert text back to correct newlines for this document
            string newLine = TextUtilities.GetNewLineFromDocument(textArea.Document, textArea.Caret.Line);
            string text = TextUtilities.NormalizeNewLines(Clipboard.GetText(), newLine);
            string[] commands = text.Split([newLine], StringSplitOptions.None);
            string scriptText = "";
            if (commands.Length > 1)
            {
                text = newLine;
                foreach (string command in commands)
                {
                    text += "... " + command + newLine;
                    scriptText += command.Replace("\t", "   ") + newLine;
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                bool fullLine = textArea.Options.CutCopyWholeLine && Clipboard.ContainsData(LineSelectedType);
                bool rectangular = Clipboard.ContainsData(RectangleSelection.RectangularSelectionDataType);
                if (fullLine)
                {
                    DocumentLine currentLine = textArea.Document.GetLineByNumber(textArea.Caret.Line);
                    if (textArea.ReadOnlySectionProvider.CanInsert(currentLine.Offset))
                    {
                        textArea.Document.Insert(currentLine.Offset, text);
                    }
                }
                else if (rectangular && textArea.Selection.IsEmpty)
                {
                    if (!RectangleSelection.PerformRectangularPaste(textArea, textArea.Caret.Position, text, false))
                        _textEditor.Write(text, false, false);
                }
                else
                {
                    _textEditor.Write(text, false, false);
                }
            }
            textArea.Caret.BringCaretToView();
            args.Handled = true;

            if (commands.Length <= 1) return;
            lock (_scriptText)
            {
                _scriptText = scriptText;
            }
            _dispatcher.BeginInvoke(new Action(ExecuteStatements));
        }

        private void OnCopy(object target, ExecutedRoutedEventArgs args)
        {
            if (target.Equals(_textEditor.TextArea)) return;
            if (_textEditor.SelectionLength == 0 && _executing)
            {
                // Send the 'Ctrl-C' abort 
                //if (!IsInReadOnlyRegion)
                //{
                MoveToHomePosition();
                //textEditor.Column = GetLastTextEditorLine().Length + 1;
                //textEditor.Write (Environment.NewLine);
                //}
                _dispatcherThread.Abort(new KeyboardInterruptException(""));
                args.Handled = true;
            }
            else PythonEditingCommandHandler.OnCopy(target, args);
        }

        private const string LineSelectedType = "MSDEVLineSelect";  // This is the type VS 2003 and 2005 use for flagging a whole line copy

        private void OnUndo(object target, ExecutedRoutedEventArgs args)
        {
        }
        #endregion

        /// <summary>
        /// Run externally provided statements in the Console Engine. 
        /// </summary>
        /// <param name="statements"></param>
        public void RunStatements(string statements)
        {
            MoveToHomePosition();
            lock (_scriptText)
            {
                _scriptText = statements;
            }
            _dispatcher.BeginInvoke(new Action(ExecuteStatements));
        }

        /// <summary>
        /// Run on the statement execution thread. 
        /// </summary>
        private void ExecuteStatements()
        {
            lock (_scriptText)
            {
                _textEditor.Write("\r\n");
                ScriptSource scriptSource = _commandLine.ScriptScope.Engine.CreateScriptSourceFromString(_scriptText, SourceCodeKind.Statements);
                string error = "";
                try
                {
                    _executing = true;
                    var errors = new ErrorReporter();
                    var command = scriptSource.Compile(errors);
                    if (command == null)
                    {
                        // compilation failed
                        error = "Syntax Error: " + string.Join("\nSyntax Error: ", errors.Errors) + "\n";
                    }
                    else
                    {
                        ObjectHandle wrapexception = null;
                        GetCommandDispatcher()(() => scriptSource.ExecuteAndWrap(_commandLine.ScriptScope, out wrapexception));
                        if (wrapexception != null)
                        {
                            error = "Exception : " + wrapexception.Unwrap() + "\n";
                        }
                    }                    
                }
                catch (ThreadAbortException tae)
                {
                    if (tae.ExceptionState is KeyboardInterruptException) Thread.ResetAbort();
                    error = "KeyboardInterrupt" + Environment.NewLine;
                }
                catch (SyntaxErrorException exception)
                {
                    var eo = _commandLine.ScriptScope.Engine.GetService<ExceptionOperations>();
                    error = eo.FormatException(exception);
                }
                catch (Exception exception)
                {
                    var eo = _commandLine.ScriptScope.Engine.GetService<ExceptionOperations>();
                    error = eo.FormatException(exception) + Environment.NewLine;
                }
                _executing = false;
                if (error != "") _textEditor.Write(error);
                _textEditor.Write(_prompt);
            }
        }

        /// <summary>
        /// Returns the next line typed in by the console user. If no line is available, this method
        /// will block.
        /// </summary>
        public string ReadLine(int autoIndentSize)
        {
            string indent = String.Empty;
            if (autoIndentSize > 0)
            {
                indent = String.Empty.PadLeft(autoIndentSize);
                Write(indent, Style.Prompt);
            }

            string line = ReadLineFromTextEditor();
            if (line != null)
            {
                return indent + line;
            }
            return null;
        }

        /// <summary>
        /// Writes text to the console.
        /// </summary>
        public void Write(string text, Style style)
        {
            _textEditor.Write(text);
            if (style != Style.Prompt) return;
            _promptLength = text.Length;
            if (_consoleInitialized) return;
            _consoleInitialized = true;
            ConsoleInitialized?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Writes text followed by a newline to the console.
        /// </summary>
        public void WriteLine(string text, Style style)
        {
            Write(text + Environment.NewLine, style);
        }

        /// <summary>
        /// Writes an empty line to the console.
        /// </summary>
        public void WriteLine()
        {
            Write(Environment.NewLine, Style.Out);
        }

        /// <summary>
        /// Gets the text that is yet to be processed from the console. This is the text that is being
        /// typed in by the user who has not yet pressed the enter key.
        /// </summary>
        private string GetCurrentLine()
        {
            string fullLine = GetLastTextEditorLine();
            return fullLine.Substring(_promptLength);
        }

        /// <summary>
        /// Gets the lines that have not been returned by the ReadLine method. This does not
        /// include the current line.
        /// </summary>
        public string[] GetUnreadLines()
        {
            return _previousLines.ToArray();
        }

        private string GetLastTextEditorLine()
        {
            return _textEditor.GetLine(_textEditor.TotalLines - 1);
        }

        private string ReadLineFromTextEditor()
        {
            int result = WaitHandle.WaitAny(_waitHandles);
            if (result != _lineReceivedEventIndex) return null;
            lock (_previousLines)
            {
                string line = _previousLines[0];
                _previousLines.RemoveAt(0);
                if (_previousLines.Count == 0)
                {
                    _lineReceivedEvent.Reset();
                }
                return line;
            }
        }

        /// <summary>
        /// Processes characters entered into the text editor by the user.
        /// </summary>
        private void textEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Delete:
                    if (!CanDelete) e.Handled = true;
                    return;
                case Key.Tab:
                    if (IsInReadOnlyRegion) e.Handled = true;
                    return;
                case Key.Back:
                    if (!CanBackspace) e.Handled = true;
                    return;
                case Key.Home:
                    MoveToHomePosition();
                    e.Handled = true;
                    return;
                case Key.Down:
                    if (!IsInReadOnlyRegion) MoveToNextCommandLine();
                    e.Handled = true;
                    return;
                case Key.Up:
                    if (!IsInReadOnlyRegion) MoveToPreviousCommandLine();
                    e.Handled = true;
                    return;
            }
        }

        /// <summary>
        /// Processes characters entering into the text editor by the user.
        /// </summary>
        private void textEditor_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0)
            {
                if (!char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_') // Underscore is a fairly common character in Revit API names.
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    _textEditor.RequestCompletionInsertion(e);
                }
            }

            if (IsInReadOnlyRegion)
            {
                e.Handled = true;
            }
            else
            {
                if (e.Text[0] == '\n')
                {
                    OnEnterKeyPressed();
                }

                if (e.Text[0] == '.' && _allowFullAutocompletion)
                {
                    _textEditor.ShowCompletionWindow();
                }

                if ((e.Text[0] == ' ') && (Keyboard.Modifiers == ModifierKeys.Control))
                {
                    e.Handled = true;
                    if (_allowCtrlSpaceAutocompletion) _textEditor.ShowCompletionWindow();
                }
            }
        }

        /// <summary>
        /// Move the cursor to the end of the line before retrieving the line.
        /// </summary>
        private void OnEnterKeyPressed()
        {
            _textEditor.StopCompletion();
            if (_textEditor.WriteInProgress) return;
            lock (_previousLines)
            {
                // Move the cursor to the end of the line.
                _textEditor.Column = GetLastTextEditorLine().Length + 1;

                // Append line.
                string currentLine = GetCurrentLine();
                _previousLines.Add(currentLine);
                _commandLineHistory.Add(currentLine);

                _lineReceivedEvent.Set();
            }
        }

        /// <summary>
        /// Returns true if the cursor is in a readonly text editor region.
        /// </summary>
        private bool IsInReadOnlyRegion => IsCurrentLineReadOnly || IsInPrompt;

        /// <summary>
        /// Only the last line in the text editor is not read only.
        /// </summary>
        private bool IsCurrentLineReadOnly => _textEditor.Line < _textEditor.TotalLines;

        /// <summary>
        /// Determines whether the current cursor position is in a prompt.
        /// </summary>
        private bool IsInPrompt => _textEditor.Column - _promptLength - 1 < 0;

        /// <summary>
        /// Returns true if the user can delete at the current cursor position.
        /// </summary>
        private bool CanDelete
        {
            get
            {
                if (_textEditor.SelectionLength > 0) return SelectionIsDeletable;
                else return !IsInReadOnlyRegion;
            }
        }

        /// <summary>
        /// Returns true if the user can backspace at the current cursor position.
        /// </summary>
        private bool CanBackspace
        {
            get
            {
                if (_textEditor.SelectionLength > 0) return SelectionIsDeletable;
                int cursorIndex = _textEditor.Column - _promptLength - 1;
                return !IsCurrentLineReadOnly && (cursorIndex > 0 || (cursorIndex == 0 && _textEditor.SelectionLength > 0));
            }
        }

        private bool SelectionIsDeletable =>
            !_textEditor.SelectionIsMultiline
            && !IsCurrentLineReadOnly
            && (_textEditor.SelectionStartColumn - _promptLength - 1 >= 0)
            && (_textEditor.SelectionEndColumn - _promptLength - 1 >= 0);

        /// <summary>
        /// The home position is at the start of the line after the prompt.
        /// </summary>
        private void MoveToHomePosition()
        {
            _textEditor.Line = _textEditor.TotalLines;
            _textEditor.Column = _promptLength + 1;
        }

        /// <summary>
        /// Shows the previous command line in the command line history.
        /// </summary>
        private void MoveToPreviousCommandLine()
        {
            if (_commandLineHistory.MovePrevious())
            {
                ReplaceCurrentLineTextAfterPrompt(_commandLineHistory.Current);
            }
        }

        /// <summary>
        /// Shows the next command line in the command line history.
        /// </summary>
        private void MoveToNextCommandLine()
        {
            _textEditor.Line = _textEditor.TotalLines;
            if (_commandLineHistory.MoveNext())
            {
                ReplaceCurrentLineTextAfterPrompt(_commandLineHistory.Current);
            }
        }

        /// <summary>
        /// Replaces the current line text after the prompt with the specified text.
        /// </summary>
        private void ReplaceCurrentLineTextAfterPrompt(string text)
        {
            string currentLine = GetCurrentLine();
            _textEditor.Replace(_promptLength, currentLine.Length, text);

            // Put cursor at end.
            _textEditor.Column = _promptLength + text.Length + 1;
        }
    }

    public class ErrorReporter : ErrorListener
    {
        public readonly List<String> Errors = [];

        public override void ErrorReported(ScriptSource source, string message, SourceSpan span, int errorCode, Severity severity)
        {
            Errors.Add($"{message} (line {span.Start.Line})");
        }

        public int Count => Errors.Count;
    }
}
