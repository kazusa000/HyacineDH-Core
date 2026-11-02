using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Scene;

// A core plate lift (1040406's G24 on 20431001, also F30137001/F80701001 G2) reports reaching its first or second stop
// as the group property PlateArrived=1/2, and the stripped server task turns that into the floor's
// FSV_CorePlateArrived / FSV_CorePlateArrived_2. The graphs only send it while the value is still 0, and G69 arms
// 104040651's battle trigger once FSV_CorePlateArrived_2 is 1.
public static class CorePlateArrival
{
    private static readonly Logger Log = new("CorePlate");

    public static async ValueTask OnGroupProperty(PlayerInstance player, int groupId, string name, int value)
    {
        if (name != "PlateArrived" || value is not (1 or 2) || player.SceneInstance is not { } scene ||
            player.SceneData == null) return;
        var key = value == 1 ? "FSV_CorePlateArrived" : "FSV_CorePlateArrived_2";
        var saved = player.SceneData.GetFloorSavedValues(scene.FloorId); // with the floor's defaults
        if (!saved.TryGetValue(key, out var current) || current != 0) return;
        Log.Info($"Group {groupId} PlateArrived={value} on floor {scene.FloorId} sets {key}=1");
        await scene.UpdateFloorSavedValue(key, 1);
    }
}
