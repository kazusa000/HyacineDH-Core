using HyacineCore.Server.GameServer.Server.Packet.Send.DrinkMaker;
using HyacineCore.Server.Kcp;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.DrinkMaker;

[Opcode(CmdIds.GetDrinkMakerDataCsReq)]
public class HandlerGetDrinkMakerDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(new PacketGetDrinkMakerDataScRsp());
    }
}
