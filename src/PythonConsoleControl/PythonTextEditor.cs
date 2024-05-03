// Copyright (c) 2010 Joe Moorhouse

using System.Text;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Diagnostics;

namespace PythonConsoleControl
{
    /// <summary>
    /// Interface console to AvalonEdit and handle autocompletion.
    /// </summary>
    public class PythonTextEditor
    {
        internal readonly TextEditor TextEditor;
        internal readonly TextArea TextArea;
        private readonly StringBuilder _writeBuffer = new();
        private volatile bool _writeInProgress;
        private PythonConsoleCompletionWindow _completionWindow;
        private readonly int _completionEventIndex = 0;
        private readonly int _descriptionEventIndex = 1;
        private readonly WaitHandle[] _completionWaitHandles;
        private readonly AutoResetEvent _completionRequestedEvent = new(false);
        private readonly AutoResetEvent _descriptionRequestedEvent = new(false);
        private readonly Thread _completionThread;
        private PythonConsoleCompletionDataProvider _completionProvider;
        private Action<Action> _completionDispatcher = (command) => command(); // dummy completion dispatcher

        public PythonTextEditor(TextEditor textEditor)
        {
            TextEditor = textEditor;
            TextArea = textEditor.TextArea;
            _completionWaitHandles = [_completionRequestedEvent, _descriptionRequestedEvent];
            _completionThread = new Thread(Completion);
            _completionThread.Priority = ThreadPriority.Lowest;
            _completionThread.IsBackground = true;
            _completionThread.Start();
        }

        /// <summary>
        /// Set the dispatcher to use to force code completion to happen on a specific thread
        /// if necessary.
        /// </summary>
        public void SetCompletionDispatcher(Action<Action> newDispatcher)
        {
            _completionDispatcher = newDispatcher;
        }

        public bool WriteInProgress => _writeInProgress;

        public void Write(string text, bool allowSynchronous = false, bool moveToEnd = true)
        {
            //text = text.Replace("\r\r\n", "\r\n");
            text = text.Replace("\r\r\n", "\r");
            text = text.Replace("\r\n", "\r");
            if (allowSynchronous)
            {
                if (moveToEnd)
                {
                    MoveToEnd();
                }
                PerformTextInput(text);
                return;
            }
            lock (_writeBuffer)
            {
                _writeBuffer.Append(text);
            }
            if (!_writeInProgress)
            {
                _writeInProgress = true;
                ThreadPool.QueueUserWorkItem(CheckAndOutputWriteBuffer, moveToEnd);
                Stopwatch.StartNew();
            }
        }

        private void CheckAndOutputWriteBuffer(Object stateInfo)
        {
            bool moveToEnd = (bool)stateInfo;

            AutoResetEvent writeCompletedEvent = new AutoResetEvent(false);
            Action action = delegate
            {
                string toWrite;
                lock (_writeBuffer)
                {
                    toWrite = _writeBuffer.ToString();
                    _writeBuffer.Remove(0, _writeBuffer.Length);
                    //writeBuffer.Clear();
                }
                if (moveToEnd)
                {
                    MoveToEnd();
                }
                PerformTextInput(toWrite);
                writeCompletedEvent.Set();
            };

            while (true)
            {
                // Clear writeBuffer and write out.
                TextArea.Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
                // Check if writeBuffer has refilled in the meantime; if so clear and write out again.
                writeCompletedEvent.WaitOne();
                lock (_writeBuffer)
                {
                    if (_writeBuffer.Length != 0) continue;
                    _writeInProgress = false;
                    break;
                }
            }
        }

        private void MoveToEnd()
        {
            int lineCount = TextArea.Document.LineCount;
            if (TextArea.Caret.Line != lineCount) TextArea.Caret.Line = TextArea.Document.LineCount;
            int column = TextArea.Document.Lines[lineCount - 1].Length + 1;
            if (TextArea.Caret.Column != column) TextArea.Caret.Column = column;
        }

        private void PerformTextInput(string text)
        {
            if (text == "\n" || text == "\r\n")
            {
                string newLine = TextUtilities.GetNewLineFromDocument(TextArea.Document, TextArea.Caret.Line);
                using (TextArea.Document.RunUpdate())
                {
                    TextArea.Selection.ReplaceSelectionWithText(newLine);
                }
            }
            else
                TextArea.Selection.ReplaceSelectionWithText(text);
            TextArea.Caret.BringCaretToView();
        }

        public int Column
        {
            get => TextArea.Caret.Column;
            set => TextArea.Caret.Column = value;
        }

        /// <summary>
        /// Gets the current cursor line.
        /// </summary>
        public int Line
        {
            get => TextArea.Caret.Line;
            set => TextArea.Caret.Line = value;
        }

        /// <summary>
        /// Gets the total number of lines in the text editor.
        /// </summary>
        public int TotalLines => TextArea.Document.LineCount;

