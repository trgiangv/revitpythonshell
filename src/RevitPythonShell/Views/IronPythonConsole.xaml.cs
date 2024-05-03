using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;

namespace RevitPythonShell.Views
{
    /// <summary>
    /// Interaction logic for IronPythonConsole.xaml
    /// </summary>
    public partial class IronPythonConsole : Window
    {
        // this is the name of the file currently being edited in the pad
        private string _currentFileName;

        public IronPythonConsole()
        {
            Initialized += MainWindow_Initialized;

            IHighlightingDefinition pythonHighlighting;
            using (Stream s = typeof(IronPythonConsole).Assembly.GetManifestResourceStream("RevitPythonShell.Resources.Python.xshd"))
            {
                if (s == null)
                    throw new InvalidOperationException("Could not find embedded resource");
                using (XmlReader reader = new XmlTextReader(s))
                {
                    pythonHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.
                        HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            // and register it in the HighlightingManager
            HighlightingManager.Instance.RegisterHighlighting("Python Highlighting", [".cool"], pythonHighlighting);

            InitializeComponent();

            TextEditor.SyntaxHighlighting = pythonHighlighting;
            TextEditor.PreviewKeyDown += textEditor_PreviewKeyDown;
            new ConsoleOptions(ConsoleControl.Pad);

            // get application version and show in title
            Title = "RevitPythonShell";
        }

        private void MainWindow_Initialized(object sender, EventArgs e)
        {
            //propertyGridComboBox.SelectedIndex = 1;
            TextEditor.ShowLineNumbers = true;
        }
        private void NewFileClick(object sender, RoutedEventArgs e)
        {
            _currentFileName = null;
            TextEditor.Text = string.Empty;
        }
        private void OpenFileClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                CheckFileExists = true
            };
            if (!(dlg.ShowDialog() ?? false)) return;
            _currentFileName = dlg.FileName;
            TextEditor.Load(_currentFileName);
        }
        private void SaveAsFileClick(object sender, EventArgs e)
        {
            _currentFileName = null;
            SaveFile();
        }
        private void SaveFileClick(object sender, EventArgs e)
        {
           SaveFile();
        }
        private void SaveFile()
        {
            if (_currentFileName == null)
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "Save Files (*.py)|*.py";
                dlg.DefaultExt = "py";
                dlg.AddExtension = true;
                if (dlg.ShowDialog() ?? false)
                {
                    _currentFileName = dlg.FileName;
                }
                else
                {
                    return;
                }
            }
            TextEditor.Save(_currentFileName);
        }

        private void RunClick(object sender, EventArgs e)
        {
            RunStatements();
        }

        private void textEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F5:
                    RunStatements();
                    break;
                case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                    SaveFile();
                    break;
                case Key.S when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                    SaveAsFileClick(sender, e);
                    break;
                case Key.O when Keyboard.Modifiers == ModifierKeys.Control:
                    OpenFileClick(sender, e);
                    break;
                case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                    NewFileClick(sender, e);
                    break;
                case Key.F4 when Keyboard.Modifiers == ModifierKeys.Control:
                    Close();
                    break;   
            }
        }

        private void RunStatements()
        {
            var statementsToRun = TextEditor.TextArea.Selection.Length > 0 ? TextEditor.TextArea.Selection.GetText() : TextEditor.TextArea.Document.Text;
            ConsoleControl.Pad.Console.RunStatements(statementsToRun);
        }

        // Clear the contents on first click inside the editor
        private void textEditor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_currentFileName != null) return;
            TextEditor tb = (TextEditor)sender;
            tb.Text = string.Empty;
            // Remove the handler from the list otherwise this handler will clear
            // editor contents every time the editor gains focus.
            tb.GotFocus -= textEditor_GotFocus;
        }

    }
}