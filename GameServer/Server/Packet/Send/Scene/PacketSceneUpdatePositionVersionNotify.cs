using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Send.Scene;

public class PacketSceneUpdatePositionVersionNotify : BasePacket
{
    public PacketSceneUpdatePositionVersionNotify(uint posVersion) : base(CmdIds.SceneUpdatePositionVersionNotify)
    {
        var proto = new SceneUpdatePositionVersionNotify
        {
            PosVersion = posVersion
        };

        SetData(proto);
    }
}
