using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

namespace HyacineCore.Server.GameServer.Game.Mission.FinishAction.Handler;

[MissionFinishAction(FinishActionTypeEnum.SetFloorSavedValue)]
public class MissionHandlerSetFloorSavedValue : MissionFinishActionHandler
{
    public override async ValueTask OnHandle(List<int> @params, List<string> paramString, PlayerInstance player)
    {
        _ = int.TryParse(paramString[0], out var plane);
        _ = int.TryParse(paramString[1], out var floor);
        player.SceneData!.FloorSavedData.TryGetValue(floor, out var value);
        if (value == null)
        {
            value = [];
            player.SceneData.FloorSavedData[floor] = value;
        }

        value[paramString[2]] = int.Parse(paramString[3]); // ParamString[2] is the key
        await player.SendPacket(
            new PacketUpdateFloorSavedValueNotify(paramString[2], int.Parse(paramString[3]), player));

        player.TaskManager?.SceneTaskTrigger.TriggerFloor(plane, floor);

        // Jigsaw boards and pieces driven by these values (1030601's G372) follow them now.
        if (player.SceneInstance is { } scene && scene.FloorId == floor)
            foreach (var group in scene.FloorInfo?.Groups.Values.Where(g =>
                         g.PropList.Any(p => p.Name.Contains("JigsawSir"))) ?? [])
                await player.CheckJigsawPieces(group.Id, scene);
    }
}