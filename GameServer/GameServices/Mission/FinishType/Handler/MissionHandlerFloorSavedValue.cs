using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.FloorSavedValue)]
public class MissionHandlerFloorSavedValue : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (player.SceneData?.FloorSavedData.TryGetValue(info.LevelFloorID, out var floor) == true)
            if (floor.TryGetValue(info.ParamStr1, out var value))
                if (value == info.ParamInt1)
                {
                    await player.MissionManager!.FinishSubMission(info.ID);
                    return;
                }

        // A step of a puzzle that a story choice skipped (FSV_SkipPuzzle_*) takes its value.
        if (ConfigManager.Config.ServerOption.PuzzleFallback && player.SceneData != null &&
            PuzzleFallback.Skipped(player, info))
            await PuzzleFallback.TakeSkippedValue(player, info);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        // this type wont be used in quest
        await ValueTask.CompletedTask;
    }
}