using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.EraFlipper;

// The client enters an era region (1040307's 斯缇科西亚, "切换时空以显示任务目标" before 104030706's 百夫长) and waits
// for the reply before it switches; the reply is named EnterEraFlipperRegionScRsp, which the CsReq -> ScRsp naming
// fallback cannot find, so without this handler the client never gets one.
[Opcode(CmdIds.EnterEraFlipperDataCsReq)]
public class HandlerEnterEraFlipperDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = EnterEraFlipperDataCsReq.Parser.ParseFrom(data);
        var rsp = new BasePacket((ushort)CmdIds.EnterEraFlipperRegionScRsp);
        rsp.SetData(new EnterEraFlipperRegionScRsp { LDIJAKPKLIN = req.LDIJAKPKLIN });
        await connection.SendPacket(rsp);
    }
}
