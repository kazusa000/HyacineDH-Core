using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishAction.Handler;

// The story moving past older optional main missions (1021201's 102120119 closes 2020103, 2020104 and 2020106 once the
// player gives them up on the client's MissionDeleteWarningDialog): unfinished ones are removed for good.
[MissionFinishAction(FinishActionTypeEnum.DisableMission)]
public class MissionHandlerDisableMission : MissionFinishActionHandler
{
    public override async ValueTask OnHandle(List<int> @params, List<string> paramString, PlayerInstance player)
    {
        foreach (var missionId in @params)
            await player.MissionManager!.DisableMainMission(missionId);
    }
}
