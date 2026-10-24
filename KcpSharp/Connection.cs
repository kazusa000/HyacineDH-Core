using System.Collections.Concurrent;
using System.Net;
using System.Text;
using HyacineCore.Server.Kcp.KcpSharp;
using HyacineCore.Server.Util;

namespace HyacineCore.Server.Kcp;

public class HyacineCoreConnection
{
    public const int HANDSHAKE_SIZE = 20;
    public static readonly ConcurrentBag<int> BannedPackets = [];
    private static readonly Logger Logger = new("GameServer");
    public static readonly ConcurrentDictionary<int, string> LogMap = [];
    public static readonly ConcurrentDictionary<string, int> NameToOpcode = new(StringComparer.Ordinal);

    public static readonly ConcurrentBag<int> IgnoreLog =
    [
        CmdIds.PlayerHeartBeatCsReq, CmdIds.PlayerHeartBeatScRsp, CmdIds.SceneEntityMoveCsReq,
        CmdIds.SceneEntityMoveScRsp, CmdIds.GetShopListCsReq, CmdIds.GetShopListScRsp
    ];

    protected readonly CancellationTokenSource CancelToken;
    protected readonly KcpConversation Conversation;
    public readonly IPEndPoint RemoteEndPoint;

    public string DebugFile = "";
    public bool IsOnline = true;
    public StreamWriter? Writer;

    public HyacineCoreConnection(KcpConversation conversation, IPEndPoint remote)
    {
        Conversation = conversation;
        RemoteEndPoint = remote;
        CancelToken = new CancellationTokenSource();
        if (ConfigManager.Config.GameServer.UsePacketEncryption) XorKey = Crypto.ClientSecretKey!.GetXorKey();

        Start();
    }

    public byte[]? XorKey { get; set; }
    public ulong ClientSecretKeySeed { get; set; }

    public long? ConversationId => Conversation.ConversationId;

    public SessionStateEnum State { get; set; } = SessionStateEnum.INACTIVE;
    //public PlayerInstance? Player { get; set; }