        private delegate string StringAction();
        /// <summary>
        /// Gets the text for the specified line.
        /// </summary>
        public string GetLine(int index)
        {
            return (string)TextArea.Dispatcher.Invoke(new StringAction(delegate
            {
                DocumentLine line = TextArea.Document.Lines[index];
                return TextArea.Document.GetText(line);
            }));
        }

        /// <summary>
        /// Replaces the text at the specified index on the current line with the specified text.
        /// </summary>
        public void Replace(int index, int length, string text)
        {
            //int currentLine = textArea.Caret.Line - 1;
            int currentLine = TextArea.Document.LineCount - 1;
            int startOffset = TextArea.Document.Lines[currentLine].Offset;
            TextArea.Document.Replace(startOffset + index, length, text); 
        }

        public event TextCompositionEventHandler TextEntering
        {
            add => TextArea.TextEntering += value;
            remove => TextArea.TextEntering -= value;
        }

        public event TextCompositionEventHandler TextEntered
        {
            add => TextArea.TextEntered += value;
            remove => TextArea.TextEntered -= value;
        }

        public event KeyEventHandler PreviewKeyDown
        {
            add => TextArea.PreviewKeyDown += value;
            remove => TextArea.PreviewKeyDown -= value;
        }

        public int SelectionLength => TextArea.Selection.Length;

        public bool SelectionIsMultiline => TextArea.Selection.IsMultiline;

        public int SelectionStartColumn
        {
            get
            {
                int startOffset = TextArea.Selection.SurroundingSegment.Offset;
                return startOffset - TextArea.Document.GetLineByOffset(startOffset).Offset + 1;
            }
        }

        public int SelectionEndColumn
        {
            get
            {
                int endOffset = TextArea.Selection.SurroundingSegment.EndOffset;
                return endOffset - TextArea.Document.GetLineByOffset(endOffset).Offset + 1;
            }
        }

        public PythonConsoleCompletionDataProvider CompletionProvider
        {
            get => _completionProvider;
            set => _completionProvider = value;
        }

        public bool StopCompletion()
        {
            if (!_completionProvider.AutocompletionInProgress) return false;
            // send Ctrl-C abort
            _completionThread.Abort(new Microsoft.Scripting.KeyboardInterruptException(""));
            return true;
        }

        public void ShowCompletionWindow()
        {
            _completionRequestedEvent.Set();
        }

        public void UpdateCompletionDescription()
        {
            _descriptionRequestedEvent.Set();
        }

        /// <summary>
        /// Perform completion actions on the background completion thread.
        /// </summary>
        private void Completion()
        {
            int action = WaitHandle.WaitAny(_completionWaitHandles);
            if (action == _completionEventIndex && _completionProvider != null) BackgroundShowCompletionWindow();
            if (action == _descriptionEventIndex && _completionProvider != null && _completionWindow != null) BackgroundUpdateCompletionDescription();
        }

        /// <summary>
        /// Obtain completions (this runs in its own thread)
        /// </summary>
        private void BackgroundShowCompletionWindow() //ICompletionItemProvider
        {
			// provide AvalonEdit with the data:
            string itemForCompletion = "";
            TextArea.Dispatcher.Invoke(delegate
            {
                DocumentLine line = TextArea.Document.Lines[TextArea.Caret.Line - 1];
                itemForCompletion = TextArea.Document.GetText(line.Offset, TextArea.Caret.Column - 1);
            });

            
            _completionDispatcher.Invoke(delegate
            {
                try
                {
                    var completionInfo = _completionProvider.GenerateCompletionData(itemForCompletion);

                    if (completionInfo == null) return;
                    ICompletionData[] completions = completionInfo.Item1;
                    string memberName = completionInfo.Item3;

                    if (completions.Length > 0) TextArea.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _completionWindow = new PythonConsoleCompletionWindow(TextArea, this, true);
                        IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
                        foreach (ICompletionData completion in completions)
                        {
                            data.Add(completion);
                        }
                        _completionWindow.Show();
                        _completionWindow.Closed += delegate
                        {
                            _completionWindow = null;
                        };

                        _completionWindow.StartOffset -= memberName.Length;
                        _completionWindow.CompletionList.SelectItem(TextArea.Document.GetText(_completionWindow.StartOffset, memberName.Length));
                    }));
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.ToString(), "Error");
                }

            });            
        }

        private void BackgroundUpdateCompletionDescription()
        {
            _completionDispatcher.Invoke(delegate
            {
                try
                {
                    _completionWindow.UpdateCurrentItemDescription();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.ToString(), "Error");
                }

            });            
        }

        public void RequestCompletionInsertion(TextCompositionEventArgs e)
        {
            if (_completionWindow != null) _completionWindow.CompletionList.RequestInsertion(e);
            // if autocompletion still in progresses, terminate
            StopCompletion();
        }

    }
}

    
   
