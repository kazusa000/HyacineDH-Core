using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;

namespace HyacineCore.Server.GameServer.Game.Scene;

internal static class TavernStellaJade
{
    private const int Floor = 20502001;
    private const int Group = 80;
    private const int Step = 105400132;
    private const string SavedValue = "FSV_StellaJade";

    internal static bool IsJade(EntityProp prop) =>
        prop.Scene.FloorId == Floor && prop.GroupId == Group && prop.InstId == 300001 &&
        prop.PropInfo.PropID == 105065;

    internal static bool UsesNativeResult(SubMissionInfo info) =>
        info.LevelFloorID == Floor && info.FinishType == MissionFinishTypeEnum.FloorSavedValue &&
        info.ParamStr1 == SavedValue && (info.ID, info.ParamInt1) is (105400183, 2) or (Step, 3);

    internal static ValueTask RestoreState(EntityProp prop)
    {
        if (!IsJade(prop)) return ValueTask.CompletedTask;
        PropStateEnum? state = prop.Scene.GetFloorSavedValue(SavedValue) switch
        {
            1 => PropStateEnum.EventClose,
            2 or 3 => PropStateEnum.EventOpen,
            _ => null
        };
        return state is { } next && next != prop.State
            ? prop.SetState(next, false)
            : ValueTask.CompletedTask;
    }

    // The client counts hits and asks to open the prop once its own threshold is reached.
    internal static async ValueTask OnInteract(EntityProp prop, int interactId)
    {
        var scene = prop.Scene;
        if (interactId != 118191 || prop.State != PropStateEnum.EventClose ||
            scene.GetFloorSavedValue(SavedValue) != 1 ||
            scene.Player.MissionManager!.GetSubMissionStatus(Step) != MissionPhaseEnum.Accept) return;

        await prop.SetState(PropStateEnum.EventOpen);
        await scene.UpdateFloorSavedValue(SavedValue, 2);
    }

    // The G80 graph sends this property after the first explosion and its input lock have ended.
    internal static async ValueTask OnGroupProperty(SceneInstance scene, int groupId, string name, int value)
    {
        if (scene.FloorId != Floor || groupId != Group || name != "StellaJadeBeHit" || value != 1 ||
            scene.GetFloorSavedValue(SavedValue) != 2 ||
            scene.Player.MissionManager!.GetSubMissionStatus(Step) != MissionPhaseEnum.Accept) return;

        await scene.UpdateFloorSavedValue(SavedValue, 3);
    }
}
