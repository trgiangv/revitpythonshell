// Copyright (c) 2010 Joe Moorhouse

using ICSharpCode.AvalonEdit;
using System.Windows.Media;

namespace PythonConsoleControl
{   
    public class PythonConsolePad 
    {
        private readonly TextEditor _textEditor;
        private readonly PythonConsoleHost _host;

        public PythonConsolePad()
        {
            _textEditor = new TextEditor();
            var pythonTextEditor = new PythonTextEditor(_textEditor);
            _host = new PythonConsoleHost(pythonTextEditor);
            _host.Run();
            _textEditor.FontFamily = new FontFamily("Consolas");
            _textEditor.FontSize = 12;
        }

        public TextEditor Control => _textEditor;

        public PythonConsoleHost Host => _host;

        public PythonConsole Console => _host.Console;

        public void Dispose()
        {
            _host.Dispose();
        }
    }
}
