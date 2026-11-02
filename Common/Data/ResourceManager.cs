using System.Reflection;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Config.AdventureAbility;
using HyacineCore.Server.Data.Config.Character;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Data.Config.SummonUnit;
using HyacineCore.Server.Data.Custom;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Internationalization;
using HyacineCore.Server.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HyacineCore.Server.Data;

public class ResourceManager
{
    public static Logger Logger { get; } = new("ResMgr");
    public static bool IsLoaded { get; set; }

    public static void LoadGameData()
    {
        LoadExcel();

        var loadTasks = new List<Task>
        {
            Task.Run(LoadFloorInfo),
            Task.Run(LoadMazeSkill),
            Task.Run(LoadSummonUnit),
            Task.Run(LoadAdventureModifier),
            Task.Run(LoadLocalPlayer)
        };

        if (ConfigManager.Config.ServerOption.EnableMission)
        {
            loadTasks.Add(Task.Run(() =>
            {
                LoadMissionInfo();
                LoadSubMissionInfo();
            }));
            loadTasks.Add(Task.Run(LoadPerformanceInfo));
        }
        else
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.MissionLoadSkipped"));
        }

        GameData.ActivityConfig =
            LoadCustomFile<ActivityConfig>("Activity", "ActivityConfig", ConfigManager.Config.Path.GameDataPath) ??
            new ActivityConfig();
        GameData.BannersConfig =
            LoadCustomFile<BannersConfig>("Banner", "Banners", ConfigManager.Config.Path.GameDataPath) ??
            new BannersConfig();
        GameData.VideoKeysConfig =
            LoadCustomFile<VideoKeysConfig>("VideoKeys", "VideoKeysConfig", ConfigManager.Config.Path.KeyPath) ??
            new VideoKeysConfig();
        GameData.SceneRainbowGroupPropertyData =
            LoadCustomFile<SceneRainbowGroupPropertyConfig>("Scene Rainbow Group Property",
                "SceneRainbowGroupProperty", ConfigManager.Config.Path.GameDataPath) ??
            new SceneRainbowGroupPropertyConfig();
        GameData.ChallengePeakOverrideConfig =
            LoadCustomFile<ChallengePeakOverrideConfig>("ChallengePeak", "ChallengePeak",
                ConfigManager.Config.Path.GameDataPath) ??
            new ChallengePeakOverrideConfig();
        ApplyChallengePeakOverrideConfig(GameData.ChallengePeakOverrideConfig);

        Task.WaitAll(loadTasks.ToArray());

        // Build ChallengePeak runtime mapping from resource data instead of relying on hardcoded map.
        GameConstants.RefreshChallengePeakTargetEntriesFromResource();

