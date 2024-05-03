// Copyright (c) 2010 Joe Moorhouse

using System.ComponentModel;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using PythonConsoleControl;

namespace RevitPythonShell.Views
{
    public class ConsoleOptions(PythonConsolePad pad)
    {
        private readonly TextEditor _textEditor = pad.Control;

        [DefaultValue(false)]
        public bool ShowSpaces
        {
            get => _textEditor.TextArea.Options.ShowSpaces;
            set => _textEditor.TextArea.Options.ShowSpaces = value;
        }

        [DefaultValue(false)]
        public bool ShowTabs
        {
            get => _textEditor.TextArea.Options.ShowTabs;
            set => _textEditor.TextArea.Options.ShowTabs = value;
        }

        [DefaultValue(false)]
        public bool AllowScrollBelowDocument
        {
            get => _textEditor.TextArea.Options.AllowScrollBelowDocument;
            set => _textEditor.TextArea.Options.AllowScrollBelowDocument = value;
        }

        [DefaultValue("Consolas")]
        public string FontFamily
        {
            get => _textEditor.TextArea.FontFamily.ToString();
            set => _textEditor.TextArea.FontFamily = new FontFamily(value);
        }

        [DefaultValue(12.0)]
        public double FontSize
        {
            get => _textEditor.TextArea.FontSize;
            set => _textEditor.TextArea.FontSize = value;
        }

        [DefaultValue(false)]
        public bool FullAutocompletion
        {
            get => pad.Console.AllowFullAutocompletion;
            set => pad.Console.AllowFullAutocompletion = value;
        }

        [DefaultValue(true)]
        public bool CtrlSpaceAutocompletion
        {
            get => pad.Console.AllowCtrlSpaceAutocompletion;
            set => pad.Console.AllowCtrlSpaceAutocompletion = value;
        }
    }
}
