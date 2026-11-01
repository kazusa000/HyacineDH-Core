using HyacineCore.Server.Database.Activity;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.MovieRacing;

public class PacketGetMovieRacingDataScRsp : BasePacket
{
    public PacketGetMovieRacingDataScRsp(Dictionary<int, Dictionary<int, MovieRacingResult>> data) : base(
        CmdIds.GetMovieRacingDataScRsp)
    {
        var proto = new GetMovieRacingDataScRsp();
        foreach (var (type, levels) in data)
        foreach (var (level, result) in levels)
            proto.GONKIBFLNEN.Add(result.ToProto(type, level));

        SetData(proto);
    }
}