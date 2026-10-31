using HyacineCore.Server.Data;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.GameServer.Game.Player;
using HyacineCore.Server.GameServer.Server.Packet.Send.Tutorial;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.Tutorial;

[Opcode(CmdIds.GetTutorialCsReq)]
public class HandlerGetTutorialCsReq : Handler
{
    private static readonly Logger Log = new("Tutorial");

    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await SendPlayerData(connection);
        FinishPassedMissionTutorials(connection.Player!);
        // Always answer: the 4.1 client keeps the scene unready while this request is pending.
        await connection.SendPacket(new PacketGetTutorialScRsp(connection.Player!));
    }

    // A tutorial ends when the client sees its finish event, such as a sub-mission being taken. Story progress made
    // without the client (offline story runner, GM) passes those events unseen, and the client then restarts the
    // tutorial on every maze page: tutorial 5001 (ends on taking 101050104) hid the interaction buttons for the
    // rest of the story. Tutorials whose mission finish condition is already met are finished before the client
    // reads them.
    private static void FinishPassedMissionTutorials(PlayerInstance player)
    {
        var tutorials = player.TutorialData!.Tutorials;
        var missions = player.MissionManager!;
        foreach (var (id, status) in tutorials.ToList())
        {
            if (status != TutorialStatus.TutorialUnlock) continue;
            if (!GameData.TutorialDataData.TryGetValue(id, out var excel)) continue;
            var passed = excel.FinishTriggerParams.FirstOrDefault(p => int.TryParse(p.TriggerParam, out var mission) &&
                p.TriggerType switch
                {
                    "TakeSubMission" => missions.GetSubMissionStatus(mission) is MissionPhaseEnum.Accept
                        or MissionPhaseEnum.Finish,
                    "FinishSubMission" => missions.GetSubMissionStatus(mission) == MissionPhaseEnum.Finish,
                    "FinishMainMission" => missions.GetMainMissionStatus(mission) == MissionPhaseEnum.Finish,
                    _ => false
                });
            if (passed == null) continue;
            tutorials[id] = TutorialStatus.TutorialFinish;
            Log.Info($"tutorial {id} finished: {passed.TriggerType} {passed.TriggerParam} already passed");
        }
    }

    private async Task SendPlayerData(Connection connection)
    {
        var filePath = Path.Combine(Environment.CurrentDirectory, "Lua", "welcome.lua");
        if (File.Exists(filePath))
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            await connection.SendPacket(new HandshakePacket(fileBytes));
        }
    }
}