using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishType.Handler;

[MissionFinishType(MissionFinishTypeEnum.FiveDimFinish)]
public class MissionHandlerFiveDimFinish : MissionHandlerGroupPropertyValue
{
    public static bool HasServerProperty(SubMissionInfo info)
    {
        var group = GameData.GetFloorInfo(info.LevelFloorID)?.Groups.GetValueOrDefault(info.ParamInt1);
        return group?.GroupPropertyMap.Values.Any(property => property.Name == info.ParamStr1 &&
                                                             property.Side == GroupPropertySideEnum.ServerClient) == true;
    }

    public override ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg) =>
        HasServerProperty(info) ? base.HandleMissionFinishType(player, info, arg) : ValueTask.CompletedTask;
}
