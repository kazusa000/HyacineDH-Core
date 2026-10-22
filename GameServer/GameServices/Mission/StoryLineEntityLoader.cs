using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Config.Scene;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;

namespace HyacineCore.Server.GameServer.Game.Mission;

public class StoryLineEntityLoader(SceneInstance scene) : SceneEntityLoader(scene)
{
    public int DimensionId;

    public override async ValueTask LoadEntity()
    {
        if (Scene.IsLoaded) return;

        var storyId = Scene.Player.StoryLineManager?.StoryLineData.CurStoryLineId ?? 0;

        // Floors a story line has no entry for (raid floors, for one) show their default dimension.
        GameData.StoryLineFloorDataData.TryGetValue(storyId, out var floorData);
        StoryLineFloorDataExcel? floorInfo = null;
        floorData?.TryGetValue(Scene.FloorInfo?.FloorID ?? 0, out floorInfo);
        floorInfo ??= new StoryLineFloorDataExcel { DimensionID = 0 }; // Default

        var dim = Scene.FloorInfo?.DimensionList.Find(d => d.ID == floorInfo.DimensionID);
        if (dim == null) return;

        DimensionId = dim.ID;

        LoadGroups.AddRange(dim.GroupIDList);

        foreach (var group in Scene.FloorInfo?.Groups.Values!) // Sanity check in SceneInstance
        {
            if (group.LoadSide == GroupLoadSideEnum.Client) continue;
            if (group.GroupName.Contains("TrainVisitor")) continue;
            await LoadGroup(group);
        }

        Scene.IsLoaded = true;
    }

    public override async ValueTask<List<BaseGameEntity>?> LoadGroup(GroupInfo info, bool forceLoad = false)
    {
        if (!LoadGroups.Contains(info.Id)) return null;
        return await base.LoadGroup(info, forceLoad);
    }
}