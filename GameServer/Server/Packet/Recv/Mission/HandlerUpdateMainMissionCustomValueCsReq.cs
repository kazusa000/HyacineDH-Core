using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Mission;

[Opcode(CmdIds.UpdateMainMissionCustomValueCsReq)]
public class HandlerUpdateMainMissionCustomValueCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = UpdateMainMissionCustomValueCsReq.Parser.ParseFrom(data);
        var manager = connection.Player!.MissionManager!;
        var status = await manager.UpdateCustomValue((int)req.MainMissionId, req.CBBMFAIEGDP, req.Value);
        var response = new UpdateMainMissionCustomValueScRsp { Retcode = (uint)status };
        if (status == Retcode.RetSucc) response.ACGBHPOFHCO = manager.MainMissionToProto((int)req.MainMissionId);
        var packet = new BasePacket(CmdIds.UpdateMainMissionCustomValueScRsp);
        packet.SetData(response);
        await connection.SendPacket(packet);
    }
}
