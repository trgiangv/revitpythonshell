using System.Xml.Linq;

namespace RpsRuntime
{
    /// <summary>
    /// SettingsDictionary is a dictionary backed by an XML file.
    /// </summary>
    public class SettingsDictionary : IDictionary<string, string>
    {
        private readonly IDictionary<string, string> _dict;
        private readonly string _settingsPath;
        private readonly XDocument _settings;

        public SettingsDictionary(string settingsPath)
        {
            _settingsPath = settingsPath;
            _settings = XDocument.Load(_settingsPath);

            _dict = _settings.Root?.Descendants("StringVariable").ToDictionary(
                v => v.Attribute("name")?.Value,
                v => v.Attribute("value")?.Value);
        }

        private void SetVariable(string name, string value)
        {
            var variable = ((_settings.Root?.Descendants("StringVariable")) ?? Array.Empty<XElement>()).FirstOrDefault(x => x.Attribute("name")?.Value == name);
            if (variable != null)
            {
                variable.Attribute("value")!.Value = value.ToString();
            }
            else
            {
                _settings.Root?.Descendants("Variables").First().Add(
                    new XElement("StringVariable", new XAttribute("name", name), new XAttribute("value", value)));
            }
            _settings.Save(_settingsPath);
        }

        private void RemoveVariable(string name)
        {
            var variable = ((_settings.Root?.Descendants("StringVariable")) ?? Array.Empty<XElement>()).FirstOrDefault(x => x.Attribute("name")?.Value == name);
            if (variable == null) return;
            variable.Remove();
            _settings.Save(_settingsPath);
        }

        private void ClearVariables()
        {
            var variables = _settings.Root?.Descendants("StringVariable");
            foreach (var variable in variables!)
            {
                variable.Remove();
            }
            _settings.Save(_settingsPath);
        }

        public void Add(string key, string value)
        {
            _dict.Add(key, value);
            SetVariable(key, value);
        }

        public bool ContainsKey(string key)
        {
            return _dict.ContainsKey(key);
        }

        public ICollection<string> Keys => _dict.Keys;

        public bool Remove(string key)
        {
            RemoveVariable(key);
            return _dict.Remove(key);            
        }

        public bool TryGetValue(string key, out string value)
        {
            return _dict.TryGetValue(key, out value);
        }

        public ICollection<string> Values => _dict.Values;

        public string this[string key]
        {
            get => _dict[key];
            set
            {
                _dict[key] = value;
                SetVariable(key, value);
            }
        }

        public void Add(KeyValuePair<string, string> item)
        {
            _dict.Add(item);
            SetVariable(item.Key, item.Value);
        }

        public void Clear()
        {
            ClearVariables();
            _dict.Clear();            
        }

        public bool Contains(KeyValuePair<string, string> item)
        {
            return _dict.Contains(item);
        }

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            _dict.CopyTo(array, arrayIndex);
        }

        public int Count => _dict.Count;

        public bool IsReadOnly => false;

        public bool Remove(KeyValuePair<string, string> item)
        {
            RemoveVariable(item.Key);
            return _dict.Remove(item);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return _dict.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _dict.GetEnumerator();
        }
    } 
}
