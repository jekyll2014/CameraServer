// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CameraServer
{
    public class Config<T> where T : class, new()
    {
        public string ConfigFileName { get; set; }
        public bool AutoCreateConfig { get; set; } = false;

        public T ConfigStorage { get; set; } = new T();

        public Config()
        {
            ConfigFileName = string.Empty;
        }

        public Config(string file, bool autoCreateConfig = false)
        {
            ConfigFileName = file;
            AutoCreateConfig = autoCreateConfig;

            LoadConfig();
        }

        public bool LoadConfig()
        {
            if (string.IsNullOrEmpty(ConfigFileName))
                return false;

            try
            {
                if (File.Exists(ConfigFileName))
                {
                    var json = JToken.Parse(File.ReadAllText(ConfigFileName));
                    ConfigStorage = GetSection<T>(json, "");
                }
                else if (AutoCreateConfig)
                {
                    this.SaveConfig();
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private TK GetSection<TK>(JToken json, string sectionName = "") where TK : class, new()
        {
            if (string.IsNullOrEmpty(ConfigFileName))
                return new TK();

            if (string.IsNullOrEmpty(sectionName))
                return json?.ToObject<TK>() ??
                       throw new InvalidOperationException($"Cannot find section {sectionName}");

            return json[sectionName]?.ToObject<TK>() ??
                   throw new InvalidOperationException($"Cannot find section {sectionName}");
        }

        public bool SaveConfig(string fileName)
        {
            ConfigFileName = fileName;
            return SaveConfig();
        }

        public bool SaveConfig()
        {
            if (string.IsNullOrEmpty(ConfigFileName))
                return false;

            try
            {
                File.WriteAllText(ConfigFileName,
                    JsonConvert.SerializeObject(ConfigStorage, Formatting.Indented, new Newtonsoft.Json.JsonSerializerSettings()
                    {
                        Converters = new List<Newtonsoft.Json.JsonConverter>
                        {
                            new Newtonsoft.Json.Converters.StringEnumConverter()
                        }
                    }));
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
