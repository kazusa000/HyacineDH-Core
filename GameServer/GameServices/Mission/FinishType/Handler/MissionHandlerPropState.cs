using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.PropState)]
public class MissionHandlerPropState : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (player.SceneInstance?.FloorId != info.LevelFloorID) return; // not a same scene
        var prop = player.SceneInstance.GetEntitiesInGroup<EntityProp>(info.ParamInt1);

        foreach (var p in prop)
            if (p.PropInfo.ID == info.ParamInt2 &&
                ((int)p.State == info.ParamInt3 || await SettleByGroupGraph(player, info, p)))
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