using HyacineCore.Server.Data;
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
        var raid = player.RaidManager!;
        if (raid.RaidData.CurRaidId != info.ParamInt1)
            // change raid
            await raid.EnterRaid(info.ParamInt1, 0);
        // Finish first so the next step is already in the scene's mission info when the floor loads: its client
        // group is then set up before the player is placed, and a district trigger around the entry point fires.
        await player.MissionManager!.FinishSubMission(info.ID);

        // Moving between floors of the active raid must not suspend its parent storyline. The client clears
        // scene groups on storyline changes; leaving before the floor packet and resuming after it can lose them.
        var sameRaid = info.ParamInt1 > 0 && raid.RaidData.CurRaidId == info.ParamInt1 &&
                       GameData.MapEntranceData.TryGetValue(info.ParamInt2, out var entrance) &&
                       entrance.PlaneID == player.Data.PlaneId;
        var storyLineId = sameRaid ? (player.StoryLineManager?.StoryLineData.CurStoryLineId ?? 0) : 0;
        await player.EnterScene(info.ParamInt2, 0, true, storyLineId);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        // this type wont be used in quest
        await ValueTask.CompletedTask;
    }
}