        // copy modifiers
        foreach (var value in GameData.AdventureAbilityConfigListData.Values)
        foreach (var adventureModifierConfig in value?.GlobalModifiers ?? [])
            GameData.AdventureModifierData.Add(adventureModifierConfig.Key, adventureModifierConfig.Value);
    }

    public static void LoadExcel()
    {
        var classes = Assembly.GetExecutingAssembly().GetTypes(); // Get all classes in the assembly
        List<ExcelResource> resList = [];

        foreach (var cls in classes.Where(x => x.IsSubclassOf(typeof(ExcelResource))))
        {
            var res = LoadSingleExcelResource(cls);
            if (res != null) resList.AddRange(res);
        }

        foreach (var cls in resList) cls.AfterAllDone();
    }

    public static List<T>? LoadSingleExcel<T>(Type cls) where T : ExcelResource, new()
    {
        return LoadSingleExcelResource(cls) as List<T>;
    }

    public static List<ExcelResource>? LoadSingleExcelResource(Type cls)
    {
        var attribute = (ResourceEntity?)Attribute.GetCustomAttribute(cls, typeof(ResourceEntity));

        if (attribute == null) return null;
        var resource = (ExcelResource)Activator.CreateInstance(cls)!;
        var count = 0;
        List<ExcelResource> resList = [];
        foreach (var fileName in attribute.FileName)
            try
            {
                var path = ConfigManager.Config.Path.ResourcePath + "/ExcelOutput/" + fileName;
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    // ResourceCache.IsComplete = false;
                    Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToReadItem", fileName,
                        I18NManager.Translate("Word.NotFound")));
                    continue;
                }

                var json = file.OpenText().ReadToEnd();
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.Read();
                    switch (reader.TokenType)
                    {
                        case JsonToken.StartArray:
                        {
                            // array
                            var jArray = JArray.Parse(json);
                            foreach (var item in jArray)
                            {
                                var res = JsonConvert.DeserializeObject(item.ToString(), cls);
                                resList.Add((ExcelResource)res!);
                                ((ExcelResource?)res)?.Loaded();
                                count++;
                            }

                            break;
                        }
                        case JsonToken.StartObject:
                        {
                            // dictionary
                            var jObject = JObject.Parse(json);
                            foreach (var (_, obj) in jObject)
                            {
                                var instance = JsonConvert.DeserializeObject(obj!.ToString(), cls);

                                if (((ExcelResource?)instance)?.GetId() == 0 || (ExcelResource?)instance == null)
                                {
                                    // Deserialize as JObject to handle nested dictionaries
                                    var nestedObject = JsonConvert.DeserializeObject<JObject>(obj.ToString());

                                    foreach (var nestedItem in nestedObject ?? [])
                                    {
                                        var nestedInstance =
                                            JsonConvert.DeserializeObject(nestedItem.Value!.ToString(), cls);
                                        resList.Add((ExcelResource)nestedInstance!);
                                        ((ExcelResource?)nestedInstance)?.Loaded();
                                        count++;
                                    }
                                }
                                else
                                {
                                    resList.Add((ExcelResource)instance);
                                    ((ExcelResource)instance).Loaded();
                                }

                                count++;
                            }

                            break;
                        }
                    }
                }

                resource.Finalized();
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", fileName,
                        I18NManager.Translate("Word.Error")), ex);
            }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(), cls.Name));

        return resList;
    }

    public static void LoadFloorInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.FloorInfo")));
        DirectoryInfo directory = new(ConfigManager.Config.Path.ResourcePath + "/Config/LevelOutput/RuntimeFloor/");
        var missingGroupInfos = 0;
        var floorDataLock = new object();

        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.FloorInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/LevelOutput/RuntimeFloor",
                I18NManager.Translate("Word.FloorMissingResult")));
            return;
        }

        var files = directory.GetFiles();

        // Load floor infos in parallel
        var res = Parallel.ForEach(files, file =>
        {
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd();
                var info = JsonConvert.DeserializeObject<FloorInfo>(text);
                var name = file.Name[..file.Name.IndexOf('.')];
                if (info == null) return;
                lock (floorDataLock)
                {
                    GameData.FloorInfoData[name] = info;
                }

                // Load navmap infos
                FileInfo navmapFile = new(ConfigManager.Config.Path.ResourcePath + "/" + info.NavmapConfigPath);
                if (navmapFile.Exists)
                    try
                    {
                        using var navmapReader = navmapFile.OpenRead();
                        using StreamReader navmapReader2 = new(navmapReader);
                        var navmapText = navmapReader2.ReadToEnd();
                        var navmap = JsonConvert.DeserializeObject<MapInfo>(navmapText);
                        if (navmap != null)
                            foreach (var area in navmap.AreaList)
                            foreach (var section in area.MinimapVolume.Sections)
                                info.MapSections.Add(section.ID);
                    }
                    catch (Exception ex)
                    {
                        ResourceCache.IsComplete = false;
                        Logger.Error(
                            I18NManager.Translate("Server.ServerInfo.FailedToReadItem", navmapFile.Name,
                                I18NManager.Translate("Word.Error")), ex);
                    }

                // Load group infos sequentially to maintain order. A group a dimension rebuilds is listed once per
                // variant (F20412001's G102 and G102_D1, whose puzzle-state default differs): the floor's default
                // dimension decides its config, and each variant stays reachable by its index for story lines.
                var defaultIndexes = info.DimensionList.Find(d => d.ID == 0)?.GroupIndexList.ToHashSet() ?? [];
                for (var index = 0; index < info.GroupInstanceList.Count; index++)
                {
                    var groupInfo = info.GroupInstanceList[index];
                    if (groupInfo.IsDelete) continue;
                    if (groupInfo.GroupPath.Contains("_D100")) continue;
                    FileInfo groupFile = new(ConfigManager.Config.Path.ResourcePath + "/" + groupInfo.GroupPath);
                    if (!groupFile.Exists) continue;

                    try
                    {
                        using var groupReader = groupFile.OpenRead();
                        using StreamReader groupReader2 = new(groupReader);
                        var groupText = groupReader2.ReadToEnd();
                        var group = JsonConvert.DeserializeObject<GroupInfo>(groupText);
                        if (group != null)
                        {
                            group.Id = groupInfo.ID;
                            if (defaultIndexes.Count == 0 || defaultIndexes.Contains(index) ||
                                !info.Groups.ContainsKey(groupInfo.ID))
                                info.Groups[groupInfo.ID] = group;
                            info.GroupsByIndex[index] = group;

                            // Load graph
                            var graphPath = ConfigManager.Config.Path.ResourcePath + "/" + group.LevelGraph;
                            var graphFile = new FileInfo(graphPath);
                            if (graphFile.Exists)
                            {
                                using var graphReader = graphFile.OpenRead();
                                using StreamReader graphReader2 = new(graphReader);
                                var graphText = graphReader2.ReadToEnd().Replace("$type", "Type");
                                var graphObj = JObject.Parse(graphText);
                                var graphInfo = LevelGraphConfigInfo.LoadFromJsonObject(graphObj);
                                group.LevelGraphConfig = graphInfo;
                            }

                            group.Load();
                        }
                    }
                    catch (Exception ex)
                    {
                        ResourceCache.IsComplete = false;
                        Logger.Error(
                            I18NManager.Translate("Server.ServerInfo.FailedToReadItem", groupFile.Name,
                                I18NManager.Translate("Word.Error")), ex);
                    }
                }

                if (info.Groups.Count == 0) Interlocked.Exchange(ref missingGroupInfos, 1);

                info.OnLoad();
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!res.IsCompleted) Thread.Sleep(10);

        if (missingGroupInfos == 1)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.FloorGroupInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/LevelOutput/SharedRuntimeGroup",
                I18NManager.Translate("Word.FloorGroupMissingResult")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", GameData.FloorInfoData.Count.ToString(),
            I18NManager.Translate("Word.FloorInfo")));
    }

    public static void LoadMissionInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.MissionInfo")));
        DirectoryInfo directory = new(ConfigManager.Config.Path.ResourcePath + "/Config/Level/Mission");
        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.MissionInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/Level/Mission",
                I18NManager.Translate("Word.Mission")));
            return;
        }

        var missingMissionInfos = 0;
        var count = 0;
        var res = Parallel.ForEach(GameData.MainMissionData, missionExcel =>
        {
            var path =
                $"{ConfigManager.Config.Path.ResourcePath}/Config/Level/Mission/{missionExcel.Key}/MissionInfo_{missionExcel.Key}.json";
            if (!File.Exists(path))
            {
                Interlocked.Exchange(ref missingMissionInfos, 1);
                return;
            }

            var json = File.ReadAllText(path);
            var missionInfo = JsonConvert.DeserializeObject<MissionInfo>(json);
            if (missionInfo != null)
            {
                GameData.MainMissionData[missionExcel.Key].SetMissionInfo(missionInfo);
                Interlocked.Increment(ref count);
            }
            else
            {
                Interlocked.Exchange(ref missingMissionInfos, 1);
            }
        });

        // wait it done
        while (!res.IsCompleted) Thread.Sleep(10);

        if (missingMissionInfos == 1)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.MissionInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/Level/Mission",
                I18NManager.Translate("Word.Mission")));
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.MissionInfo")));
    }

    public static T? LoadCustomFile<T>(string filetype, string filename, string? folderPath = null)
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", filetype));
        var basePath = string.IsNullOrWhiteSpace(folderPath) ? ConfigManager.Config.Path.ConfigPath : folderPath;
        var filePath = Path.Combine(basePath, $"{filename}.json");
        FileInfo file = new(filePath);
        T? customFile = default;
        if (!file.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing", filetype,
                filePath, filetype));
            return customFile;
        }

        try
        {
            using var reader = file.OpenRead();
            using StreamReader reader2 = new(reader);
            var text = reader2.ReadToEnd();
            var json = JsonConvert.DeserializeObject<T>(text);
            customFile = json;
        }
        catch (Exception ex)
        {
            ResourceCache.IsComplete = false;
            Logger.Error("Error in reading " + file.Name, ex);
        }

        switch (customFile)
        {
            case Dictionary<int, int> d:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", d.Count.ToString(), filetype));
                break;
            case Dictionary<int, List<int>> di:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", di.Count.ToString(), filetype));
                break;
            case BannersConfig c:
                Logger.Info(
                    I18NManager.Translate("Server.ServerInfo.LoadedItems", c.Banners.Count.ToString(), filetype));
                break;
            case ActivityConfig a:
                a.Normalize();
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", a.ScheduleData.Count.ToString(),
                    filetype));
                break;
            case VideoKeysConfig a:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", a.TotalCount.ToString(),
                    filetype));
                break;
            case SceneRainbowGroupPropertyConfig c:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", c.FloorProperty.Count.ToString(),
                    filetype));
                break;
            case ChallengePeakOverrideConfig c:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", c.ChallengePeak.Count.ToString(),
                    filetype));
                break;
            default:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", filetype));
                break;
        }

        return customFile;
    }

    private static void ApplyChallengePeakOverrideConfig(ChallengePeakOverrideConfig? config)
    {
        if (config?.ChallengePeak == null || config.ChallengePeak.Count == 0) return;

        foreach (var group in config.ChallengePeak)
        {
            if (group.Stages == null || group.Stages.Count == 0) continue;

            foreach (var (stageKey, stageOverride) in group.Stages)
            {
                if (!int.TryParse(stageKey, out var stageId)) continue;
                if (!GameData.ChallengePeakConfigData.TryGetValue(stageId, out var peakConfig)) continue;

                if (stageOverride.MapEntranceId > 0)
                    peakConfig.MapEntranceID = stageOverride.MapEntranceId;

                if (stageOverride.MazeGroupId > 0)
                    peakConfig.MazeGroupID = stageOverride.MazeGroupId;

                var npcMonsterId = stageOverride.NpcMonsterId > 0
                    ? stageOverride.NpcMonsterId
                    : group.NpcMonsterIdDefault;
                if (npcMonsterId > 0)
                {
                    if (peakConfig.EventIDList.Count > 0)
                        peakConfig.NpcMonsterIDList = Enumerable.Repeat(npcMonsterId, peakConfig.EventIDList.Count)
                            .ToList();
                    else
                        peakConfig.NpcMonsterIDList = [npcMonsterId];
                }

                peakConfig.RebuildChallengeMonsters();
            }
        }
    }

    public static void LoadMazeSkill()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.MazeSkillInfo")));
        var count = 0;
        var abilityDataLock = new object();

        void AddAdventureAbility(int id, AdventureAbilityConfigListInfo info)
        {
            lock (abilityDataLock)
            {
                if (GameData.AdventureAbilityConfigListData.TryAdd(id, info))
                    Interlocked.Increment(ref count);
            }
        }

        var res = Parallel.ForEach(GameData.AdventurePlayerData.Values, adventure =>
        {
            var adventurePath = adventure.PlayerJsonPath.Replace("_Config.json", "_Ability.json")
                .Replace("ConfigCharacter", "ConfigAdventureAbility");
            var path = ConfigManager.Config.Path.ResourcePath + "/" + adventurePath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);

                var info = AdventureAbilityConfigListInfo.LoadFromJsonObject(obj);
                AddAdventureAbility(adventure.ID, info);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", adventurePath,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        var res2 = Parallel.ForEach(GameData.NpcMonsterDataData.Values, adventure =>
        {
            var adventurePath = adventure.ConfigEntityPath.Replace("_Entity.json", "_Ability.json")
                .Replace("_Config.json", "_Ability.json")
                .Replace("ConfigEntity", "ConfigAdventureAbility");

            var path = ConfigManager.Config.Path.ResourcePath + "/" + adventurePath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);

                var info = AdventureAbilityConfigListInfo.LoadFromJsonObject(obj);
                AddAdventureAbility(adventure.ID, info);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", adventurePath,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!res.IsCompleted || !res2.IsCompleted) Thread.Sleep(10);

        // Missing adventure ability files are tolerated in this simplified server build.

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.MazeSkillInfo")));
    }

    public static void LoadSummonUnit()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.SummonUnitInfo")));
        var count = 0;
        var res = Parallel.ForEach(GameData.SummonUnitDataData.Values, summonUnit =>
        {
            var path = ConfigManager.Config.Path.ResourcePath + "/" + summonUnit.JsonPath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");

                var obj = JObject.Parse(text);
                var info = SummonUnitConfigInfo.LoadFromJsonObject(obj);

                summonUnit.ConfigInfo = info;
                Interlocked.Increment(ref count);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", summonUnit.JsonPath,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!res.IsCompleted) Thread.Sleep(10);

        if (count < GameData.SummonUnitDataData.Count)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.SummonUnitInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/ConfigSummonUnit",
                I18NManager.Translate("Word.SummonUnit")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.SummonUnitInfo")));
    }
    public static void LoadPerformanceInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.PerformanceInfo")));
        var count = 0;

        var res = Parallel.ForEach(GameData.PerformanceEData.Values, performance =>
        {
                if (performance.PerformancePath == "")
                {
                Interlocked.Increment(ref count);
                return;
                }

            var path = ConfigManager.Config.Path.ResourcePath + "/" + performance.PerformancePath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);
                var info = LevelGraphConfigInfo.LoadFromJsonObject(obj);
                performance.ActInfo = info;
                Interlocked.Increment(ref count);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        var res2 = Parallel.ForEach(GameData.PerformanceDData.Values, performance =>
        {
            if (performance.PerformancePath == "")
            {
                Interlocked.Increment(ref count);
                return;
            }

            var path = ConfigManager.Config.Path.ResourcePath + "/" + performance.PerformancePath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);
                var info = LevelGraphConfigInfo.LoadFromJsonObject(obj);
                performance.ActInfo = info;
                Interlocked.Increment(ref count);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!(res.IsCompleted && res2.IsCompleted)) Thread.Sleep(10);

        if (count < GameData.PerformanceEData.Count + GameData.PerformanceDData.Count)
        {
            // looks like many dont exist
            //Logger.Warn("Performance infos are missing, please check your resources folder: " + ConfigManager.Config.Path.ResourcePath + "/Config/Level/Mission/*/Act. Performances may not work!");
        }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.PerformanceInfo")));
    }

    public static void LoadSubMissionInfo()
    {
        Logger.Info(
            I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.SubMissionInfo")));
        var count = 0;
        var res = Parallel.ForEach(GameData.SubMissionInfoData.Values, subMission =>
        {
            if (subMission.SubMissionInfo == null || subMission.SubMissionInfo.MissionJsonPath == "") return;

            var path = ConfigManager.Config.Path.ResourcePath + "/" + subMission.SubMissionInfo.MissionJsonPath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);
                var info = LevelGraphConfigInfo.LoadFromJsonObject(obj);
                subMission.SubMissionTaskInfo = info;
                Interlocked.Increment(ref count);
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!res.IsCompleted) Thread.Sleep(10);

        if (count < GameData.SubMissionInfoData.Count)
        {
            //Logger.Warn("Performance infos are missing, please check your resources folder: " + ConfigManager.Config.Path.ResourcePath + "/Config/Level/Mission/*/Act. Performances may not work!");
        }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.SubMissionInfo")));
    }
    public static void LoadAdventureModifier()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.AdventureModifierInfo")));
        var count = 0;

        // list the files in folder
        var directory = new DirectoryInfo($"{ConfigManager.Config.Path.ResourcePath}/Config/ConfigAdventureModifier");
        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.AdventureModifierInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/ConfigAdventureModifier",
                I18NManager.Translate("Word.Buff")));

            return;
        }

        var files = directory.GetFiles();

        foreach (var file in files)
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");
                var obj = JObject.Parse(text);
                var info = AdventureModifierLookupTableConfig.LoadFromJObject(obj);

                foreach (var config in info.ModifierMap)
                {
                    GameData.AdventureModifierData.Add(config.Key, config.Value);
                    count++;
                }
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                        I18NManager.Translate("Word.Error")), ex);
            }

        //if (count < boardList.Count)
        //    Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
        //        I18NManager.Translate("Word.AdventureModifierInfo"),
        //        $"{ConfigManager.Config.Path.ResourcePath}/Config/ConfigAdventureModifier",
        //        I18NManager.Translate("Word.Buff")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.AdventureModifierInfo")));
    }

    public static void LoadLocalPlayer()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.LocalPlayerCharacter")));
        var count = 0;
        var characterDataLock = new object();
        var res = Parallel.ForEach(GameData.AdventurePlayerData.Values, excel =>
        {
            var path = ConfigManager.Config.Path.ResourcePath + "/" + excel.PlayerJsonPath;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            try
            {
                using var reader = file.OpenRead();
                using StreamReader reader2 = new(reader);
                var text = reader2.ReadToEnd().Replace("$type", "Type");

                var info = JsonConvert.DeserializeObject<CharacterConfigInfo>(text);
                if (info == null) return;

                lock (characterDataLock)
                {
                    if (GameData.CharacterConfigInfoData.TryAdd(excel.ID, info))
                        Interlocked.Increment(ref count);
                }
            }
            catch (Exception ex)
            {
                ResourceCache.IsComplete = false;
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToReadItem", excel.PlayerJsonPath,
                        I18NManager.Translate("Word.Error")), ex);
            }
        });

        // wait it done
        while (!res.IsCompleted) Thread.Sleep(10);

        if (count < GameData.AdventurePlayerData.Count)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.LocalPlayerCharacterInfo"),
                $"{ConfigManager.Config.Path.ResourcePath}/Config/ConfigCharacter",
                I18NManager.Translate("Word.LocalPlayerCharacter")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.LocalPlayerCharacterInfo")));
    }
}
