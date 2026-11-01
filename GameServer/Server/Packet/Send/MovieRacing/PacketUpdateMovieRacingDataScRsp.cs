using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.MovieRacing;

public class PacketUpdateMovieRacingDataScRsp : BasePacket
{
    public PacketUpdateMovieRacingDataScRsp(FGCDAFADHIM best) : base(CmdIds.UpdateMovieRacingDataScRsp)
    {
        SetData(new UpdateMovieRacingDataScRsp { MBDIBNKFMPA = best });
    }
}