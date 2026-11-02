using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Data.Custom;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Util;
using Newtonsoft.Json.Linq;

namespace HyacineCore.Server.GameServer.Game.Mission;

// Puzzle steps that wait for a floor saved value, a group property or a prop timeline are set by group graphs.
// The data strips those graph tasks' names and values, so the server cannot run them. Instead the step
// finishes as soon as the player interacts with the puzzle's group. Every use is logged.
public static class PuzzleFallback
{
    private static readonly Logger Log = new("PuzzleFallback");
    private static readonly ConcurrentDictionary<string, string> GraphText = new();

    public static async ValueTask OnGroupInteract(PlayerInstance player, int groupId)
    {
        if (!ConfigManager.Config.ServerOption.PuzzleFallback) return;
        var scene = player.SceneInstance;
        if (scene == null) return;

        foreach (var info in player.MissionManager!.GetRunningSubMissionList())
        {
            if (info.LevelFloorID != scene.FloorId) continue;

            switch (info.FinishType)
            {
                case MissionFinishTypeEnum.FloorSavedValue:
                    if (!PuzzleGroups(scene.FloorInfo, info).Contains(groupId)) continue;
                    Log.Warn($"sub-mission {info.ID}: group {groupId} interacted, " +
                             $"setting {info.ParamStr1}={info.ParamInt1} on floor {scene.FloorId}");
                    await scene.UpdateFloorSavedValue(info.ParamStr1, info.ParamInt1);
                    break;
                case MissionFinishTypeEnum.GroupPropertyValue:
                    if (info.ParamInt1 != groupId) continue;
                    Log.Warn($"sub-mission {info.ID}: group {groupId} interacted, " +
                             $"setting {info.ParamStr1}={info.ParamInt2} on floor {scene.FloorId}");
                    await scene.UpdateGroupProperty(groupId, info.ParamStr1, info.ParamInt2);
                    break;
                case MissionFinishTypeEnum.TimeLineSetState:
                    if (info.ParamInt1 != groupId) continue;
                    Log.Warn($"sub-mission {info.ID}: group {groupId} interacted, " +
                             $"taking prop {info.ParamInt2} timeline as {info.ParamStr1} on floor {scene.FloorId}");
                    break;
                default:
                    continue;
            }

            // The value may not be declared on the floor or group, so the setter above can be a no-op.
            if (player.MissionManager.GetSubMissionStatus(info.ID) == MissionPhaseEnum.Accept)
                await player.MissionManager.FinishSubMission(info.ID);
        }
    }

