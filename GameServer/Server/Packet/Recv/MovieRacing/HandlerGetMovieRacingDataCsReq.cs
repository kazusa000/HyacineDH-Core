using HyacineCore.Server.GameServer.Server.Packet.Send.MovieRacing;
using HyacineCore.Server.Kcp;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MovieRacing;

// The cinema's movie racing minigame asks for the best result of each level it has been played on.
[Opcode(CmdIds.GetMovieRacingDataCsReq)]
public class HandlerGetMovieRacingDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var activity = connection.Player!.ActivityManager!.Data;
        await connection.SendPacket(new PacketGetMovieRacingDataScRsp(activity.MovieRacingData ??= []));
    }
}