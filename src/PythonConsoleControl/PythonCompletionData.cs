// Copyright (c) 2010 Joe Moorhouse

using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.Scripting.Hosting.Shell;

namespace PythonConsoleControl
{
    /// <summary>
    /// Implements AvalonEdit ICompletionData interface to provide the entries in the completion drop down.
    /// </summary>
    public class PythonCompletionData(string text, string stub, CommandLine commandLine, bool isInstance)
        : ICompletionData
    {
        public System.Windows.Media.ImageSource Image => null;

        public string Text { get; private set; } = text;

        public string Stub { get; private set; } = stub;

        public bool IsInstance { get; private set; } = isInstance;

        // Use this property if you want to show a fancy UIElement in the drop down list.
        public object Content => Text;

        public object Description =>
            // Do nothing: description now updated externally and asynchronously.
            "Not available";

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}