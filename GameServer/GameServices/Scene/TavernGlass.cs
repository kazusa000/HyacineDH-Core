using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;

namespace HyacineCore.Server.GameServer.Game.Scene;

internal static class TavernGlass
{
    private const int Floor = 20502001;
    private const int Group = 141;
    private const int Step = 105400175;
    private const string SavedValue = "FSV_OneSideGlass";

    internal static bool IsGlass(EntityProp prop) =>
        prop.Scene.FloorId == Floor && prop.GroupId == Group && prop.InstId == 300001 &&
        prop.PropInfo.PropID == 105098;

    internal static bool UsesHit(SubMissionInfo info) =>
        info.ID == Step && info.LevelFloorID == Floor &&
        info.FinishType == MissionFinishTypeEnum.FloorSavedValue &&
        info.ParamStr1 == SavedValue && info.ParamInt1 == 2;

    internal static async ValueTask OnFloorValueChange(SceneInstance scene, string name)
    {
        if (scene.FloorId != Floor || name != SavedValue) return;
        foreach (var prop in scene.GetEntitiesInGroup<EntityProp>(Group))
            await ApplyState(prop, scene.IsLoaded);
    }

    // The native glass only installs its hit box in Open. Locked plays the break and disables the collider.
    internal static ValueTask ApplyState(EntityProp prop, bool sendPacket)
    {
        if (!IsGlass(prop)) return ValueTask.CompletedTask;
        PropStateEnum? state = prop.Scene.GetFloorSavedValue(SavedValue) switch
        {
            1 => PropStateEnum.Open,
            2 => PropStateEnum.Locked,
            _ => null
        };
        return state is { } next && next != prop.State
            ? prop.SetState(next, sendPacket)
            : ValueTask.CompletedTask;
    }

    internal static async ValueTask OnInteract(EntityProp prop, int interactId)
    {
        var scene = prop.Scene;
        // The client's hit box sends 1120 after a normal attack hits the armed glass.
        if (interactId != 1120 || prop.State != PropStateEnum.Open || scene.GetFloorSavedValue(SavedValue) != 1 ||
            scene.Player.MissionManager!.GetSubMissionStatus(Step) != MissionPhaseEnum.Accept) return;

        await prop.SetState(PropStateEnum.Locked);
        await scene.UpdateFloorSavedValue(SavedValue, 2);
    }
}