    // Minigame steps the server cannot evaluate are skipped when accepted, and so are puzzle steps nothing
    // can reach.
    // A floor saved value that no sub-mission finish action sets, the server's own interaction rules
    // (FSV_ML*, JG_ML*) do not cover and whose puzzle groups have no server-side prop to interact with
    // (client-side or entity-less groups) can never be reached, e.g. a choice made inside a
    // raid's stripped graph. Such a step is skipped as soon as it is accepted; of a choice pair, the first
    // one taken wins and its delSubMission removes the other.
    public static async ValueTask OnSubMissionAccepted(PlayerInstance player, SubMissionInfo info)
    {
        if (!ConfigManager.Config.ServerOption.PuzzleFallback) return;
        if (player.MissionManager!.GetSubMissionStatus(info.ID) != MissionPhaseEnum.Accept) return;
        var type = info.FinishType.ToString();
        if (MinigamePrefixes.Any(type.StartsWith) && !MissionManager.FinishTypeHandlers.ContainsKey(info.FinishType))
        {
            Log.Warn($"sub-mission {info.ID}: minigame step {type} is not implemented, skipping it");
            await player.MissionManager.FinishSubMission(info.ID);
            return;
        }

        var floor = GameData.GetFloorInfo(info.LevelFloorID);
        switch (info.FinishType)
        {
            case MissionFinishTypeEnum.FloorSavedValue:
                if (Skipped(player, info))
                {
                    await TakeSkippedValue(player, info);
                    return;
                }

                if (info.ParamStr1.StartsWith("FSV_ML") || info.ParamStr1.StartsWith("JG_ML")) return;
                if (MissionSetValues.Value.Contains((info.LevelFloorID.ToString(), info.ParamStr1, info.ParamInt1))) return;
                if (RainbowSetValues.Value.Contains((info.LevelFloorID, info.ParamStr1, info.ParamInt1))) return;
                if (PuzzleGroups(floor, info).Any(id => Interactable(floor, id))) return;

                Log.Warn($"sub-mission {info.ID}: nothing can set {info.ParamStr1}={info.ParamInt1} " +
                         $"on floor {info.LevelFloorID}, skipping it");
                if (!player.SceneData!.FloorSavedData.TryGetValue(info.LevelFloorID, out var values))
                {
                    values = [];
                    player.SceneData.FloorSavedData[info.LevelFloorID] = values;
                }

                values[info.ParamStr1] = info.ParamInt1;
                break;
            case MissionFinishTypeEnum.GroupPropertyValue:
                // A step waiting on a prop turned back in time (104041026: rewind G31's floating stone, which unlocks
                // the lift, Lock=0): the 4.1 client never builds the rewind button for it (MazeMiniGameBtn stays
                // empty, like 104010572's), so the step is taken as done and the property set as the rewind would.
                if (floor?.Groups.GetValueOrDefault(info.WayPointGroupID)?.PropList
                        .FirstOrDefault(x => x.ID == info.WayPointEntityID) is { IsTimeRewindable: true })
                {
                    Log.Warn($"sub-mission {info.ID}: waypoint prop {info.WayPointGroupID}/{info.WayPointEntityID} is " +
                             $"turned back in time, which the client offers no button for; setting group property " +
                             $"{info.ParamInt1}/{info.ParamStr1}={info.ParamInt2} on floor {info.LevelFloorID}");
                    await SetGroupProperty(player, info.LevelFloorID, info.ParamInt1, info.ParamStr1, info.ParamInt2);
                    if (player.MissionManager.GetSubMissionStatus(info.ID) == MissionPhaseEnum.Accept)
                        await player.MissionManager.FinishSubMission(info.ID);
                    return;
                }

                // The mascot capture QTE (raid 41442's 104050421, G28 SGP_CatchFinish): timed holds on a rewind control
                // the 4.1 client leaves empty, with the camera locked on the arena; its group is already loaded, so no
                // interaction reaches OnGroupInteract either. The property is set as a catch would.
                if (floor?.Groups.GetValueOrDefault(info.ParamInt1) is { LevelGraph.Length: > 0 } qteGroup &&
                    ReadGraph(qteGroup.LevelGraph).Contains("RPG.GameCore.QteCatchGhostRewind\""))
                {
                    Log.Warn($"sub-mission {info.ID}: group {info.ParamInt1} is a capture QTE the client cannot play; " +
                             $"setting group property {info.ParamStr1}={info.ParamInt2} on floor {info.LevelFloorID}");
                    await SetGroupProperty(player, info.LevelFloorID, info.ParamInt1, info.ParamStr1, info.ParamInt2);
                    if (player.MissionManager.GetSubMissionStatus(info.ID) == MissionPhaseEnum.Accept)
                        await player.MissionManager.FinishSubMission(info.ID);
                    return;
                }

                if (Interactable(floor, info.ParamInt1) || ClientSetsGroupProperty(floor, info.ParamInt1, info.ParamStr1))
                    return;
                Log.Warn($"sub-mission {info.ID}: nothing can set group property {info.ParamInt1}/{info.ParamStr1}=" +
                         $"{info.ParamInt2} on floor {info.LevelFloorID}, skipping it");
                break;
            case MissionFinishTypeEnum.TimeLineSetState:
                if (Interactable(floor, info.ParamInt1)) return;
                Log.Warn($"sub-mission {info.ID}: group {info.ParamInt1} has nothing to interact with for timeline " +
                         $"{info.ParamStr1} on floor {info.LevelFloorID}, skipping it");
                break;
            case MissionFinishTypeEnum.Talk:
                // A talk on a prop nothing creates (104050303 「见证卡厄斯兰那的全部回忆」: G89's head, LoadOnInitial
                // false, its dialogue its own; G89 is a client group whose graph, empty in the data, never spawns
                // it, and the client ignores a server-sent entity for it). Its video, Act104050303, already played
                // when G87's district was first entered (104050301).
                if (floor?.Groups.GetValueOrDefault(info.WayPointGroupID) is not
                        { LoadSide: GroupLoadSideEnum.Client, LevelGraph.Length: > 0 } talkGroup ||
                    talkGroup.PropList.FirstOrDefault(x => x.ID == info.WayPointEntityID) is not
                        { LoadOnInitial: false, Dialog.LevelGraph.Length: > 0 } ||
                    ReadGraph(talkGroup.LevelGraph).Contains("$type")) return;
                Log.Warn($"sub-mission {info.ID}: waypoint prop {info.WayPointGroupID}/{info.WayPointEntityID} is " +
                         $"never created (its group graph is empty), skipping the talk");
                await player.MissionManager.FinishSubMission(info.ID);
                // The talk also leads into the raid its goto names (GotoID 6900, a raid entrance: 41442 for
                // 104050303, whose finish 104050306 waits on), which the client would ask for.
                if (info.GotoID == RaidEntranceGoto && info.GotoParam.FirstOrDefault() is > 0 and var raidId &&
                    player.RaidManager!.RaidData.CurRaidId == 0 &&
                    await player.RaidManager.EnterRaid(raidId, 0) is { } record)
                {
                    Log.Warn($"sub-mission {info.ID}: entering raid {raidId} the talk leads to");
                    await player.EnterScene(record.EntryId, 0, true);
                }

                return;
            default:
                return;
        }

        await player.MissionManager.FinishSubMission(info.ID);
    }

