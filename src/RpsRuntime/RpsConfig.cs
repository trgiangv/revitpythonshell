using System.Xml.Linq;

namespace RpsRuntime
{
    /// <summary>
    /// Provides access functions to those parts of the RevitPythonShell.xml file
    /// that are also used in RpsAddin deployments.
    /// </summary>
    public class RpsConfig: IRpsConfig
    {
        private readonly XDocument _settings;
        private readonly SettingsDictionary _dict;

        public RpsConfig(string settingsFilePath)
        {
            var settingsPath = settingsFilePath;
            _settings = XDocument.Load(settingsPath);
            _dict = new SettingsDictionary(settingsPath);
        }

        /// <summary>
        /// Returns a list of search paths to be added to python interpreter engines.
        /// </summary>
        public IEnumerable<string> GetSearchPaths()
        {
            return _settings.Root?.Descendants("SearchPath").Select(searchPathNode => searchPathNode.Attribute("name")?.Value);
        }

        /// <summary>
        /// Returns the list of variables to be included with the scope in RevitPythonShell scripts.
        /// </summary>
        public IDictionary<string, string> GetVariables()
        {
            return _dict;
        }               
    }
}
