using Google.Protobuf;
using Newtonsoft.Json.Linq;
using HyacineCore.Server.Data;
using HyacineCore.Server.Database.Scene;
using HyacineCore.Server.Database;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Scene;

// The native client computes the room transform and sends it in RotateMapInfo. Preserve those
// wire fields rather than guessing angles from the obfuscated field names. State is isolated
// by floor and by the two identifiers carried by EnterMapRotationRegionCsReq.
public class MapRotationManager(PlayerInstance player)
{
    // Keep the pre-existing local server capacity. This is not a claim about every official room.
    private const uint Capacity = 5;

    private static GetMapRotationDataScRsp EmptyState() => new()
    {
        EnergyInfo = new RotaterEnergyInfo { MaxNum = Capacity },
        RogueMap = new RotateMapInfo { CBGJJJMGACI = new Vector4 { W = 1 }, MKNFGECIECC = new Vector() }
    };

    private MapRotationFloorData? Floor(bool create = false)
    {
        if (player.SceneInstance is not { } scene || player.SceneData == null) return null;
        player.SceneData.MapRotationData ??= [];
        if (player.SceneData.MapRotationData.TryGetValue(scene.FloorId, out var floor)) return floor;
        if (!create) return null;
        floor = new MapRotationFloorData();
        player.SceneData.MapRotationData[scene.FloorId] = floor;
        return floor;
    }

    public GetMapRotationDataScRsp Read()
    {
        var floor = Floor();
        return floor != null && floor.Rooms.TryGetValue(floor.CurrentRoom, out var bytes)
            ? GetMapRotationDataScRsp.Parser.ParseFrom(bytes)
            : EmptyState();
    }

    private void Save(GetMapRotationDataScRsp state)
    {
        var floor = Floor() ?? throw new InvalidOperationException("No rotation room selected");
        floor.Rooms[floor.CurrentRoom] = state.ToByteArray();
        DatabaseHelper.ToSaveUidList.SafeAdd(player.Uid);
    }

    public void Deactivate()
    {
        var floor = Floor();
        if (floor?.Rooms.ContainsKey(floor.CurrentRoom) != true) return;
        var state = Read();
        state.EPBFBCKPFDI = false;
        Save(state);
    }

    private static bool ValidMotion(MotionInfo? motion) => motion?.Pos != null && motion.Rot != null;
    private static bool ValidMap(RotateMapInfo? map) => map?.MKNFGECIECC != null && map.CBGJJJMGACI != null &&
        float.IsFinite(map.CBGJJJMGACI.X) && float.IsFinite(map.CBGJJJMGACI.Y) &&
        float.IsFinite(map.CBGJJJMGACI.Z) && float.IsFinite(map.CBGJJJMGACI.W) &&
        (Math.Abs(map.CBGJJJMGACI.X) + Math.Abs(map.CBGJJJMGACI.Y) + Math.Abs(map.CBGJJJMGACI.Z) + Math.Abs(map.CBGJJJMGACI.W) > 0);

    private uint Move(MotionInfo motion)
    {
        player.Data.Pos = motion.Pos.ToPosition();
        player.Data.Rot = motion.Rot.ToPosition();
        DatabaseHelper.ToSaveUidList.SafeAdd(player.Uid);
        var scene = player.SceneInstance!;
        scene.ClientPosVersion = scene.ClientPosVersion == uint.MaxValue ? 1 : scene.ClientPosVersion + 1;
        return scene.ClientPosVersion;
    }

    public async ValueTask Send(int opcode, IMessage message)
    {
        var packet = new BasePacket(checked((ushort)opcode));
        packet.SetData(message);
        await player.SendPacket(packet);
    }

    private ValueTask Notify(GetMapRotationDataScRsp state) => Send(CmdIds.UpdateMapRotationDataScNotify,
        new UpdateMapRotationDataScNotify
        {
            RogueMap = state.RogueMap.Clone(), EnergyInfo = state.EnergyInfo.Clone(),
            EPBFBCKPFDI = state.EPBFBCKPFDI, KMINHNLKPIC = state.KMINHNLKPIC, LDIJAKPKLIN = state.LDIJAKPKLIN,
            EFMLPKJCPBJ = { state.EFMLPKJCPBJ }, HNPDHHKNKMB = { state.HNPDHHKNKMB }
        });