    public virtual void Start()
    {
        Logger.Info($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
    }

    public virtual void Stop()
    {
        //Player?.OnLogoutAsync();
        //Listener.UnregisterConnection(this);
        Conversation.Dispose();
        try
        {
            CancelToken.Cancel();
            CancelToken.Dispose();
        }
        catch
        {
        }

        IsOnline = false;
    }

    public void LogPacket(string sendOrRecv, ushort opcode, byte[] payload)
    {
        var logOption = ConfigManager.Config.ServerOption.LogOption;
        if (!logOption.EnableGamePacketLog) return;
        if (IgnoreLog.Contains(opcode)) return;

        var packetName = LogMap.GetValueOrDefault(opcode, "UnknownPacket");
        var output = $"{sendOrRecv}: {packetName}({opcode})";
        // Unknown command numbers have no message type to decode; keep the bytes to identify them later.
        if (!LogMap.ContainsKey(opcode))
            output += $" payload={Convert.ToHexString(payload, 0, Math.Min(payload.Length, 256))}";
        var showJsonText = logOption.DebugShowJsonText && !logOption.DisableLogDetailPacket;

        if (showJsonText)
        {
            try
            {
                var asJson = PacketLogHelper.ConvertPacketToJson(opcode, payload);
                output += "\r\n" + asJson;
            }
            catch
            {
                // ignore json parse failure and keep plain packet line
            }
        }

        if (logOption.LogPacketToConsole)
            Logger.Debug(output);

        if (DebugFile == "" || !logOption.SavePersonalDebugFile) return;

        var sw = GetWriter();
        sw.WriteLine(BuildDebugLogLine(output));
        sw.Flush();
    }

    private StreamWriter GetWriter()
    {
        // Create the file if it doesn't exist
        var file = new FileInfo(DebugFile);
        if (!file.Exists)
        {
            Directory.CreateDirectory(file.DirectoryName!);
            File.Create(DebugFile).Dispose();
        }

        Writer ??= new StreamWriter(DebugFile, true);
        return Writer;
    }

    private static string BuildDebugLogLine(string output)
    {
        if (Logger.ShouldShowTimeInLog())
        {
            return $"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] {output}";
        }

        return $"[GameServer] [DEBUG] {output}";
    }

    public async Task SendPacket(byte[] packet)
    {
        try
        {
            if (ConfigManager.Config.GameServer.UsePacketEncryption)
                Crypto.Xor(packet, XorKey!);

            _ = await Conversation.SendAsync(packet, CancelToken.Token);
        }
        catch
        {
            // ignore
        }
    }

    // Sees every packet sent to this client; the story runner uses it to check what a real client would receive.
    public Action<BasePacket>? PacketObserver { get; set; }

    public async Task SendPacket(BasePacket packet)
    {
        // Test
        if (packet.CmdId <= 0)
        {
            Logger.Debug("Tried to send packet with missing cmd id!");
            return;
        }

        PacketObserver?.Invoke(packet);

        // DO NOT REMOVE (unless we find a way to validate code before sending to client which I don't think we can)
        if (BannedPackets.Contains(packet.CmdId)) return;
        LogPacket("Send", packet.CmdId, packet.Data);
        // Header
        var packetBytes = packet.BuildPacket();

        try
        {
            await SendPacket(packetBytes);
        }
        catch
        {
            // ignore
        }

        if (packet.CmdId == CmdIds.SetClientPausedScRsp && ConfigManager.Config.ServerOption.ClientWatermark)
        {
            BasePacket lData;
            switch (ConfigManager.Config.ServerOption.Language)
            {
                case "CHS":
                    lData = new HandshakePacket(BuildLuaHandshakePayload(
                        "bG9jYWwgZnVuY3Rpb24gc3RyaXBfcmljaF90ZXh0KHMpCiAgICBpZiBub3QgcyB0aGVuIHJldHVybiAiIiBlbmQKICAgIHJldHVybiBzdHJpbmcuZ3N1YihzLCAiPFtePl0tPiIsICIiKQplbmQKCmxvY2FsIGZ1bmN0aW9uIGV4dHJhY3RfdWlkX2Zyb21fdGV4dChyYXcpCiAgICBsb2NhbCBzID0gc3RyaXBfcmljaF90ZXh0KHJhdykKICAgIGxvY2FsIHVpZCA9IHN0cmluZy5tYXRjaChzLCAiW1V1XVtJaV1bRGRdJXMqWzrvvJpdJXMqKCVkKykiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICByZXR1cm4gIlVJRDoiIC4uIHVpZAogICAgZW5kCiAgICByZXR1cm4gbmlsCmVuZAoKbG9jYWwgZnVuY3Rpb24gaGludF90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL0hpbnRUZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIEhpbnRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIkhpbnRUZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHJlY3QgPSBnYW1lT2JqZWN0OkdldENvbXBvbmVudCh0eXBlb2YoQ1MuVW5pdHlFbmdpbmUuUmVjdFRyYW5zZm9ybSkpCiAgICBpZiByZWN0IHRoZW4KICAgICAgICByZWN0LmFuY2hvck1pbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yTWF4ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5waXZvdCA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yZWRQb3NpdGlvbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMTUsIDApCiAgICBlbmQKCiAgICBsb2NhbCB1aWRUZXh0ID0gIlVJRDpVTktOT1dOIgogICAgbG9jYWwgdmVyc2lvbk9iaiA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgKICAgICAgICAiL1VJUm9vdC9BYm92ZURpYWxvZy9CZXRhSGludERpYWxvZyhDbG9uZSkvQ29udGVudHMvVmVyc2lvblRleHQiCiAgICApCgogICAgaWYgdmVyc2lvbk9iaiB0aGVuCiAgICAgICAgbG9jYWwgdnQgPQogICAgICAgICAgICB2ZXJzaW9uT2JqOkdldENvbXBvbmVudEluQ2hpbGRyZW4odHlwZW9mKENTLlJQRy5DbGllbnQuTG9jYWxpemVkVGV4dCkpCiAgICAgICAgaWYgdnQgYW5kIHZ0LnRleHQgdGhlbgogICAgICAgICAgICBsb2NhbCBleHRyYWN0ZWQgPSBleHRyYWN0X3VpZF9mcm9tX3RleHQodnQudGV4dCkKICAgICAgICAgICAgaWYgZXh0cmFjdGVkIHRoZW4gdWlkVGV4dCA9IGV4dHJhY3RlZCBlbmQKICAgICAgICBlbmQKICAgIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRkY2OUI0Pkh5YWNpbmVDb3JlIHwgIiAuLiB1aWRUZXh0IC4uICI8L2NvbG9yPjwvc2l6ZT4iCiAgICB0ZXh0Q29tcG9uZW50Lmhvcml6b250YWxPdmVyZmxvdyA9IDEKICAgIHRleHRDb21wb25lbnQudmVydGljYWxPdmVyZmxvdyA9IDEKICAgIGdhbWVPYmplY3Q6U2V0QWN0aXZlKHRydWUpCmVuZAoKbG9jYWwgZnVuY3Rpb24gdmVyc2lvbl90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL1ZlcnNpb25UZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIFZlcnNpb25UZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIlZlcnNpb25UZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRDc4QkZGPuWmguaenOaer+iQjuS4jeWPr+mBv+WFje+8jOiHs+WwkeiuqeaIkeS7rOeahOWRiuWIq+KApuabtOe+juS4gOS6m+OAgi3pgZDonbY8L2NvbG9yPjwvc2l6ZT4iCgogICAgbG9jYWwgcmVjdCA9IGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50KHR5cGVvZihDUy5Vbml0eUVuZ2luZS5SZWN0VHJhbnNmb3JtKSkKICAgIGlmIHJlY3QgdGhlbgogICAgICAgIHJlY3QuYW5jaG9yTWluID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JNYXggPSBDUy5Vbml0eUVuZ2luZS5WZWN0b3IyKDAsIDAuNSkKICAgICAgICByZWN0LnBpdm90ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JlZFBvc2l0aW9uID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigxNSwgLTE4KQogICAgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gbWh5X3RleHQoKQogICAgbG9jYWwgZ2FtZU9iamVjdCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiSURNQVAxIikKICAgIGlmIG5vdCBnYW1lT2JqZWN0IHRoZW4gcmV0dXJuIGVuZAogICAgbG9jYWwgdXRpbCA9CiAgICAgICAgZ2FtZU9iamVjdDpHZXRDb21wb25lbnRJbkNoaWxkcmVuKAogICAgICAgICAgICB0eXBlb2YoQ1MuUlBHLkNsaWVudC5NZXNzYWdlQm94RGlhbG9nVXRpbCkKICAgICAgICApCiAgICBpZiB1dGlsIHRoZW4gdXRpbC5TaG93QWJvdmVEaWFsb2dUZXh0ID0gZmFsc2UgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gb25fZXJyb3IoZXJyKQogICAgbG9jYWwgbXNnID0gIui/nOeoi+iEmuacrOmUmeivr++8miIgLi4gdG9zdHJpbmcoZXJyKQogICAgQ1MuUlBHLkNsaWVudC5Db25maXJtRGlhbG9nVXRpbC5TaG93Q3VzdG9tT2tDYW5jZWxIaW50KG1zZykKICAgIGxvY2FsIGYgPSBpby5vcGVuKCIuL2Vycm9yLnR4dCIsICJ3IikKICAgIGlmIGYgdGhlbiBmOndyaXRlKG1zZykgZjpjbG9zZSgpIGVuZAplbmQKCnhwY2FsbCh2ZXJzaW9uX3RleHQsIG9uX2Vycm9yKQp4cGNhbGwoaGludF90ZXh0LCBvbl9lcnJvcikKeHBjYWxsKG1oeV90ZXh0LCBvbl9lcnJvcikK"));
                    break;
                case "CHT":
                    lData = new HandshakePacket(BuildLuaHandshakePayload(
                        "bG9jYWwgZnVuY3Rpb24gc3RyaXBfcmljaF90ZXh0KHMpCiAgICBpZiBub3QgcyB0aGVuIHJldHVybiAiIiBlbmQKICAgIHJldHVybiBzdHJpbmcuZ3N1YihzLCAiPFtePl0tPiIsICIiKQplbmQKCmxvY2FsIGZ1bmN0aW9uIGV4dHJhY3RfdWlkX2Zyb21fdGV4dChyYXcpCiAgICBsb2NhbCBzID0gc3RyaXBfcmljaF90ZXh0KHJhdykKICAgIGxvY2FsIHVpZCA9IHN0cmluZy5tYXRjaChzLCAiW1V1XVtJaV1bRGRdJXMqWzrvvJpdJXMqKCVkKykiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICByZXR1cm4gIlVJRDoiIC4uIHVpZAogICAgZW5kCiAgICByZXR1cm4gbmlsCmVuZAoKbG9jYWwgZnVuY3Rpb24gaGludF90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL0hpbnRUZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIEhpbnRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIkhpbnRUZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHJlY3QgPSBnYW1lT2JqZWN0OkdldENvbXBvbmVudCh0eXBlb2YoQ1MuVW5pdHlFbmdpbmUuUmVjdFRyYW5zZm9ybSkpCiAgICBpZiByZWN0IHRoZW4KICAgICAgICByZWN0LmFuY2hvck1pbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yTWF4ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5waXZvdCA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yZWRQb3NpdGlvbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMTUsIDApCiAgICBlbmQKCiAgICBsb2NhbCB1aWRUZXh0ID0gIlVJRDpVTktOT1dOIgogICAgbG9jYWwgdmVyc2lvbk9iaiA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgKICAgICAgICAiL1VJUm9vdC9BYm92ZURpYWxvZy9CZXRhSGludERpYWxvZyhDbG9uZSkvQ29udGVudHMvVmVyc2lvblRleHQiCiAgICApCgogICAgaWYgdmVyc2lvbk9iaiB0aGVuCiAgICAgICAgbG9jYWwgdnQgPQogICAgICAgICAgICB2ZXJzaW9uT2JqOkdldENvbXBvbmVudEluQ2hpbGRyZW4odHlwZW9mKENTLlJQRy5DbGllbnQuTG9jYWxpemVkVGV4dCkpCiAgICAgICAgaWYgdnQgYW5kIHZ0LnRleHQgdGhlbgogICAgICAgICAgICBsb2NhbCBleHRyYWN0ZWQgPSBleHRyYWN0X3VpZF9mcm9tX3RleHQodnQudGV4dCkKICAgICAgICAgICAgaWYgZXh0cmFjdGVkIHRoZW4gdWlkVGV4dCA9IGV4dHJhY3RlZCBlbmQKICAgICAgICBlbmQKICAgIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRkY2OUI0Pkh5YWNpbmVDb3JlIHwgIiAuLiB1aWRUZXh0IC4uICI8L2NvbG9yPjwvc2l6ZT4iCiAgICB0ZXh0Q29tcG9uZW50Lmhvcml6b250YWxPdmVyZmxvdyA9IDEKICAgIHRleHRDb21wb25lbnQudmVydGljYWxPdmVyZmxvdyA9IDEKICAgIGdhbWVPYmplY3Q6U2V0QWN0aXZlKHRydWUpCmVuZAoKbG9jYWwgZnVuY3Rpb24gdmVyc2lvbl90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL1ZlcnNpb25UZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIFZlcnNpb25UZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIlZlcnNpb25UZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRDc4QkZGPuWmguaenOaer+iQjuS4jeWPr+mBv+WFje+8jOiHs+WwkeiuqeaIkeS7rOeahOWRiuWIq+KApuabtOe+juS4gOS6m+OAgi3pgZDonbY8L2NvbG9yPjwvc2l6ZT4iCgogICAgbG9jYWwgcmVjdCA9IGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50KHR5cGVvZihDUy5Vbml0eUVuZ2luZS5SZWN0VHJhbnNmb3JtKSkKICAgIGlmIHJlY3QgdGhlbgogICAgICAgIHJlY3QuYW5jaG9yTWluID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JNYXggPSBDUy5Vbml0eUVuZ2luZS5WZWN0b3IyKDAsIDAuNSkKICAgICAgICByZWN0LnBpdm90ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JlZFBvc2l0aW9uID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigxNSwgLTE4KQogICAgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gbWh5X3RleHQoKQogICAgbG9jYWwgZ2FtZU9iamVjdCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiSURNQVAxIikKICAgIGlmIG5vdCBnYW1lT2JqZWN0IHRoZW4gcmV0dXJuIGVuZAogICAgbG9jYWwgdXRpbCA9CiAgICAgICAgZ2FtZU9iamVjdDpHZXRDb21wb25lbnRJbkNoaWxkcmVuKAogICAgICAgICAgICB0eXBlb2YoQ1MuUlBHLkNsaWVudC5NZXNzYWdlQm94RGlhbG9nVXRpbCkKICAgICAgICApCiAgICBpZiB1dGlsIHRoZW4gdXRpbC5TaG93QWJvdmVEaWFsb2dUZXh0ID0gZmFsc2UgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gb25fZXJyb3IoZXJyKQogICAgbG9jYWwgbXNnID0gIui/nOeoi+iEmuacrOmUmeivr++8miIgLi4gdG9zdHJpbmcoZXJyKQogICAgQ1MuUlBHLkNsaWVudC5Db25maXJtRGlhbG9nVXRpbC5TaG93Q3VzdG9tT2tDYW5jZWxIaW50KG1zZykKICAgIGxvY2FsIGYgPSBpby5vcGVuKCIuL2Vycm9yLnR4dCIsICJ3IikKICAgIGlmIGYgdGhlbiBmOndyaXRlKG1zZykgZjpjbG9zZSgpIGVuZAplbmQKCnhwY2FsbCh2ZXJzaW9uX3RleHQsIG9uX2Vycm9yKQp4cGNhbGwoaGludF90ZXh0LCBvbl9lcnJvcikKeHBjYWxsKG1oeV90ZXh0LCBvbl9lcnJvcikK"));
                    break;
                default:
                    lData = new HandshakePacket(BuildLuaHandshakePayload(
                        "bG9jYWwgZnVuY3Rpb24gc3RyaXBfcmljaF90ZXh0KHMpCiAgICBpZiBub3QgcyB0aGVuIHJldHVybiAiIiBlbmQKICAgIHJldHVybiBzdHJpbmcuZ3N1YihzLCAiPFtePl0tPiIsICIiKQplbmQKCmxvY2FsIGZ1bmN0aW9uIGV4dHJhY3RfdWlkX2Zyb21fdGV4dChyYXcpCiAgICBsb2NhbCBzID0gc3RyaXBfcmljaF90ZXh0KHJhdykKICAgIGxvY2FsIHVpZCA9IHN0cmluZy5tYXRjaChzLCAiW1V1XVtJaV1bRGRdJXMqWzrvvJpdJXMqKCVkKykiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICByZXR1cm4gIlVJRDoiIC4uIHVpZAogICAgZW5kCiAgICByZXR1cm4gbmlsCmVuZAoKbG9jYWwgZnVuY3Rpb24gaGludF90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL0hpbnRUZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIEhpbnRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIkhpbnRUZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHJlY3QgPSBnYW1lT2JqZWN0OkdldENvbXBvbmVudCh0eXBlb2YoQ1MuVW5pdHlFbmdpbmUuUmVjdFRyYW5zZm9ybSkpCiAgICBpZiByZWN0IHRoZW4KICAgICAgICByZWN0LmFuY2hvck1pbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yTWF4ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5waXZvdCA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMCwgMC41KQogICAgICAgIHJlY3QuYW5jaG9yZWRQb3NpdGlvbiA9IENTLlVuaXR5RW5naW5lLlZlY3RvcjIoMTUsIDApCiAgICBlbmQKCiAgICBsb2NhbCB1aWRUZXh0ID0gIlVJRDpVTktOT1dOIgogICAgbG9jYWwgdmVyc2lvbk9iaiA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgKICAgICAgICAiL1VJUm9vdC9BYm92ZURpYWxvZy9CZXRhSGludERpYWxvZyhDbG9uZSkvQ29udGVudHMvVmVyc2lvblRleHQiCiAgICApCgogICAgaWYgdmVyc2lvbk9iaiB0aGVuCiAgICAgICAgbG9jYWwgdnQgPQogICAgICAgICAgICB2ZXJzaW9uT2JqOkdldENvbXBvbmVudEluQ2hpbGRyZW4odHlwZW9mKENTLlJQRy5DbGllbnQuTG9jYWxpemVkVGV4dCkpCiAgICAgICAgaWYgdnQgYW5kIHZ0LnRleHQgdGhlbgogICAgICAgICAgICBsb2NhbCBleHRyYWN0ZWQgPSBleHRyYWN0X3VpZF9mcm9tX3RleHQodnQudGV4dCkKICAgICAgICAgICAgaWYgZXh0cmFjdGVkIHRoZW4gdWlkVGV4dCA9IGV4dHJhY3RlZCBlbmQKICAgICAgICBlbmQKICAgIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRkY2OUI0Pkh5YWNpbmVDb3JlIHwgIiAuLiB1aWRUZXh0IC4uICI8L2NvbG9yPjwvc2l6ZT4iCiAgICB0ZXh0Q29tcG9uZW50Lmhvcml6b250YWxPdmVyZmxvdyA9IDEKICAgIHRleHRDb21wb25lbnQudmVydGljYWxPdmVyZmxvdyA9IDEKICAgIGdhbWVPYmplY3Q6U2V0QWN0aXZlKHRydWUpCmVuZAoKbG9jYWwgZnVuY3Rpb24gdmVyc2lvbl90ZXh0KCkKICAgIGxvY2FsIGdhbWVPYmplY3QgPSBDUy5Vbml0eUVuZ2luZS5HYW1lT2JqZWN0LkZpbmQoCiAgICAgICAgIi9VSVJvb3QvQWJvdmVEaWFsb2cvQmV0YUhpbnREaWFsb2coQ2xvbmUpL0NvbnRlbnRzL1ZlcnNpb25UZXh0IgogICAgKQogICAgaWYgbm90IGdhbWVPYmplY3QgdGhlbiBlcnJvcigi5om+5LiN5YiwIFZlcnNpb25UZXh0IikgcmV0dXJuIGVuZAoKICAgIGxvY2FsIHRleHRDb21wb25lbnQgPQogICAgICAgIGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50SW5DaGlsZHJlbih0eXBlb2YoQ1MuUlBHLkNsaWVudC5Mb2NhbGl6ZWRUZXh0KSkKICAgIGlmIG5vdCB0ZXh0Q29tcG9uZW50IHRoZW4gZXJyb3IoIlZlcnNpb25UZXh0IOaXoCBMb2NhbGl6ZWRUZXh0IikgcmV0dXJuIGVuZAoKICAgIHRleHRDb21wb25lbnQudGV4dCA9CiAgICAgICAgIjxzaXplPTE4Pjxjb2xvcj0jRDc4QkZGPuWmguaenOaer+iQjuS4jeWPr+mBv+WFje+8jOiHs+WwkeiuqeaIkeS7rOeahOWRiuWIq+KApuabtOe+juS4gOS6m+OAgi3pgZDonbY8L2NvbG9yPjwvc2l6ZT4iCgogICAgbG9jYWwgcmVjdCA9IGdhbWVPYmplY3Q6R2V0Q29tcG9uZW50KHR5cGVvZihDUy5Vbml0eUVuZ2luZS5SZWN0VHJhbnNmb3JtKSkKICAgIGlmIHJlY3QgdGhlbgogICAgICAgIHJlY3QuYW5jaG9yTWluID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JNYXggPSBDUy5Vbml0eUVuZ2luZS5WZWN0b3IyKDAsIDAuNSkKICAgICAgICByZWN0LnBpdm90ID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigwLCAwLjUpCiAgICAgICAgcmVjdC5hbmNob3JlZFBvc2l0aW9uID0gQ1MuVW5pdHlFbmdpbmUuVmVjdG9yMigxNSwgLTE4KQogICAgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gbWh5X3RleHQoKQogICAgbG9jYWwgZ2FtZU9iamVjdCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiSURNQVAxIikKICAgIGlmIG5vdCBnYW1lT2JqZWN0IHRoZW4gcmV0dXJuIGVuZAogICAgbG9jYWwgdXRpbCA9CiAgICAgICAgZ2FtZU9iamVjdDpHZXRDb21wb25lbnRJbkNoaWxkcmVuKAogICAgICAgICAgICB0eXBlb2YoQ1MuUlBHLkNsaWVudC5NZXNzYWdlQm94RGlhbG9nVXRpbCkKICAgICAgICApCiAgICBpZiB1dGlsIHRoZW4gdXRpbC5TaG93QWJvdmVEaWFsb2dUZXh0ID0gZmFsc2UgZW5kCmVuZAoKbG9jYWwgZnVuY3Rpb24gb25fZXJyb3IoZXJyKQogICAgbG9jYWwgbXNnID0gIui/nOeoi+iEmuacrOmUmeivr++8miIgLi4gdG9zdHJpbmcoZXJyKQogICAgQ1MuUlBHLkNsaWVudC5Db25maXJtRGlhbG9nVXRpbC5TaG93Q3VzdG9tT2tDYW5jZWxIaW50KG1zZykKICAgIGxvY2FsIGYgPSBpby5vcGVuKCIuL2Vycm9yLnR4dCIsICJ3IikKICAgIGlmIGYgdGhlbiBmOndyaXRlKG1zZykgZjpjbG9zZSgpIGVuZAplbmQKCnhwY2FsbCh2ZXJzaW9uX3RleHQsIG9uX2Vycm9yKQp4cGNhbGwoaGludF90ZXh0LCBvbl9lcnJvcikKeHBjYWxsKG1oeV90ZXh0LCBvbl9lcnJvcikK"));
                    break;
            }

            await SendPacket(lData.BuildPacket());
        }
    }

    protected virtual string? GetLuaUidText()
    {
        return null;
    }

    private static string EscapeLuaString(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private byte[] BuildLuaHandshakePayload(string base64Script)
    {
        var scriptBytes = Convert.FromBase64String(base64Script);
        var uidText = GetLuaUidText();

        if (string.IsNullOrWhiteSpace(uidText))
        {
            return scriptBytes;
        }

        var script = Encoding.UTF8.GetString(scriptBytes);
        var escapedUid = EscapeLuaString(uidText);

        script = script.Replace(
            "local uidText = \"UID:UNKNOWN\"",
            $"local uidText = \"{escapedUid}\"",
            StringComparison.Ordinal);

        const string marker = "textComponent.text =";
        var markerIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            script = script.Insert(markerIndex, $"uidText = \"{escapedUid}\"\n    ");
        }

        return Encoding.UTF8.GetBytes(script);
    }

    public async Task SendPacket(int cmdId)
    {
        await SendPacket(new BasePacket((ushort)cmdId));
    }

    public static bool TryResolveResponseForRequest(ushort requestOpcode, out int responseOpcode, out string responseName)
    {
        responseOpcode = 0;
        responseName = "";

        var reqName = LogMap.GetValueOrDefault(requestOpcode);
        if (string.IsNullOrWhiteSpace(reqName)) return false;

        // Common naming: XxxCsReq -> XxxScRsp
        var standard = reqName.Replace("Cs", "Sc").Replace("Req", "Rsp");
        if (!string.Equals(standard, reqName, StringComparison.Ordinal) &&
            NameToOpcode.TryGetValue(standard, out responseOpcode))
        {
            responseName = standard;
            return true;
        }

        // Some modules use GetXxxScRsp for responses (e.g. SwitchHandUpdateCsReq -> GetSwitchHandUpdateScRsp).
        if (reqName.EndsWith("CsReq", StringComparison.Ordinal))
        {
            var baseName = reqName[..^"CsReq".Length];
            var getPrefix = "Get" + baseName + "ScRsp";
            if (NameToOpcode.TryGetValue(getPrefix, out responseOpcode))
            {
                responseName = getPrefix;
                return true;
            }
        }

        return false;
    }
}