    // Through the scene when the player is on that floor (the client sees the change), else into the save.
    private static async ValueTask SetGroupProperty(PlayerInstance player, int floorId, int groupId, string name,
        int value)
    {
        if (player.SceneInstance is { } scene && scene.FloorId == floorId)
        {
            await scene.UpdateGroupProperty(groupId, name, value);
            return;
        }

        if (!player.SceneData!.GroupPropertyData.TryGetValue(floorId, out var groups))
            player.SceneData.GroupPropertyData[floorId] = groups = [];
        if (!groups.TryGetValue(groupId, out var values)) groups[groupId] = values = [];
        values[name] = value;
    }

    private static bool Interactable(FloorInfo? floor, int groupId)
    {
        // Same props the scene loader spawns.
        return floor?.Groups.GetValueOrDefault(groupId) is { LoadSide: GroupLoadSideEnum.Server } group &&
               group.PropList.Any(p => !p.IsClientOnly && !p.IsDelete && p.LoadOnInitial);
    }

    // Client graphs set server group properties with RequestSetServerGroupProperty (UpdateGroupPropertyCsReq).
    private static bool ClientSetsGroupProperty(FloorInfo? floor, int groupId, string name)
    {
        return (floor?.Groups.Values ?? []).Any(g =>
        {
            if (string.IsNullOrEmpty(g.LevelGraph)) return false;
            var text = ReadGraph(g.LevelGraph);
            if (!text.Contains("RequestSetServerGroupProperty") || !text.Contains($"\"{name}\"")) return false;
            return JToken.Parse(text).SelectTokens("$..*").OfType<JObject>()
                .Where(t => (string?)t["$type"] == "RPG.GameCore.RequestSetServerGroupProperty")
                .Any(t => (int?)t["GroupID"] == groupId && (string?)t["DynamicName"]?["Value"] == name);
        });
    }

    // Activity minigames the story drops into; the server implements none of their finish types.
    private static readonly string[] MinigamePrefixes = ["TarotBook", "FiveDim", "DiceCombat", "LimaoNews"];

    private static readonly Lazy<HashSet<(string, string, int)>> MissionSetValues = new(() =>
        GameData.SubMissionInfoData.Values
            .SelectMany(x => x.SubMissionInfo?.FinishActionList ?? [])
            .Where(x => x.FinishActionType == FinishActionTypeEnum.SetFloorSavedValue &&
                        x.FinishActionParaString.Count >= 4 && int.TryParse(x.FinishActionParaString[3], out _))
            .Select(x => (x.FinishActionParaString[1], x.FinishActionParaString[2], int.Parse(x.FinishActionParaString[3])))
            .ToHashSet());