    private string Resource() => player.SceneInstance == null ? "" : Path.Combine(ConfigManager.Config.Path.ResourcePath,
        "Config", "LevelOutput", "RotatableRegion", $"RotatableRegion_Floor_{player.SceneInstance.FloorId}.json");

    // KMINHNLKPIC is the room's pose: the index into the region's Poses whose rotation (RotationAxis, RotationAngle
    // in degrees) the room is turned to. RotateMapCsReq carries only the rotation, so the pose is looked up here; the
    // client rebuilds the room from it after a battle, and with 0 it fell back to pose 0 with the room still turned
    // (1030701, 1031101), leaving the volumes of the real pose unusable.
    private uint? Pose(int region, Vector4 rotation)
    {
        var resource = Resource();
        if (!File.Exists(resource)) return null;
        if (JObject.Parse(File.ReadAllText(resource))["RotatableRegions"] is not JArray regions ||
            region < 0 || region >= regions.Count || regions[region]["Poses"] is not JArray poses) return null;
        for (var i = 0; i < poses.Count; i++)
        {
            var axis = poses[i]["RotationAxis"];
            var half = (poses[i]["RotationAngle"]?.Value<double>() ?? 0) * Math.PI / 360;
            var sin = Math.Sin(half);
            var dot = (axis?["X"]?.Value<double>() ?? 0) * sin * rotation.X +
                      (axis?["Y"]?.Value<double>() ?? 0) * sin * rotation.Y +
                      (axis?["Z"]?.Value<double>() ?? 0) * sin * rotation.Z + Math.Cos(half) * rotation.W;
            if (Math.Abs(dot) > 0.999) return (uint)i;
        }
        return null;
    }

