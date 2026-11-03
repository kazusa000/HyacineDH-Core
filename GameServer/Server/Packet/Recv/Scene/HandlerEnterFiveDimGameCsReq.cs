using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Server.Packet.Send.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Scene;

[Opcode(CmdIds.EnterFiveDimGameCsReq)]
public class HandlerEnterFiveDimGameCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = EnterFiveDimGameCsReq.Parser.ParseFrom(data);
        var player = connection.Player;
        var scene = player?.SceneInstance;
        var floor = scene?.FloorInfo;
        var target = req.GAHICMJOCEC;
        var valid = target != null && floor != null &&
                    floor.Groups.TryGetValue((int)target.GroupId, out var group) &&
                    group.LittleGameList.Any(level => level.GameType == "FiveDim" && level.ID == target.HIIHFHPBJKH);

        // The client waits for this acknowledgment before handing control to the loaded 2D instance.
        var packet = new BasePacket(CmdIds.EnterFiveDimGameScRsp);
        packet.SetData(new EnterFiveDimGameScRsp { Retcode = valid ? 0u : (uint)Retcode.RetReqParaInvalid });
        await connection.SendPacket(packet);

        // Arm the pilot scene for this visit. Its FiveDim exit still has to trigger ReadyToPlayC.
        // Persisting phase 2 would start the pilot scene on reload, before the player reaches the cockpit.
        const string pilotPhase = "GP_5DEnterCity_Phase";
        if (valid && target is { GroupId: 26, HIIHFHPBJKH: 110001 } && scene?.FloorId == 20513001 &&
            scene == player!.SceneInstance && player.MissionManager!.GetSubMissionStatus(105410012) == MissionPhaseEnum.Accept)
        {
            var phase = scene.GetGroupProperty(26, pilotPhase);
            if (phase < 2)
                await connection.SendPacket(new PacketSceneGroupRefreshScNotify(scene,
                    [new GroupPropertyRefreshData(26, pilotPhase, phase, 2)]));
        }
    }
}
