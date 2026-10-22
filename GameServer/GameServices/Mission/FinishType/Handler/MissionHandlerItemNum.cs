using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.ItemNum)]
public class MissionHandlerItemNum : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        var count = 0;
        var item = player.InventoryManager?.GetItem(info.ParamInt1);
        if (item != null) count += item.Count;

        // Holding at least the target is enough: items can arrive several at a time (e.g. from finish
        // actions) before this is evaluated, so the count may skip past the exact number.
        if (count >= info.Progress)
        {
            await player.MissionManager!.FinishSubMission(info.ID);
        }
        else
        {
            if (player.MissionManager?.GetMissionProgress(info.ID) != count)
                await player.MissionManager!.SetMissionProgress(info.ID, count);
        }
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        var count = 0;
        var item = player.InventoryManager?.GetItem(excel.ParamInt1);
        if (item != null) count += item.Count;

        await player.QuestManager!.UpdateQuestProgress(quest.QuestID, count);
    }
}