    public async ValueTask Enter(EnterMapRotationRegionCsReq req)
    {
        if (!ValidMotion(req.Motion) || !File.Exists(Resource()))
        {
            await Send(CmdIds.EnterMapRotationRegionScRsp, new EnterMapRotationRegionScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        var floor = Floor(true)!;
        // The room is the region alone; the first slot once held KMINHNLKPIC (the pose) and stays 0 so saved rooms
        // keep their keys.
        var key = $"0:{req.LDIJAKPKLIN}";
        if (!floor.Rooms.ContainsKey(key) && floor.Rooms.Count >= 128)
        {
            await Send(CmdIds.EnterMapRotationRegionScRsp, new EnterMapRotationRegionScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        floor.CurrentRoom = key;
        var state = Read();
        if (!floor.Rooms.ContainsKey(key)) state.EnergyInfo.CurNum = Capacity;
        state.KMINHNLKPIC = req.KMINHNLKPIC;
        state.LDIJAKPKLIN = (int)req.LDIJAKPKLIN;
        state.EPBFBCKPFDI = true;
        Save(state);
        await Send(CmdIds.EnterMapRotationRegionScRsp, new EnterMapRotationRegionScRsp
        {
            Motion = req.Motion.Clone(), ClientPosVersion = Move(req.Motion), EnergyInfo = state.EnergyInfo.Clone(),
            KMINHNLKPIC = state.KMINHNLKPIC, LDIJAKPKLIN = (uint)state.LDIJAKPKLIN
        });
        await Notify(state);
    }

    public async ValueTask Rotate(RotateMapCsReq req)
    {
        var state = Read();
        if (!state.EPBFBCKPFDI || !ValidMotion(req.Motion) || !ValidMap(req.RogueMap))
        {
            await Send(CmdIds.RotateMapScRsp, new RotateMapScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        state.RogueMap = req.RogueMap.Clone();
        state.KMINHNLKPIC = Pose(state.LDIJAKPKLIN, req.RogueMap.CBGJJJMGACI) ?? state.KMINHNLKPIC;
        Save(state);
        await Send(CmdIds.RotateMapScRsp, new RotateMapScRsp { Motion = req.Motion.Clone(), ClientPosVersion = Move(req.Motion) });
        await Notify(state);
    }

    public async ValueTask Reset(ResetMapRotationRegionCsReq req)
    {
        var state = Read();
        if (!state.EPBFBCKPFDI || !ValidMotion(req.Motion) || (req.RogueMap != null && !ValidMap(req.RogueMap)))
        {
            await Send(CmdIds.ResetMapRotationRegionScRsp, new ResetMapRotationRegionScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        // The reset request carries the client's target transform and motion; do not replace it
        // with a made-up origin. Reset only this room, not other rooms on the same floor.
        state.RogueMap = req.RogueMap?.Clone() ?? EmptyState().RogueMap;
        state.KMINHNLKPIC = Pose(state.LDIJAKPKLIN, state.RogueMap.CBGJJJMGACI) ?? 0;
        state.EFMLPKJCPBJ.Clear();
        state.HNPDHHKNKMB.Clear();
        state.EnergyInfo.CurNum = Capacity;
        Save(state);
        await Send(CmdIds.ResetMapRotationRegionScRsp, new ResetMapRotationRegionScRsp { Motion = req.Motion.Clone(), ClientPosVersion = Move(req.Motion) });
        await Notify(state);
    }

    public async ValueTask Leave(LeaveMapRotationRegionCsReq req)
    {
        var state = Read();
        if (!ValidMotion(req.Motion) || player.SceneInstance == null)
        {
            await Send(CmdIds.LeaveMapRotationRegionScRsp, new LeaveMapRotationRegionScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        state.EPBFBCKPFDI = false;
        var floor = Floor();
        if (floor?.Rooms.ContainsKey(floor.CurrentRoom) == true) Save(state);
        await Send(CmdIds.LeaveMapRotationRegionScRsp, new LeaveMapRotationRegionScRsp { Motion = req.Motion.Clone(), ClientPosVersion = Move(req.Motion) });
        await Notify(state);
    }

    public async ValueTask Deploy(DeployRotatorCsReq req)
    {
        var state = Read();
        var rotater = req.RotaterData;
        var existing = rotater == null ? null : state.EFMLPKJCPBJ.FirstOrDefault(x => x.GroupId == rotater.GroupId && x.HIIHFHPBJKH == rotater.HIIHFHPBJKH);
        var valid = state.EPBFBCKPFDI && rotater != null && float.IsFinite(rotater.BAGKGNMEKKE) && (existing != null || state.EnergyInfo.CurNum > 0);
        if (valid && existing == null)
        {
            state.EFMLPKJCPBJ.Add(rotater!.Clone());
            state.EnergyInfo.CurNum--;
            Save(state);
        }
        await Send(CmdIds.DeployRotatorScRsp, new DeployRotatorScRsp
        {
            Retcode = valid ? 0u : (uint)Retcode.RetReqParaInvalid,
            RotaterData = (existing ?? rotater)?.Clone(), EnergyInfo = state.EnergyInfo.Clone()
        });
        if (valid) await Notify(state);
    }

    public async ValueTask Remove(RemoveRotatorCsReq req)
    {
        var state = Read();
        if (!state.EPBFBCKPFDI || req.RotaterData == null)
        {
            await Send(CmdIds.RemoveRotaterScRsp, new RemoveRotaterScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        var existing = state.EFMLPKJCPBJ.FirstOrDefault(x => x.GroupId == req.RotaterData.GroupId && x.HIIHFHPBJKH == req.RotaterData.HIIHFHPBJKH);
        if (existing != null) state.EFMLPKJCPBJ.Remove(existing);
        Save(state);
        await Send(CmdIds.RemoveRotaterScRsp, new RemoveRotaterScRsp { RotaterData = req.RotaterData.Clone(), EnergyInfo = state.EnergyInfo.Clone() });
        await Notify(state);
    }

    public async ValueTask Charge(InteractChargerCsReq req)
    {
        var state = Read();
        if (!state.EPBFBCKPFDI || req.ChargerInfo == null)
        {
            await Send(CmdIds.InteractChargerScRsp, new InteractChargerScRsp { Retcode = (uint)Retcode.RetReqParaInvalid });
            return;
        }
        if (!state.HNPDHHKNKMB.Any(x => x.GroupId == req.ChargerInfo.GroupId && x.HIIHFHPBJKH == req.ChargerInfo.HIIHFHPBJKH))
            state.HNPDHHKNKMB.Add(req.ChargerInfo.Clone());
        state.EnergyInfo.CurNum = Capacity;
        Save(state);
        await Send(CmdIds.InteractChargerScRsp, new InteractChargerScRsp { ChargerInfo = req.ChargerInfo.Clone(), EnergyInfo = state.EnergyInfo.Clone() });
        await Send(CmdIds.UpdateEnergyScNotify, new UpdateEnergyScNotify { EnergyInfo = state.EnergyInfo.Clone() });
        await Notify(state);
    }
}
