using System.Collections.Concurrent;
using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Util;

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

    // Minigame steps the server cannot evaluate are skipped when accepted.
    // A floor saved value that no group graph mentions, no sub-mission finish action sets and the server's
    // own interaction rules (FSV_ML*, JG_ML*) do not cover can never be reached, e.g. a choice made inside a
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

        if (info.FinishType != MissionFinishTypeEnum.FloorSavedValue) return;
        if (info.ParamStr1.StartsWith("FSV_ML") || info.ParamStr1.StartsWith("JG_ML")) return;
        if (MissionSetValues.Value.Contains((info.ParamStr1, info.ParamInt1))) return;
        if (PuzzleGroups(GameData.GetFloorInfo(info.LevelFloorID), info).Count > 0) return;

        Log.Warn($"sub-mission {info.ID}: nothing can set {info.ParamStr1}={info.ParamInt1} " +
                 $"on floor {info.LevelFloorID}, skipping it");
        if (!player.SceneData!.FloorSavedData.TryGetValue(info.LevelFloorID, out var values))
        {
            values = [];
            player.SceneData.FloorSavedData[info.LevelFloorID] = values;
        }

        values[info.ParamStr1] = info.ParamInt1;
        await player.MissionManager.FinishSubMission(info.ID);
    }

    // Activity minigames the story drops into; the server implements none of their finish types.
    private static readonly string[] MinigamePrefixes = ["TarotBook", "FiveDim", "DiceCombat", "LimaoNews"];

    private static readonly Lazy<HashSet<(string, int)>> MissionSetValues = new(() =>
        GameData.SubMissionInfoData.Values
            .SelectMany(x => x.SubMissionInfo?.FinishActionList ?? [])
            .Where(x => x.FinishActionType == FinishActionTypeEnum.SetFloorSavedValue &&
                        x.FinishActionParaString.Count >= 4 && int.TryParse(x.FinishActionParaString[3], out _))
            .Select(x => (x.FinishActionParaString[2], int.Parse(x.FinishActionParaString[3])))
            .ToHashSet());

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

    private static string ReadGraph(string path)
    {
        return GraphText.GetOrAdd(path, p =>
        {
            var file = Path.Combine(ConfigManager.Config.Path.ResourcePath, p);
            return File.Exists(file) ? File.ReadAllText(file) : "";
        });
    }
}
