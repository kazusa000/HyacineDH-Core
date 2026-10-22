using HyacineCore.Server.GameServer.Server.Packet.Send.MatchThree;
using HyacineCore.Server.Kcp;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MatchThree;

// No match-three progress is kept yet; an empty answer lets the client open the minigame.
[Opcode(CmdIds.MatchThreeGetDataCsReq)]
public class HandlerMatchThreeGetDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(new PacketMatchThreeGetDataScRsp());
    }
}
