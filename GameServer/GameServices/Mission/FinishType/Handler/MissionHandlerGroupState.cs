using HyacineCore.Server.Data.Config;
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
        if (player.SceneInstance?.FloorId != info.LevelFloorID) return;
        if (player.SceneInstance.GetGroupState(info.ParamInt1) == info.ParamInt2)
            await player.MissionManager!.FinishSubMission(info.ID);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (player.SceneInstance?.FloorId != excel.MazeFloorID) return;
        if (player.SceneInstance.GetGroupState(excel.ParamInt1) == excel.ParamInt2)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}
