using HyacineCore.Server.Command;
using HyacineCore.Server.Command.Command;
using HyacineCore.Server.Configuration;
using HyacineCore.Server.Data;
using HyacineCore.Server.Database;
using HyacineCore.Server.Database.Avatar;
using HyacineCore.Server.Enums;
using HyacineCore.Server.GameServer.Command;
using HyacineCore.Server.GameServer.Game.Challenge.Definitions;
using HyacineCore.Server.GameServer.Plugin;
using HyacineCore.Server.GameServer.Server;
using HyacineCore.Server.GameServer.Server.Packet;
using HyacineCore.Server.Internationalization;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Program.Handbook;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer;
using HyacineCore.Server.WebServer.Server;
using System.Globalization;
using System.IO.Compression;

namespace HyacineCore.Server.Program.Program;

public class EntryPoint
{
    private static readonly Logger Logger = new("Program");
    public static readonly DatabaseHelper DatabaseHelper = new();
    public static readonly Listener Listener = new();
    public static readonly CommandManager CommandManager = new();

    private static void WaitForInteractiveFailure()
    {
        Environment.ExitCode = 1;
        if (IConsole.IsConsoleAvailable) Console.ReadLine();
    }

    public static async Task Main(string[] args)
    {
        IConsole.InitConsole();
        IConsole.RedrawInput(IConsole.Input);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            PerformCleanup();
            global::HyacineCore.Server.Util.Logger.Flush();
        };
        AppDomain.CurrentDomain.UnhandledException += (obj, arg) =>
        {
            Logger.Error(I18NManager.Translate("Server.ServerInfo.UnhandledException", obj.GetType().Name),
                (Exception)arg.ExceptionObject);
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            PerformCleanup();
            Environment.Exit(1);
        };

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.CancelKeyPressed"));
            eventArgs.Cancel = true;
            Environment.Exit(0);
        };
        // A hidden server has no console to press Ctrl+C in; scripts ask for a clean shutdown (players saved)
        // by creating this file in the working directory.
        var shutdownRequest = Path.Combine(Environment.CurrentDirectory, "shutdown.request");
        _ = Task.Run(async () =>
        {
            while (!File.Exists(shutdownRequest)) await Task.Delay(1000);
            File.Delete(shutdownRequest);
            Logger.Info("Shutdown requested");
            Environment.Exit(0);
        });
        var time = DateTime.Now;

        // pack the old log
        var logDirectory = new DirectoryInfo(GetConfig().Path.LogPath);
        if (logDirectory.Exists)
        {
            List<string> packed = [];
            foreach (var oldFile in logDirectory.GetFiles().ToArray())
            {
                if (!oldFile.Name.EndsWith(".log")) continue;
                if (oldFile.Name.EndsWith("-debug.log")) continue;
                if (packed.Contains(oldFile.Name)) continue;

                var fileName = oldFile.Name.Replace(".log", "");
                var debugFileName = fileName + "-debug";
                var oldDebugFile = logDirectory.GetFiles(debugFileName + ".log").FirstOrDefault();

                if (oldFile.Exists)
                {
                    var zipFileName = fileName + ".zip";
                    var zipFile = new FileInfo(GetConfig().Path.LogPath + $"/{zipFileName}");
                    if (zipFile.Exists) zipFile.Delete();
                    using (var zip = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(oldFile.FullName, oldFile.Name);
                        if (oldDebugFile is { Exists: true })
                            zip.CreateEntryFromFile(oldDebugFile.FullName, oldDebugFile.Name);
                    }

                    oldFile.Delete();
                    oldDebugFile?.Delete();
                    packed.Add(oldFile.Name);
                    packed.Add(oldDebugFile?.Name ?? "");
                }
            }
        }

        // Initialize the logfile
        var counter = 0;
        FileInfo file;
        FileInfo zi;
        while (true)
        {
            file = new FileInfo(GetConfig().Path.LogPath + $"/{DateTime.Now:yyyy-MM-dd}-{++counter}.log");
            zi = new FileInfo(GetConfig().Path.LogPath + $"/{DateTime.Now:yyyy-MM-dd}-{counter}.zip");
            if (file is not { Exists: false, Directory: not null }) continue;
            if (zi is not { Exists: false, Directory: not null }) continue;
            file.Directory.Create();
            break;
        }

        var debugFile = new FileInfo(GetConfig().Path.LogPath + $"/{DateTime.Now:yyyy-MM-dd}-{counter}-debug.log");

        Logger.SetLogFile(file);
        Logger.SetDebugLogFile(debugFile);
        // Starting the server
        Logger.Info(I18NManager.Translate("Server.ServerInfo.StartingServer"));

        // Load the config
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Config")));
        try
        {
            ConfigManager.LoadConfig();
            Directory.CreateDirectory(ConfigManager.Config.Path.GameDataPath);
            Directory.CreateDirectory(ConfigManager.Config.Path.KeyPath);
            Directory.CreateDirectory(Path.Combine(ConfigManager.Config.Path.ConfigPath, "Json"));
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Config")), e);
            WaitForInteractiveFailure();
            return;
        }

        // Load the language
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Language")));
        try
        {
            I18NManager.LoadLanguage();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Language")), e);
            WaitForInteractiveFailure();
            return;
        }

        // Initialize the database
        try
        {
            _ = Task.Run(DatabaseHelper.Initialize); // do not wait

            while (!DatabaseHelper.LoadAccount) Thread.Sleep(100);

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem",
                I18NManager.Translate("Word.DatabaseAccount")));
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Database")), e);
            WaitForInteractiveFailure();
            return;
        }

        GenerateLogMap();
        HandlerManager.Init();
        if (ConfigManager.Config.GameServer.UsePacketEncryption)
        {
            Crypto.ClientSecretKey = Crypto.InitEc2b();
            if (Crypto.ClientSecretKey == null) ConfigManager.Config.GameServer.UsePacketEncryption = false;
        }

        Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitForAllDone"));
        WebProgram.Main([], GetConfig().HttpServer.Port, GetConfig().HttpServer.GetBindDisplayAddress());
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerRunning", I18NManager.Translate("Word.Dispatch"),
            GetConfig().HttpServer.GetDisplayAddress()));

        if (ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
        {
            var handler =
                new Listener.ConnectionCreatedHandler((conversation, remote) =>
                    new Connection(conversation, remote));
            Listener.CreateConnection = handler;
            Listener.StartListener();
        }

        // Load the game data
        if (ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
        {
            try
            {
                var isCache = false;
                if (File.Exists(ResourceCache.CachePath))
                    if (ConfigManager.Config.ServerOption.UseCache)
                    {
                        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
                            I18NManager.Translate("Word.Cache")));
                        isCache = ResourceCache.LoadCache();
                        if (isCache)
                            GameConstants.RefreshChallengePeakTargetEntriesFromResource();

                        // Clear all game data if cache loading fails
                        if (!isCache)
                        {
                            ResourceCache.ClearGameData();
                            Logger.Warn(I18NManager.Translate("Server.ServerInfo.CacheLoadFailed"));
                        }
                    }
                    else
                    {
                        File.Delete(ResourceCache.CachePath);
                        Logger.Warn(I18NManager.Translate("Server.ServerInfo.CacheLoadSkip"));
                    }

                if (!isCache)
                {
                    Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
                        I18NManager.Translate("Word.GameData")));
                    ResourceManager.LoadGameData();

                    // Async process cache saving
                    if (ConfigManager.Config.ServerOption.UseCache && ResourceCache.IsComplete)
                    {
                        Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitingItem",
                            I18NManager.Translate("Word.Cache")));
                        _ = ResourceCache.SaveCache();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.GameData")),
                    e);
                WaitForInteractiveFailure();
                return;
            }
        }

        // Register the command handlers
        try
        {
            CommandManager.RegisterCommands();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToInitializeItem",
                    I18NManager.Translate("Word.Command")), e);
            WaitForInteractiveFailure();
            return;
        }

        // Load the plugins
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Plugin")));
        try
        {
            PluginManager.LoadPlugins();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Plugin")), e);
            WaitForInteractiveFailure();
            return;
        }

        CommandExecutor.OnRunCommand += (sender, e) => { CommandManager.HandleCommand(e, sender); };

        MuipManager.OnExecuteCommand += CommandManager.HandleCommandAsync;
        MuipManager.OnGetServerInformation += x =>
        {
            foreach (var con in Listener.Connections.Values)
                if ((con as Connection)?.Player != null)
                    x.Add((con as Connection)!.Player!.Uid, (con as Connection)!.Player!.Data);
        };
        MuipManager.OnGetPlayerStatus += (int uid, out PlayerStatusEnum status, out PlayerSubStatusEnum subStatus) =>
        {
            subStatus = PlayerSubStatusEnum.None;
            foreach (var con in Listener.Connections.Values)
                if ((con as Connection)!.Player != null && (con as Connection)!.Player!.Uid == uid)
                {
                    if ((con as Connection)!.Player!.ChallengeManager?.ChallengeInstance is
                             BaseChallengeInstance)
                    {
                        status = PlayerStatusEnum.Challenge;
                    }
                    else if ((con as Connection)!.Player!.RaidManager?.RaidData.CurRaidId != 0)
                    {
                        status = PlayerStatusEnum.Raid;
                    }
                    else
                    {
                        status = PlayerStatusEnum.Explore;
                    }

                    if ((con as Connection)!.Player!.BattleInstance != null) subStatus = PlayerSubStatusEnum.Battle;

                    return;
                }

            status = PlayerStatusEnum.Offline;
        };

        // generate the handbook
        if (ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
            _ = Task.Run(() =>
            {
                try
                {
                    HandbookGenerator.GenerateAll();
                }
                catch (Exception e)
                {
                    Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToInitializeItem",
                        I18NManager.Translate("Word.Handbook")), e);
                }
            });

        if (!DatabaseHelper.LoadAllData)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitForAllDone"));
            var t = Task.Run(() =>
            {
                while (!DatabaseHelper.LoadAllData) // wait for all data to be loaded
                    Thread.Sleep(100);
            });

            await t.WaitAsync(new CancellationToken());

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", I18NManager.Translate("Word.Database")));
        }

        ServerUtils.InitializeHandlers();

        // check if the database is up to date
        var updated = false;
        foreach (var avatarData in DatabaseHelper.GetAllInstanceFromMap<AvatarData>()!)
        {
            if (avatarData.DatabaseVersion == GameConstants.AvatarDbVersion) continue;

            foreach (var avatar in avatarData.Avatars)
            {
                var formalAvatar = new FormalAvatarInfo
                {
                    BaseAvatarId = avatar.AvatarId,
                    AvatarId = avatar.PathId == 0 ? avatar.AvatarId : avatar.PathId,
                    CurrentHp = avatar.CurrentHp,
                    CurrentSp = avatar.CurrentSp,
                    Exp = avatar.Exp,
                    ExtraLineupHp = avatar.ExtraLineupHp,
                    ExtraLineupSp = avatar.ExtraLineupSp,
                    IsMarked = avatar.IsMarked,
                    Level = avatar.Level,
                    Promotion = avatar.Promotion,
                    PathInfos = []
                };

                foreach (var info in avatar.PathInfoes)
                {
                    if (info.Value.PathId == 0)
                        info.Value.PathId = avatar.AvatarId;
                    formalAvatar.PathInfos.Add(info.Value.PathId, new PathInfo(info.Value.PathId)
                    {
                        PathId = info.Value.PathId,
                        EquipId = info.Value.EquipId,
                        Rank = info.Value.Rank,
                        Relic = info.Value.Relic,
                        Skin = info.Value.Skin,
                        EnhanceInfos =
                        {
                            {
                                0, new EnhanceInfo(0)
                                {
                                    SkillTree = avatar.SkillTreeExtra.GetValueOrDefault(info.Value.PathId) ?? []
                                }
                            }
                        }
                    });
                }

                avatarData.FormalAvatars.Add(formalAvatar);
            }

            avatarData.DatabaseVersion = "20250430";
            updated = true;
            DatabaseHelper.ToSaveUidList.Add(avatarData.Uid);
        }

        if (updated)
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.UpdatedItem",
                I18NManager.Translate("Word.Database")));
        }

        if (args.Contains("--upgrade-database")) DatabaseHelper.UpgradeDatabase();

        if (args.Contains("--move")) DatabaseHelper.MoveFromSqlite();

        var elapsed = DateTime.Now - time;
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerStarted",
            Math.Round(elapsed.TotalSeconds, 2).ToString(CultureInfo.InvariantCulture)));

        if (GetConfig().ServerOption.EnableMission)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.MissionEnabled"));

        ResourceManager.IsLoaded = true;

        if (IConsole.IsConsoleAvailable)
        {
            IConsole.OnConsoleExcuteCommand += command =>
            {
                CommandManager.HandleCommand(command, new ConsoleCommandSender(Logger));
                IConsole.RedrawInput(IConsole.Input);
            };

            IConsole.ListenConsole();
        }
        else
        {
            await Task.Delay(Timeout.Infinite);
        }
    }

    public static ConfigContainer GetConfig()
    {
        return ConfigManager.Config;
    }

    private static void PerformCleanup()
    {
        // Save before anything else: shutting down right after a client disconnected ended the process
        // without saving or flushing the log, and the progress since the last periodic save was lost.
        DatabaseHelper.SaveThread?.Interrupt();
        TryCleanup("save", DatabaseHelper.SaveDatabase);
        TryCleanup("plugins", PluginManager.UnloadPlugins);
        TryCleanup("connections", () => Listener.Connections.Values.ToList().ForEach(x => x.Stop()));
        TryCleanup("save", DatabaseHelper.SaveDatabase); // whatever logout handlers changed
    }

    private static void TryCleanup(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Logger.Error($"Shutdown step {step} failed", e);
        }
    }

    private static void GenerateLogMap()
    {
        // get opcode from CmdIds
        var opcodes = typeof(CmdIds).GetFields().Where(x => x.FieldType == typeof(int)).ToList();
        foreach (var opcode in opcodes)
        {
            var name = opcode.Name;
            var value = (int)opcode.GetValue(null)!;
            Connection.LogMap.TryAdd(value, name);
            Connection.NameToOpcode.TryAdd(name, value);
        }
    }
}
