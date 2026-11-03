using HyacineCore.Server.Data;
using HyacineCore.Server.Database.Scene;
using HyacineCore.Server.Enums.Scene;
using HyacineCore.Server.GameServer.Game.Scene.Entity;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Game.Player.Components;

public class SwitchHandComponent(PlayerInstance player) : BasePlayerComponent(player)
{
    public int RunningHandConfigId { get; set; } = 0;

    public List<SwitchHandInfo> GetHandInfos()
    {
        List<SwitchHandInfo> infos = [];
        foreach (var configId in GameData.MazePuzzleSwitchHandData.Keys)
        {
            var info = GetHandInfo(configId);
            if (info.Item2 == null) continue;
            infos.Add(info.Item2);
        }

        return infos;
    }

    public (Retcode, SwitchHandInfo?) GetHandInfo(int configId)
    {
        var excel = GameData.MazePuzzleSwitchHandData.GetValueOrDefault(configId);
        if (excel == null) return (Retcode.RetInteractConfigNotExist, null);
        if (Player.SceneData!.SwitchHandData.TryGetValue(configId, out var info)) return (Retcode.RetSucc, info);

        // create a new one
        info = new SwitchHandInfo
        {
            ConfigId = configId
        };
        // set default values
        var floorInfo = GameData.GetFloorInfo(excel.FloorID);
        if (floorInfo == null) return (Retcode.RetInteractConfigNotExist, null);
        if (!floorInfo.Groups.TryGetValue(excel.SwitchHandID[0], out var groupInfo))
            return (Retcode.RetReqParaInvalid, null);
        var prop = groupInfo.PropList.FirstOrDefault(x => x.ID == excel.SwitchHandID[1]);
        if (prop == null) return (Retcode.RetReqParaInvalid, null);

        info.Pos = prop.ToPositionProto();
        info.Rot = prop.ToRotationProto();

        Player.SceneData.SwitchHandData[configId] = info;
        return (Retcode.RetSucc, info);
    }

    public (Retcode, SwitchHandInfo?) UpdateHandInfo(MFJODIAILFL info)
    {
        var dbInfo = GetHandInfo((int)info.ConfigId).Item2;
        if (dbInfo == null) return (Retcode.RetInteractConfigNotExist, null);

        if (info.ALMMDIOABGJ != null)
        {
            dbInfo.Pos = info.ALMMDIOABGJ.Pos.ToPosition();
            dbInfo.Rot = info.ALMMDIOABGJ.Rot.ToPosition();
        }

        dbInfo.State = info.CMMNJCIDABE;
        dbInfo.CoinNum = (int)info.JMMIHOEDFCG;
        dbInfo.ByteValue = info.PMIEJBIDLIL.ToByteArray();

        return (Retcode.RetSucc, dbInfo);
    }

    // Catch and lay can both send Okgfofadjmd. In the tomb (G44) taking the cube sends state 0 for the source,
    // and placing it sends state 1 for the socket. The socket must open so its group graph can continue.
    public async ValueTask HandleItemOp(GOPPKKEGPPG? op)
    {
        if (op == null || op.PHOMPFCPNML == 0 || Player.SceneInstance is not { } scene) return;
        var prop = scene.Entities.Values.OfType<EntityProp>()
            .FirstOrDefault(p => p.GroupId == op.GroupId && p.InstId == op.PHOMPFCPNML);
        Logger.GetByClassName().Info(
            $"Hand {op.OpType} prop {op.GroupId}:{op.PHOMPFCPNML} ({op.NLOPDLONLAF}) state {prop?.State}");
        if (prop == null) return;
        if (op.OpType == JLGANPHGGHB.Okgfofadjmd && op.NLOPDLONLAF <= (uint)PropStateEnum.Open)
            await prop.SetState((PropStateEnum)op.NLOPDLONLAF);
        else if (op.OpType == JLGANPHGGHB.Gfhldkelcmi) await prop.SetState(PropStateEnum.Open);
    }
}