    // The rainbow compass floors' group property rules (SceneRainbowGroupProperty) set floor saved values once their
    // puzzles are solved: 1040406's G6/G77/G78 compasses set FSV_CompassFinished 1-3 and FSV_Rainbow, and 104040651's
    // lift battle waits for FSV_Rainbow. Skipping those compass steps would leave FSV_Rainbow unset for good.
    private static readonly Lazy<HashSet<(int, string, int)>> RainbowSetValues = new(() =>
    {
        HashSet<(int, string, int)> values = [];

        void Add(int floorId, IEnumerable<RainbowActionInfo> actions)
        {
            foreach (var action in actions)
            {
                if (action.ActionType == SceneActionTypeEnum.SetFloorSavedValue &&
                    action.Params.GetValueOrDefault("SavedValueName") is string name &&
                    action.Params.GetValueOrDefault("SavedValue") is long value)
                    values.Add((floorId, name, (int)value));
                if (action.Params.GetValueOrDefault("SuccActions") is JToken succ)
                    Add(floorId, succ.ToObject<List<RainbowActionInfo>>() ?? []);
            }
        }

        foreach (var (floorId, groups) in GameData.SceneRainbowGroupPropertyData.FloorProperty)
        foreach (var property in groups.Values.SelectMany(g => g.Values).SelectMany(p => p.Values))
            Add(floorId, property.PrivateActions.Concat(property.Actions));
        return values;
    });

    private static readonly ConcurrentDictionary<string, HashSet<(string, int)>> FloorValueBranches = new();

    // The (floor saved value, value) branches of a group graph that change the group's state when the value changes
    // (LoopWaitServerFloorSavedValueChange, then ByCompareFloorSavedValue branches that run ChangeGroupState).
    public static HashSet<(string Name, int Value)> FloorValueStates(GroupInfo group)
    {
        if (string.IsNullOrEmpty(group.LevelGraph)) return [];
        return FloorValueBranches.GetOrAdd(group.LevelGraph, path =>
        {
            var text = ReadGraph(path);
            if (!text.Contains("LoopWaitServerFloorSavedValueChange") || !text.Contains("ChangeGroupState")) return [];
            try
            {
                return JToken.Parse(text).SelectTokens("$..*").OfType<JObject>()
                    .Where(t => (string?)t["$type"] == "RPG.GameCore.PredicateTaskList" &&
                                t["Predicate"] is JObject p &&
                                (string?)p["$type"] == "RPG.GameCore.ByCompareFloorSavedValue" &&
                                p.Properties().All(x => x.Name is "$type" or "Name" or "CompareValue") &&
                                t["SuccessTaskList"] is JArray tasks &&
                                tasks.Any(x => (string?)x["$type"] == "RPG.GameCore.ChangeGroupState"))
                    .Select(t => ((string?)t["Predicate"]!["Name"] ?? "", (int?)t["Predicate"]!["CompareValue"] ?? 0))
                    .ToHashSet();
            }
            catch
            {
                // a graph that does not parse changes no state
                return [];
            }
        });
    }

    private static readonly ConcurrentDictionary<string, List<List<(string, int)>>> FloorValueUnlockBranches = new();

    // The floor saved value conditions of a group graph's branches that set a server group property it does not name
    // (WaitServerFloorSavedValueChange, then a PredicateTaskList of ByCompareFloorSavedValue, alone or under ByAnd,
    // whose success is a bare SetServerGroupProperty), each branch as (value name, value) pairs that must all hold.
    public static List<List<(string Name, int Value)>> FloorValueUnlocks(GroupInfo group)
    {
        if (string.IsNullOrEmpty(group.LevelGraph)) return [];
        return FloorValueUnlockBranches.GetOrAdd(group.LevelGraph, path =>
        {
            var text = ReadGraph(path);
            if (!text.Contains("WaitServerFloorSavedValueChange") || !text.Contains("SetServerGroupProperty")) return [];
            try
            {
                List<(string, int)>? Compare(JToken? p)
                {
                    if (p is not JObject o) return null;
                    if ((string?)o["$type"] == "RPG.GameCore.ByAnd" && o["PredicateList"] is JArray list)
                    {
                        var all = list.Select(Compare).ToList();
                        return all.Any(x => x == null) ? null : all.SelectMany(x => x!).ToList();
                    }

                    if ((string?)o["$type"] != "RPG.GameCore.ByCompareFloorSavedValue" ||
                        o.Properties().Any(x => x.Name is not ("$type" or "Name" or "DynamicName" or "CompareValue")))
                        return null;
                    var name = (string?)o["DynamicName"]?["Value"] ?? (string?)o["Name"];
                    return string.IsNullOrEmpty(name) ? null : [(name, (int?)o["CompareValue"] ?? 0)];
                }

                return JToken.Parse(text).SelectTokens("$..*").OfType<JObject>()
                    .Where(t => (string?)t["$type"] == "RPG.GameCore.PredicateTaskList" &&
                                t["SuccessTaskList"] is JArray tasks && tasks.Count > 0 &&
                                tasks.All(x => x is JObject task && (string?)task["$type"] ==
                                    "RPG.GameCore.SetServerGroupProperty" && task.Count == 1))
                    .Select(t => Compare(t["Predicate"]))
                    .Where(x => x is { Count: > 0 })
                    .Select(x => x!)
                    .ToList();
            }
            catch
            {
                // a graph that does not parse unlocks nothing
                return [];
            }
        });
    }

