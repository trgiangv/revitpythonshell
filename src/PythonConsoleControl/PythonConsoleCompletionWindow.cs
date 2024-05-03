// Copyright (c) 2010 Joe Moorhouse

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Reflection;

namespace PythonConsoleControl
{
    public delegate void DescriptionUpdateDelegate(string description);
    
    /// <summary>
    /// The code completion window.
    /// </summary>
    public class PythonConsoleCompletionWindow : CompletionWindowBase
    {
        private readonly CompletionList _completionList = new();
        private ToolTip _toolTip = new();
        private readonly DispatcherTimer _updateDescription;
        private readonly TimeSpan _updateDescriptionInterval;
        private readonly PythonTextEditor _textEditor;
        private readonly PythonConsoleCompletionDataProvider _completionDataProvider;

        /// <summary>
        /// Gets the completion list used in this completion window.
        /// </summary>
        public CompletionList CompletionList => _completionList;

        /// <summary>
        /// Creates a new code completion window.
        /// </summary>
        public PythonConsoleCompletionWindow(TextArea textArea, PythonTextEditor textEditor, bool closeWhenCaretAtBeginning)
            : base(textArea)
        {
            // keep height automatic
            _completionDataProvider = textEditor.CompletionProvider;
            _textEditor = textEditor;
            CloseWhenCaretAtBeginning = closeWhenCaretAtBeginning;
            CloseAutomatically = true;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 300;
            Width = 175;
            Content = _completionList;
            // prevent user from resizing a window to 0x0
            MinHeight = 15;
            MinWidth = 30;

            _toolTip.PlacementTarget = this;
            _toolTip.Placement = PlacementMode.Right;
            _toolTip.Closed += toolTip_Closed;

            _completionList.InsertionRequested += completionList_InsertionRequested;
            _completionList.SelectionChanged += completionList_SelectionChanged;
            AttachEvents();

            _updateDescription = new DispatcherTimer();
            _updateDescription.Tick += completionList_UpdateDescription;
            _updateDescriptionInterval = TimeSpan.FromSeconds(0.3);

            EventInfo eventInfo = typeof(TextView).GetEvent("ScrollOffsetChanged");
            Delegate methodDelegate = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, "TextViewScrollOffsetChanged");
            eventInfo.RemoveEventHandler(TextArea.TextView, methodDelegate);
        }

        #region ToolTip handling

        private void toolTip_Closed(object sender, RoutedEventArgs e)
        {
            // Clear content after the tooltip is closed.
            // We cannot clear is immediately when setting IsOpen=false
            // because the tooltip uses an animation for closing.
            if (_toolTip != null)
                _toolTip.Content = null;
        }

        private void completionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _completionList.SelectedItem;
            if (item == null)
            {
                _updateDescription.Stop();
            }
            else
            {
                _updateDescription.Interval = _updateDescriptionInterval;
                _updateDescription.Start();
            }
        }

        private void completionList_UpdateDescription(Object sender, EventArgs e)
        {
            _updateDescription.Stop();
            _textEditor.UpdateCompletionDescription();
        }

        /// <summary>
        /// Update the description of the current item. This is typically called from a separate thread from the main UI thread.
        /// </summary>
        internal void UpdateCurrentItemDescription()
        {
            if (_textEditor.StopCompletion())
            {
                _updateDescription.Interval = _updateDescriptionInterval;
                _updateDescription.Start();
                return;
            }
            string stub = "";
            string item = "";
            bool isInstance = false;
            _textEditor.TextEditor.Dispatcher.Invoke(delegate()
            {
                PythonCompletionData data = (_completionList.SelectedItem as PythonCompletionData);
                if (data == null || _toolTip == null)
                    return;
                stub = data.Stub;
                item = data.Text;
                isInstance = data.IsInstance;
            });
            // Send to the completion thread to generate the description, providing callback.
            _completionDataProvider.GenerateDescription(stub, item, completionList_WriteDescription, isInstance);
        }

        private void completionList_WriteDescription(string description)
        {
            _textEditor.TextEditor.Dispatcher.Invoke(delegate
            {
                if (_toolTip == null) return;
                if (description != null)
                {
                    _toolTip.Content = description;
                    _toolTip.IsOpen = true;
                }
                else
                {
                    _toolTip.IsOpen = false;
                }
            });
        }

        #endregion

        private void completionList_InsertionRequested(object sender, EventArgs e)
        {
            Close();
            // The window must close before Complete() is called.
            // If the Complete callback pushes stacked input handlers, we don't want to pop those when the CC window closes.
            var item = _completionList.SelectedItem;
            if (item != null)
                item.Complete(TextArea, new AnchorSegment(TextArea.Document, StartOffset, EndOffset - StartOffset), e);
        }

        private void AttachEvents()
        {
            TextArea.Caret.PositionChanged += CaretPositionChanged;
            TextArea.MouseWheel += textArea_MouseWheel;
            TextArea.PreviewTextInput += textArea_PreviewTextInput;
        }

        /// <inheritdoc/>
        protected override void DetachEvents()
        {
            TextArea.Caret.PositionChanged -= CaretPositionChanged;
            TextArea.MouseWheel -= textArea_MouseWheel;
            TextArea.PreviewTextInput -= textArea_PreviewTextInput;
            base.DetachEvents();
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_toolTip != null)
            {
                _toolTip.IsOpen = false;
                _toolTip = null;
            }
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled)
            {
                _completionList.HandleKey(e);
            }
        }

        private void textArea_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = RaiseEventPair(this, PreviewTextInputEvent, TextInputEvent,
                                       new TextCompositionEventArgs(e.Device, e.TextComposition));
        }

        private void textArea_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = RaiseEventPair(GetScrollEventTarget(),
                                       PreviewMouseWheelEvent, MouseWheelEvent,
                                       new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta));
        }

        private UIElement GetScrollEventTarget()
        {
            if (_completionList == null)
                return this;
            return _completionList.ScrollViewer ?? _completionList.ListBox ?? (UIElement)_completionList;
        }

        /// <summary>
        /// Gets/Sets whether the completion window should close automatically.
        /// The default value is true.
        /// </summary>
        private bool CloseAutomatically { get; }

        /// <inheritdoc/>
        protected override bool CloseOnFocusLost => CloseAutomatically;

        /// <summary>
        /// When this flag is set, code completion closes if the caret moves to the
        /// beginning of the allowed range. This is useful in Ctrl+Space and "complete when typing",
        /// but not in dot-completion.
        /// It Has no effect if CloseAutomatically is false.
        /// </summary>
        private bool CloseWhenCaretAtBeginning { get; }

        private void CaretPositionChanged(object sender, EventArgs e)
        {
            int offset = TextArea.Caret.Offset;
            if (offset == StartOffset)
            {
                if (CloseAutomatically && CloseWhenCaretAtBeginning)
                {
                    Close();
                }
                else
                {
                    _completionList.SelectItem(string.Empty);
                }
                return;
            }
            if (offset < StartOffset || offset > EndOffset)
            {
                if (CloseAutomatically)
                {
                    Close();
                }
            }
            else
            {
                TextDocument document = TextArea.Document;
                if (document != null)
                {
                    _completionList.SelectItem(document.GetText(StartOffset, offset - StartOffset));
                }
            }
        }
    }
}
