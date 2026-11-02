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

        // Groups loaded by this value (1040103's G89, FSV_TimeRewind_TutorialFinish) load now: the step that waits on
        // them was accepted, and its groups synced, before this finish action ran.
        if (player.SceneInstance is { } loaded && loaded.FloorId == floor)
            await loaded.SyncGroupInfo();

        // Groups whose state follows the value (1036002's D4 gashapon, G587) change state now.
        if (player.SceneInstance is { } changed && changed.FloorId == floor)
            await changed.ApplyFloorValueGroupStates(paramString[2], int.Parse(paramString[3]));

        // Jigsaw boards and pieces driven by these values (1030601's G372) follow them now.
        if (player.SceneInstance is { } scene && scene.FloorId == floor)
            foreach (var group in scene.FloorInfo?.Groups.Values.Where(g =>
                         g.PropList.Any(p => p.Name.Contains("JigsawSir"))) ?? [])
                await player.CheckJigsawPieces(group.Id, scene);

        // Steps of a puzzle this value skips (1040109's FSV_SkipPuzzle_Infusion) take their values now.
        if (paramString[2].StartsWith("FSV_SkipPuzzle_") && int.Parse(paramString[3]) == 1)
            await PuzzleFallback.OnPuzzleSkipped(player, floor);

        // Doors a mission step opens by this value (1030701's G37) open now.
        if (paramString[2].StartsWith("MissionFV_") && player.SceneInstance is { } current &&
            current.FloorId == floor)
            foreach (var group in current.FloorInfo?.Groups.Values.Where(g => current.Groups.Contains(g.Id)) ?? [])
                await player.OpenMissionDoors(group, current);
    }
}