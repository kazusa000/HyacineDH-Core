using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.GameServer.Game.Player;

namespace HyacineCore.Server.GameServer.Game.Task;

public class SceneTaskTrigger(PlayerInstance player)
{
    public PlayerInstance Player { get; } = player;

    private readonly HashSet<int> _triggeringFloors = [];

    // Runs the level graphs of the floor's loaded groups. Groups whose load conditions are not met do
    // not exist for the client either, and their graphs may move the player (EnterMap) back into
    // this same floor, which used to recurse until the stack overflowed.
    public void TriggerFloor(int planeId, int floorId, IReadOnlyCollection<int>? loadedGroups = null)
    {
        GameData.GetFloorInfo(planeId, floorId, out var floor);
        if (floor == null) return;
        loadedGroups ??= Player.SceneInstance?.FloorId == floorId ? Player.SceneInstance.Groups : null;
        if (loadedGroups == null) return;
        if (!_triggeringFloors.Add(floorId)) return;

        try
        {
            foreach (var group in floor.Groups.Values)
            {
                if (group.LevelGraphConfig == null || !loadedGroups.Contains(group.Id)) continue;
                foreach (var task in group.LevelGraphConfig.OnInitSequece)
                    Player.TaskManager?.LevelTask.TriggerInitAct(task, new SubMissionData(0), group);

                foreach (var task in group.LevelGraphConfig.OnStartSequece)
                    Player.TaskManager?.LevelTask.TriggerStartAct(task, new SubMissionData(0), group);
            }
        }
        finally
        {
            _triggeringFloors.Remove(floorId);
        }
    }

    public void TriggerGroup(int planeId, int floorId, int groupId)
    {
        GameData.GetFloorInfo(planeId, floorId, out var floor);
        if (floor == null) return;
        if (floor.Groups.TryGetValue(groupId, out var group))
        {
            if (group.LevelGraphConfig == null) return;
            foreach (var task in group.LevelGraphConfig.OnInitSequece)
                Player.TaskManager?.LevelTask.TriggerInitAct(task, new SubMissionData(0), group);

            foreach (var task in group.LevelGraphConfig.OnStartSequece)
                Player.TaskManager?.LevelTask.TriggerStartAct(task, new SubMissionData(0), group);
        }
    }
}