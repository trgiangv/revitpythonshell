using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using RevitPythonShell.Views;

namespace RevitPythonShell.RevitCommands
{
    /// <summary>
    /// Open the configuration dialog.
    /// </summary>
    [Regeneration(RegenerationOption.Manual)]
    [Transaction(TransactionMode.Manual)]
    internal class ConfigureCommand: IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var dialog = new ConfigureCommandsForm();
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.ShowDialog();

            return Result.Succeeded;
        }
    }
}
