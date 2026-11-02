using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.EnterRaidScene)]
public class MissionHandlerEnterRaidScene : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (player.RaidManager!.RaidData.CurRaidId != info.ParamInt1)
            // change raid
            await player.RaidManager!.EnterRaid(info.ParamInt1, 0);
        // Finish first so the next step is already in the scene's mission info when the floor loads: its client
        // group is then set up before the player is placed, and a district trigger around the entry point fires.
        await player.MissionManager!.FinishSubMission(info.ID);
        await player.EnterScene(info.ParamInt2, 0, true);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        // this type wont be used in quest
        await ValueTask.CompletedTask;
    }
}