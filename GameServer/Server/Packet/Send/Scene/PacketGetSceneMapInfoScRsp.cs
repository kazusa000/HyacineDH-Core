using HyacineCore.Server.Data;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketGetSceneMapInfoScRsp : BasePacket
{
    public PacketGetSceneMapInfoScRsp(GetSceneMapInfoCsReq req, PlayerInstance player) : base(
        CmdIds.GetSceneMapInfoScRsp)
    {
        var rsp = new GetSceneMapInfoScRsp
        {
            ContentId = req.ContentId,
            EntryStoryLineId = req.EntryStoryLineId
        };

        foreach (var floorId in req.FloorIdList)
        {
            var mazeMap = new SceneMapInfo
            {
                FloorId = floorId
                //DimensionId = (uint)(player.SceneInstance?.EntityLoader is StoryLineEntityLoader loader ? loader.DimensionId
                //    : 0)
            };
            var mapDatas = GameData.MapEntranceData.Values.Where(x => x.FloorID == floorId).ToList();

            if (mapDatas.Count == 0)
            {
                rsp.SceneMapInfo.Add(mazeMap);
                continue;
            }

            var mapData = mapDatas.RandomElement();
            GameData.GetFloorInfo(mapData.PlaneID, mapData.FloorID, out var floorInfo);
            if (floorInfo == null)
            {
                rsp.SceneMapInfo.Add(mazeMap);
                continue;
            }

            mazeMap.ChestList.Add(new ChestInfo
            {
                ExistNum = 1,
                ChestType = ChestType.MapInfoChestTypeNormal
            });

            mazeMap.ChestList.Add(new ChestInfo
            {
                ExistNum = 1,
                ChestType = ChestType.MapInfoChestTypePuzzle
            });

            mazeMap.ChestList.Add(new ChestInfo
            {
                ExistNum = 1,
                ChestType = ChestType.MapInfoChestTypeChallenge
            });

            foreach (var groupInfo in floorInfo.Groups.Values) // all the icons on the map
            {
                var mazeGroup = new MazeGroup
                {
                    GroupId = (uint)groupInfo.Id
                };

                mazeMap.MazeGroupList.Add(mazeGroup);
            }

            foreach (var teleport in floorInfo.CachedTeleports.Values)
                mazeMap.UnlockTeleportList.Add((uint)teleport.MappingInfoID);

            foreach (var prop in floorInfo.UnlockedCheckpoints)
            {
                var mazeProp = new MazePropState
                {
                    GroupId = (uint)prop.AnchorGroupID,
                    ConfigId = (uint)prop.ID,
                    State = (uint)PropStateEnum.CheckPointEnable
                };
                mazeMap.MazePropList.Add(mazeProp);
            }

            // Saved and live prop states plus the floor's saved values, as March7thHoney 4.2 sends them; the map
            // only knew the teleport anchors before.
            void AddProp(int groupId, int propId, PropStateEnum state)
            {
                if (mazeMap.MazePropList.Any(x => x.GroupId == (uint)groupId && x.ConfigId == (uint)propId)) return;
                mazeMap.MazePropList.Add(new MazePropState
                    { GroupId = (uint)groupId, ConfigId = (uint)propId, State = (uint)state });
            }

            if (player.SceneInstance?.FloorId == (int)floorId)
                foreach (var prop in player.SceneInstance.Entities.Values.OfType<EntityProp>())
                    AddProp(prop.GroupId, prop.InstId, prop.State);

            if (player.SceneData!.ScenePropData.TryGetValue((int)floorId, out var floorProps))
                foreach (var (groupId, props) in floorProps)
                foreach (var prop in props)
                    AddProp(groupId, prop.PropId, prop.State);

            if (player.SceneData.FloorSavedData.TryGetValue((int)floorId, out var savedValues))
                foreach (var (key, value) in savedValues)
                    mazeMap.FloorSavedData[key] = value;

            if (!ConfigManager.Config.ServerOption.AutoLightSection)
            {
                player.SceneData!.UnlockSectionIdList.TryGetValue(mapData.FloorID, out var sections);
                foreach (var section in sections ?? []) mazeMap.LightenSectionList.Add((uint)section);
            }
            else
            {
                mazeMap.LightenSectionList.AddRange(floorInfo.MapSections.Select(x => (uint)x));
            }

            rsp.SceneMapInfo.Add(mazeMap);
        }

        SetData(rsp);
    }
}
