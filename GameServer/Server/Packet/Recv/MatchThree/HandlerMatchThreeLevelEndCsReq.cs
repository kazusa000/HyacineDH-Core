using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Server.Packet.Send.MatchThree;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MatchThree;

// The client ends a match-three level; story missions wait for the level ID (MatchThreeFinishLevel).
[Opcode(CmdIds.MatchThreeLevelEndCsReq)]
public class HandlerMatchThreeLevelEndCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = MatchThreeLevelEndCsReq.Parser.ParseFrom(data);
        await connection.Player!.MissionManager!.HandleFinishType(MissionFinishTypeEnum.MatchThreeFinishLevel, req);
        await connection.SendPacket(new PacketMatchThreeLevelEndScRsp(req));
    }
}
