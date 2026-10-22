using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

// Kill Progress monsters out of the groups in ParamIntList.
[MissionFinishType(MissionFinishTypeEnum.KillMonsterGroupList)]
public class MissionHandlerKillMonsterGroupList : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is not EntityMonster { IsAlive: false } monster) return;
        if (monster.Scene.FloorId != info.LevelFloorID || info.ParamIntList?.Contains(monster.GroupId) != true) return;

        await player.MissionManager!.AddMissionProgress(info.ID, 1);
        if (player.MissionManager.GetMissionProgress(info.ID) >= Math.Max(info.Progress, 1))
            await player.MissionManager.FinishSubMission(info.ID);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        // this type wont be used in quest
        await ValueTask.CompletedTask;
    }
}
