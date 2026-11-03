using HyacineCore.Server.Data.Config;
using HyacineCore.Server.Enums.Mission;

namespace HyacineCore.Server.GameServer.Game.Scene;

public static class GechuanMoonBoss
{
    private const int Floor = 20503001;
    private const int Group = 5;
    private const int Step = 105400106;
    private const string SavedValue = "FSV_MoonBossClear";

    public static bool UsesGroupResult(SubMissionInfo info) =>
        info.ID == Step && info.LevelFloorID == Floor &&
        info.FinishType == MissionFinishTypeEnum.FloorSavedValue &&
        info.ParamStr1 == SavedValue && info.ParamInt1 == 2;

    // The native exit sends MoonBossClear after the three switches and the last chest.
    // The step waits on the floor value and resets it to 0 as part of its finish actions.
    public static async ValueTask OnGroupProperty(SceneInstance scene, int groupId, string name, int value)
    {
        if (scene.FloorId != Floor || groupId != Group || name != "MoonBossClear" || value != 1 ||
            scene.GetFloorSavedValue(SavedValue) != 1 ||
            scene.GetGroupProperty(Group, "LG_110001_Phase") != 6 ||
            scene.GetGroupProperty(Group, "LG_110001__129_ChestStateL_Auto") != 2 ||
            !scene.Player.MissionManager!.GetRunningSubMissionList().Any(UsesGroupResult)) return;

        await scene.UpdateFloorSavedValue(SavedValue, 2);
    }
}
