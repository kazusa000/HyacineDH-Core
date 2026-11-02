using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Util;
using Newtonsoft.Json.Linq;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.PropState)]
public class MissionHandlerPropState : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (player.SceneInstance?.FloorId != info.LevelFloorID) return; // not a same scene
        var prop = player.SceneInstance.GetEntitiesInGroup<EntityProp>(info.ParamInt1);

        // A jigsaw piece the step waits for to be collected is hidden (TriggerDisable) until the step sends it out: the
        // group graph's server event that does so (1036104's G277, ChangePropState) is stripped. From TriggerEnable the
        // client picks it up (interaction 11000) when the mission's talk sends the piece's custom string; from
        // TriggerDisable the pickup did not match and 103610403 stayed open.
        foreach (var p in prop.Where(p => p.PropInfo.ID == info.ParamInt2 && p.State == PropStateEnum.TriggerDisable &&
                                          p.PropInfo.Name.StartsWith("JigsawPiece") &&
                                          info.ParamInt3 == (int)PropStateEnum.Closed))
            await p.SetState(PropStateEnum.TriggerEnable);

        foreach (var p in prop)
            if (p.PropInfo.ID == info.ParamInt2 &&
                ((int)p.State == info.ParamInt3 || await SettleByGroupGraph(player, info, p) ||
                 await SettleByPropPredicate(player, info, p, prop)))
                await player.MissionManager!.FinishSubMission(info.ID);
            else if (info.ParamInt3 == (int)PropStateEnum.CheckPointDisable ||
                     info.ParamInt3 == (int)PropStateEnum.CheckPointEnable)
                await player.MissionManager!.FinishSubMission(info.ID);
    }

    // A group graph that waits for a condition and then sets props (1021201's fog over the three furnaces, G54's
    // Maze_Chap02_X301_FogVolume_01: WaitPredicateSucc, then ChangePropState) keeps neither its predicate nor its targets
    // in this data, so the server cannot run it. Its condition is the main mission's other prop steps in that group (the
    // furnaces turned to their states): once those are all finished, the prop goes to the state this step waits for.
    private static async ValueTask<bool> SettleByGroupGraph(PlayerInstance player, SubMissionInfo info, EntityProp prop)
    {
        if (player.SceneInstance?.FloorInfo?.Groups.TryGetValue(info.ParamInt1, out var group) != true ||
            string.IsNullOrEmpty(group!.LevelGraph)) return false;
        var graph = PuzzleFallback.ReadGraph(group.LevelGraph);
        if (!graph.Contains("RPG.GameCore.WaitPredicateSucc") || !graph.Contains("RPG.GameCore.ChangePropState"))
            return false;

        GameData.MainMissionData.TryGetValue(info.MainMissionID, out var main);
        var steps = main?.MissionInfo?.SubMissionList.Where(x =>
            x.ID != info.ID && x.FinishType == MissionFinishTypeEnum.PropState && x.ParamInt1 == info.ParamInt1 &&
            x.ParamInt2 != info.ParamInt2).ToList() ?? [];
        if (steps.Count == 0 ||
            steps.Any(x => player.MissionManager!.GetSubMissionStatus(x.ID) != MissionPhaseEnum.Finish)) return false;

        var state = (PropStateEnum)info.ParamInt3;
        if (!prop.Excel.PropStateList.Contains(state)) return false;
        Logger.GetByClassName().Info(
            $"Sub-mission {info.ID}: group {info.ParamInt1} graph settles prop {prop.InstId} {prop.State} -> {state}");
        await prop.SetState(state);
        return true;
    }

    // A group graph that opens one prop once another reaches a state (1040213's G96 bookshelf: LoopWaitServerPropStateChange,
    // then ByComparePropState on the scroll 300002 being Open runs a stripped TriggerEntityServerEvent) cannot run here, and
    // neither can its floor-value branch that unlocks the scroll (FSV_ScrollUnlocked). The step's waypoint is that other
    // prop: a Locked one is closed so the player can interact with it, and once it reaches the state, the step's prop goes
    // to the state this step waits for.
    private static async ValueTask<bool> SettleByPropPredicate(PlayerInstance player, SubMissionInfo info, EntityProp prop,
        List<EntityProp> props)
    {
        if (info.WayPointGroupID != info.ParamInt1 || info.WayPointEntityID == info.ParamInt2) return false;
        if (player.SceneInstance?.FloorInfo?.Groups.TryGetValue(info.ParamInt1, out var group) != true ||
            string.IsNullOrEmpty(group!.LevelGraph)) return false;
        var graph = PuzzleFallback.ReadGraph(group.LevelGraph);
        if (!graph.Contains("RPG.GameCore.LoopWaitServerPropStateChange") ||
            !graph.Contains("RPG.GameCore.ByComparePropState")) return false;
        var key = props.FirstOrDefault(p => p.PropInfo.ID == info.WayPointEntityID);
        if (key == null) return false;

        PropStateEnum? want = null;
        try
        {
            foreach (var t in JToken.Parse(graph).SelectTokens("$..*").OfType<JObject>()
                         .Where(t => (string?)t["$type"] == "RPG.GameCore.ByComparePropState"))
                if (t["TargetType"]?["Targets"] is JArray targets && targets.Any(x => (int?)x == key.PropInfo.ID) &&
                    Enum.TryParse<PropStateEnum>((string?)t["State"], out var s))
                    want = s;
        }
        catch
        {
            // a graph that does not parse settles nothing
            return false;
        }

        if (want == null) return false;
        var log = Logger.GetByClassName();
        if (key.State == PropStateEnum.Locked && want != PropStateEnum.Locked &&
            key.Excel.PropStateList.Contains(PropStateEnum.Closed))
        {
            log.Info($"Sub-mission {info.ID}: group {info.ParamInt1} unlocks prop {key.InstId} for its {want} predicate");
            await key.SetState(PropStateEnum.Closed);
            return false;
        }

        if (key.State != want) return false;
        var state = (PropStateEnum)info.ParamInt3;
        if (!prop.Excel.PropStateList.Contains(state)) return false;
        log.Info($"Sub-mission {info.ID}: group {info.ParamInt1} prop {key.InstId} is {want}, " +
                 $"settling prop {prop.InstId} {prop.State} -> {state}");
        await prop.SetState(state);
        return true;
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (player.SceneInstance?.FloorId != excel.MazeFloorID) return; // not a same scene
        var prop = player.SceneInstance.GetEntitiesInGroup<EntityProp>(excel.ParamInt1);

        foreach (var p in prop)
            if (p.PropInfo.ID == excel.ParamInt2 && (int)p.State == excel.ParamInt3)
                await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
            else if (excel.ParamInt3 == (int)PropStateEnum.CheckPointDisable ||
                     excel.ParamInt3 == (int)PropStateEnum.CheckPointEnable)
                await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}