using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

// ParamInt1/ParamInt2 are the group and prop the player has to break with an overworld attack.
[MissionFinishType(MissionFinishTypeEnum.PropDestruct)]
public class MissionHandlerPropDestruct : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is EntityProp prop && prop.GroupId == info.ParamInt1 && prop.PropInfo.ID == info.ParamInt2)
            await player.MissionManager!.FinishSubMission(info.ID);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is EntityProp prop && prop.GroupId == excel.ParamInt1 && prop.PropInfo.ID == excel.ParamInt2)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}
