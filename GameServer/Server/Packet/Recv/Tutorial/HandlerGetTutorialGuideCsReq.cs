using HyacineCore.Server.GameServer.Server.Packet.Send.Tutorial;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Tutorial;

[Opcode(CmdIds.GetTutorialGuideCsReq)]
public class HandlerGetTutorialGuideCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // Always answer, as for GetTutorialCsReq.
        await connection.SendPacket(new PacketGetTutorialGuideScRsp(connection.Player!));
    }
}