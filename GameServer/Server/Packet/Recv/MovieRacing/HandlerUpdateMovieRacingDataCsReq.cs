using HyacineCore.Server.Data;
using HyacineCore.Server.Data.Excel;
using HyacineCore.Server.Database.Activity;
using HyacineCore.Server.Enums.Mission;
using HyacineCore.Server.Enums.Quest;
using HyacineCore.Server.GameServer.Server.Packet.Send.MovieRacing;
using HyacineCore.Server.Kcp;
using HyacineCore.Server.Proto;

namespace HyacineCore.Server.GameServer.Server.Packet.Recv.MovieRacing;

// The client ends a movie racing level with its score. The best result is kept, the level's quest
// (FinishWay FinishMovieGame: score against ParamInt1 on level ParamInt2) is judged, and a level cleared to its
// target sets the floor's Game{level}Finish: 1031701's cinema only fires Temp_MovieGameFinish for 103170124 once
// Game1Finish is 1, and no level or mission data sets it.
[Opcode(CmdIds.UpdateMovieRacingDataCsReq)]
public class HandlerUpdateMovieRacingDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = UpdateMovieRacingDataCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;
        var record = req.MBDIBNKFMPA ?? new FGCDAFADHIM();
        var type = (int)record.Type;
        var level = (int)record.Level;

        var racing = player.ActivityManager!.Data.MovieRacingData ??= [];
        if (!racing.TryGetValue(type, out var levels)) racing[type] = levels = [];
        if (!levels.TryGetValue(level, out var best)) levels[level] = best = new MovieRacingResult();
        best.Score = Math.Max(best.Score, record.HPKMEMFPLOH);
        best.Extra = Math.Max(best.Extra, record.PFLMNMAGFKP);

        await player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.FinishMovieGame, record);

        if (player.SceneInstance is { } scene &&
            GameData.FinishWayData.Values.Any(way => way.FinishType == MissionFinishTypeEnum.FinishMovieGame &&
                                                     IsCleared(way, record)))
            await scene.UpdateFloorSavedValue($"Game{level}Finish", 1);

        await connection.SendPacket(new PacketUpdateMovieRacingDataScRsp(best.ToProto(type, level)));
    }

    public static bool IsCleared(FinishWayExcel way, FGCDAFADHIM record)
    {
        if (way.ParamInt2 != record.Level) return false;
        var score = (int)record.HPKMEMFPLOH;
        return way.ParamType switch
        {
            QuestParaTypeEnum.Equal => score == way.ParamInt1,
            QuestParaTypeEnum.Greater => score > way.ParamInt1,
            _ => score >= way.ParamInt1
        };
    }
}