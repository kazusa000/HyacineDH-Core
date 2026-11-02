using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Util;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

// ParamInt1 is the group on the sub-mission's floor, ParamInt2 the state it has to reach.
[MissionFinishType(MissionFinishTypeEnum.GroupState)]
public class MissionHandlerGroupState : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (player.SceneInstance is not { } scene || scene.FloorId != info.LevelFloorID) return;
        var state = scene.GetGroupState(info.ParamInt1);
        if (state == info.ParamInt2)
        {
            await player.MissionManager!.FinishSubMission(info.ID);
            return;
        }

        // A thief spirit's catch (104011207, G629's QteCatchGhostRewind): once it has escaped into hiding (state 1)
        // the catch is a client QTE whose action button the 4.1 client never shows here (canInteract and in camera,
        // the button stays off, like 104010572's rewind button). With the puzzle fallback on, the catch is taken as
        // made; the state change runs this check again and finishes the step.
        if (ConfigManager.Config.ServerOption.PuzzleFallback && state >= 1 && state < info.ParamInt2 &&
            scene.FloorInfo?.Groups.GetValueOrDefault(info.ParamInt1) is { LevelGraph: { Length: > 0 } graph } &&
            PuzzleFallback.ReadGraph(graph).Contains("RPG.GameCore.QteCatchGhostRewind"))
        {
            Logger.GetByClassName().Warn($"sub-mission {info.ID}: group {info.ParamInt1} catch QTE in state {state}, " +
                                         $"taking it to {info.ParamInt2}");
            await scene.UpdateGroupState(info.ParamInt1, info.ParamInt2);
        }
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (player.SceneInstance?.FloorId != excel.MazeFloorID) return;
        if (player.SceneInstance.GetGroupState(excel.ParamInt1) == excel.ParamInt2)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}
