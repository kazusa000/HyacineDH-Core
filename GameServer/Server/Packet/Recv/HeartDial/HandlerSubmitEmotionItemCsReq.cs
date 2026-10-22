using HyacineCore.Server.Data;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Server.Packet.Send.HeartDial;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.HeartDial;

[Opcode(CmdIds.SubmitEmotionItemCsReq)]
public class HandlerSubmitEmotionItemCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = SubmitEmotionItemCsReq.Parser.ParseFrom(data);

        GameData.HeartDialScriptData.TryGetValue((int)req.ScriptId, out var scriptData);
        if (scriptData != null)
        {
            // Submitting the missing emotion moves the script on to the next step of its StepList
            // (e.g. Missing -> Normal), which is what HeartDialScriptListStep missions wait for.
            connection.Player!.HeartDialData!.DialList.TryGetValue((int)req.ScriptId, out var current);
            var steps = scriptData.StepList;
            var at = current != null ? steps.IndexOf(current.StepType) : 0;
            var next = steps.Count == 0 ? HeartDialStepTypeEnum.Normal : steps[Math.Min(Math.Max(at, 0) + 1, steps.Count - 1)];
            var info = connection.Player!.HeartDialData!.ChangeScriptEmotion((int)req.ScriptId,
                scriptData.DefaultEmoType, next);
            await connection.Player!.SendPacket(
                new PacketHeartDialScriptChangeScNotify(HeartDialUnlockStatus.UnlockAll, info));
            await connection.Player!.MissionManager!.HandleFinishType(MissionFinishTypeEnum.HeartDialScriptListStep);
        }

        await connection.SendPacket(new PacketSubmitEmotionItemScRsp(req.ScriptId));
    }
}