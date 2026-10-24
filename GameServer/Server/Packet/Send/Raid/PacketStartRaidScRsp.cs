using HyacineCore.Server.Database.Scene;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Raid;

public class PacketStartRaidScRsp : BasePacket
{
    public PacketStartRaidScRsp(RaidRecord record, PlayerInstance player) : base(CmdIds.StartRaidScRsp)
    {
        // The client loads the raid floor from this scene (RaidPlayerData); without it the screen stays black.
        var proto = new StartRaidScRsp
        {
            Retcode = (uint)Retcode.RetSucc,
            Scene = new EJFCPMHJLDO
            {
                RaidId = (uint)record.RaidId,
                WorldLevel = (uint)record.WorldLevel,
                Lineup = player.LineupManager!.GetCurLineup()!.ToProto(),
                ODMPLKFPHGJ = player.SceneInstance!.ToProto()
            }
        };

        SetData(proto);
    }

    public PacketStartRaidScRsp(Retcode ret) : base(CmdIds.StartRaidScRsp)
    {
        var proto = new StartRaidScRsp
        {
            Retcode = (uint)ret
        };

        SetData(proto);
    }
}