    // Groups whose graph mentions the saved value; without any, the groups the sub-mission lists.
    public static HashSet<int> PuzzleGroups(FloorInfo? floor, SubMissionInfo info)
    {
        var name = $"\"{info.ParamStr1}\"";
        var groups = (floor?.Groups.Values ?? [])
            .Where(g => !string.IsNullOrEmpty(g.LevelGraph) && ReadGraph(g.LevelGraph).Contains(name))
            .Select(g => g.Id).ToHashSet();
        if (groups.Count == 0) groups = (info.GroupIDList ?? []).ToHashSet();
        return groups;
    }

    // A story choice can skip a puzzle (1040109's 104010974 sets FSV_SkipPuzzle_Infusion), and a group graph then sets
    // the puzzle's values in its place (G302: when FSV_SkipPuzzle_Infusion is 1, the soul-forging statues'
    // FSV_SStatue*). The data strips those tasks, so a step waiting on a value that a graph names next to a skip value
    // that is 1 takes the value, when the skip value is set or when the step is accepted after it.
    public static async ValueTask OnPuzzleSkipped(PlayerInstance player, int floorId)
    {
        if (!ConfigManager.Config.ServerOption.PuzzleFallback) return;
        foreach (var info in player.MissionManager!.GetRunningSubMissionList()
                     .Where(x => x.LevelFloorID == floorId && x.FinishType == MissionFinishTypeEnum.FloorSavedValue)
                     .ToList())
            if (Skipped(player, info))
                await TakeSkippedValue(player, info);
    }

    private static readonly Regex SkipValue = new("\"(FSV_SkipPuzzle_[A-Za-z0-9_]+)\"");

    internal static bool Skipped(PlayerInstance player, SubMissionInfo info)
    {
        if (player.SceneData!.FloorSavedData.GetValueOrDefault(info.LevelFloorID) is not { } values) return false;
        var name = $"\"{info.ParamStr1}\"";
        foreach (var group in GameData.GetFloorInfo(info.LevelFloorID)?.Groups.Values ?? [])
        {
            if (string.IsNullOrEmpty(group.LevelGraph)) continue;
            var text = ReadGraph(group.LevelGraph);
            if (!text.Contains(name)) continue;
            if (SkipValue.Matches(text).Any(m => m.Groups[1].Value != info.ParamStr1 &&
                                                 values.GetValueOrDefault(m.Groups[1].Value) == 1))
                return true;
        }

        return false;
    }

    internal static async ValueTask TakeSkippedValue(PlayerInstance player, SubMissionInfo info)
    {
        if (player.MissionManager!.GetSubMissionStatus(info.ID) != MissionPhaseEnum.Accept) return;
        Log.Warn($"sub-mission {info.ID}: puzzle skipped, setting {info.ParamStr1}={info.ParamInt1} " +
                 $"on floor {info.LevelFloorID}");
        if (player.SceneInstance is { } scene && scene.FloorId == info.LevelFloorID)
            await scene.UpdateFloorSavedValue(info.ParamStr1, info.ParamInt1);
        else
            player.SceneData!.FloorSavedData[info.LevelFloorID][info.ParamStr1] = info.ParamInt1;
        if (player.MissionManager.GetSubMissionStatus(info.ID) == MissionPhaseEnum.Accept)
            await player.MissionManager.FinishSubMission(info.ID);
    }

