using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet.Recv.MovieRacing;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

// A movie racing level ended (UpdateMovieRacingDataCsReq): its quests ask for a score on a level.
[MissionFinishType(MissionFinishTypeEnum.FinishMovieGame)]
public class MissionHandlerFinishMovieGame : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is FGCDAFADHIM record && record.Level == info.ParamInt2 &&
            record.HPKMEMFPLOH >= info.ParamInt1)
            await player.MissionManager!.FinishSubMission(info.ID);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is FGCDAFADHIM record && HandlerUpdateMovieRacingDataCsReq.IsCleared(excel, record))
            await player.QuestManager!.FinishQuest(quest.QuestID, false);
    }
}