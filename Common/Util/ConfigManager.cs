using HyacineCore.Server.Configuration;
using HyacineCore.Server.Internationalization;
using Newtonsoft.Json;

namespace HyacineCore.Server.Util;

public static class ConfigManager
{
    public static readonly Logger Logger = new("ConfigManager");
    private static readonly string ConfigFilePath = Path.Combine("Config", "ServerConfig.json");
    private static string HotfixFilePath => Config.Path.ConfigPath + "/Hotfix.json";
    public static ConfigContainer Config { get; private set; } = new();
    public static HotfixContainer Hotfix { get; private set; } = new();

    public static void LoadConfig()
    {
        LoadConfigData();
        LoadHotfixData();
    }

    private static void LoadConfigData()
    {
        var file = new FileInfo(ConfigFilePath);
        if (!file.Exists)
        {
            var legacyFile = new FileInfo("Config.json");
            if (legacyFile.Exists)
            {
                if (file.Directory is { Exists: false }) file.Directory.Create();
                legacyFile.MoveTo(file.FullName, true);
                file.Refresh();
            }
        }

        if (!file.Exists)
        {
            Config = new ConfigContainer
            {
                MuipServer =
                {
                    AdminKey = Guid.NewGuid().ToString()
                },
                ServerOption =
                {
                    Language = UtilTools.GetCurrentLanguage()
                }
            };

            Logger.Info("Current Language is " + Config.ServerOption.Language);
            Logger.Info("Muipserver Admin key: " + Config.MuipServer.AdminKey);
            SaveData(Config, ConfigFilePath);
        }

        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            Config = JsonConvert.DeserializeObject<ConfigContainer>(json, new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            }) ?? throw new InvalidDataException($"Empty configuration: {ConfigFilePath}");
        }

        GameConstants.ApplyChallengePeakConfig(Config.ServerOption.ChallengePeak);
        SaveData(Config, ConfigFilePath);
    }

    private static void LoadHotfixData()
    {
        var file = new FileInfo(HotfixFilePath);

        // Generate all necessary versions
        var verList = new List<string>();
        var prefix = new List<string> { "CN", "OS" };
        foreach (var pre in prefix)
            if (GameConstants.GAME_VERSION[^1] == '5')
                for (var i = 1; i < 6; i++)
                    verList.Add(pre + GameConstants.GAME_VERSION + i);
            else
                verList.Add(pre + GameConstants.GAME_VERSION);

        if (!file.Exists)
        {
            Hotfix = new HotfixContainer();
            SaveData(Hotfix, HotfixFilePath);
            file.Refresh();
        }

        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            Hotfix = JsonConvert.DeserializeObject<HotfixContainer>(json) ?? throw new InvalidDataException($"Empty configuration: {HotfixFilePath}");
        }

        foreach (var version in verList)
            if (!Hotfix.HotfixData.TryGetValue(version, out _))
                Hotfix.HotfixData[version] = new DownloadUrlConfig();

        Logger.Info(I18NManager.Translate("Server.ServerInfo.CurrentVersion", GameConstants.GAME_VERSION));

        SaveData(Hotfix, HotfixFilePath);
    }

    private static void SaveData(object data, string path)
    {
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        var file = new FileInfo(path);
        if (file.Directory is { Exists: false }) file.Directory.Create();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException) when (attempt < 50) { Thread.Sleep(100); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void InitDirectories()
    {
        foreach (var property in Config.Path.GetType().GetProperties())
        {
            var dir = property.GetValue(Config.Path)?.ToString();

            if (!string.IsNullOrEmpty(dir))
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
        }
    }
}