    // A client graph that sets a server group property may, in the same task list, set a floor custom float
    // FCV_<X> whose server twin FSV_<X> a stripped server graph sets (e.g. FCV_SpiralStairs after the third
    // 雅努斯迷津 board, which 104021309 waits for as FSV_SpiralStairs). The declared saved value follows the float.
    public static async ValueTask OnClientGroupProperty(PlayerInstance player, int groupId, string name, int value)
    {
        if (!ConfigManager.Config.ServerOption.PuzzleFallback) return;
        var scene = player.SceneInstance;
        var graph = scene?.FloorInfo?.Groups.GetValueOrDefault(groupId)?.LevelGraph;
        if (scene == null || string.IsNullOrEmpty(graph)) return;
        var text = ReadGraph(graph);
        if (!text.Contains("SetFloorCustomFloat") || !text.Contains("RequestSetServerGroupProperty")) return;

        List<(string Name, int Value)> mirrors = [];
        try
        {
            foreach (var list in JToken.Parse(text).SelectTokens("$..*").OfType<JArray>())
            {
                var tasks = list.OfType<JObject>().ToList();
                if (!tasks.Any(t => (string?)t["$type"] == "RPG.GameCore.RequestSetServerGroupProperty" &&
                                    (int?)t["GroupID"] == groupId && (string?)t["DynamicName"]?["Value"] == name &&
                                    ((int?)t["Value"] ?? 0) == value)) continue;
                foreach (var task in tasks.Where(t => (string?)t["$type"] == "RPG.GameCore.SetFloorCustomFloat"))
                {
                    var custom = (string?)task["Name"]?["Value"];
                    var set = (double?)task["Value"]?["FixedValue"]?["Value"] ?? 0;
                    if (custom?.StartsWith("FCV_") == true) mirrors.Add(("FSV_" + custom[4..], (int)set));
                }
            }
        }
        catch
        {
            // a graph that does not parse mirrors nothing
            return;
        }

        foreach (var (saved, set) in mirrors.Distinct())
        {
            if (scene.FloorInfo!.FloorSavedValue.All(x => x.Name != saved)) continue;
            if (player.SceneData!.FloorSavedData.GetValueOrDefault(scene.FloorId)?.TryGetValue(saved, out var cur) == true &&
                cur == set) continue;
            Log.Warn($"group {groupId} set {name}={value} with its floor float, setting {saved}={set} on floor {scene.FloorId}");
            await scene.UpdateFloorSavedValue(saved, set);
        }
    }

    private const int RaidEntranceGoto = 6900;

    // A server group whose NPC leads the player into a district that starts a battle (raid 41442's G24 for
    // 104050429, G42 for 104050433), loaded in the middle of the scene: the 4.1 client takes the group refresh but
    // never builds the NPC, so the district and battle never come. It builds them from the scene info, as after a
    // re-login, so the scene is sent again where the player stands.
    public static async ValueTask ReloadForDroppedGroups(PlayerInstance player, IEnumerable<int> accepted)
    {
        if (!ConfigManager.Config.ServerOption.PuzzleFallback) return;
        var scene = player.SceneInstance;
        if (scene?.FloorInfo == null) return;
        var groups = accepted
            .Select(id => GameData.SubMissionInfoData.GetValueOrDefault(id)?.SubMissionInfo)
            .Where(info => info?.LevelFloorID == scene.FloorId)
            .SelectMany(info => info!.GroupIDList ?? [])
            .Where(id => scene.FloorInfo.Groups.GetValueOrDefault(id) is
            {
                LoadSide: GroupLoadSideEnum.Server, NPCList.Count: > 0, LevelGraph.Length: > 0
            } group && ReadGraph(group.LevelGraph) is var text &&
                         text.Contains("RPG.GameCore.AdvDistrictSetupTrigger\"") &&
                         text.Contains("RPG.GameCore.TriggerBattle\""))
            .ToList();
        if (groups.Count == 0) return;
        Log.Warn($"groups {string.Join(",", groups)} on floor {scene.FloorId} lead into a battle district and the client " +
                 "does not build them from a refresh; sending the scene again");
        await player.LoadScene(player.Data.PlaneId, player.Data.FloorId, player.Data.EntryId, player.Data.Pos!,
            player.Data.Rot!, true);
    }

    internal static string ReadGraph(string path)
    {
        return GraphText.GetOrAdd(path, p =>
        {
            var file = Path.Combine(ConfigManager.Config.Path.ResourcePath, p);
            return File.Exists(file) ? File.ReadAllText(file) : "";
        });
    }
}
