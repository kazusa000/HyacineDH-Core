using HyacineCore.Server.GameServer.Game.Scene;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.EraFlipper;

[Opcode(CmdIds.ChangeEraFlipperDataCsReq)]
public class HandlerChangeEraFlipperDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await new EraFlipperManager(connection.Player!).Change(ChangeEraFlipperDataCsReq.Parser.ParseFrom(data));
    }
}
