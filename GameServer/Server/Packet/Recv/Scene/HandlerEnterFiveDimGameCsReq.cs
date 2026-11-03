using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Scene;

[Opcode(CmdIds.EnterFiveDimGameCsReq)]
public class HandlerEnterFiveDimGameCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = EnterFiveDimGameCsReq.Parser.ParseFrom(data);
        var floor = connection.Player?.SceneInstance?.FloorInfo;
        var target = req.GAHICMJOCEC;
        var valid = target != null && floor != null &&
                    floor.Groups.TryGetValue((int)target.GroupId, out var group) &&
                    group.LittleGameList.Any(level => level.GameType == "FiveDim" && level.ID == target.HIIHFHPBJKH);

        // The client waits for this acknowledgment before handing control to the loaded 2D instance.
        // Progress remains on the normal group-property and mission handlers.
        var packet = new BasePacket(CmdIds.EnterFiveDimGameScRsp);
        packet.SetData(new EnterFiveDimGameScRsp { Retcode = valid ? 0u : (uint)Retcode.RetReqParaInvalid });
        await connection.SendPacket(packet);
    }
}
