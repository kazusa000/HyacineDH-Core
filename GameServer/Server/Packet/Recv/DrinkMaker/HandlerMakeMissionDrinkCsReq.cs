using HyacineCore.Server.GameServer.Server.Packet.Send.DrinkMaker;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.DrinkMaker;

[Opcode(CmdIds.MakeMissionDrinkCsReq)]
public class HandlerMakeMissionDrinkCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = MakeMissionDrinkCsReq.Parser.ParseFrom(data);

        await connection.SendPacket(new PacketMakeMissionDrinkScRsp(req));
    }
}
