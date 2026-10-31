using Google.Protobuf;
using HyacineCore.Server.Configuration;
using HyacineCore.Server.Database.Account;
using HyacineCore.Server.Proto;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer.Handler;
using HyacineCore.Server.WebServer.Objects;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace HyacineCore.Server.WebServer.Controllers;

[ApiController]
[EnableCors("AllowAll")]
[Route("/")]
public class DispatchRoutes : ControllerBase
{
    private static ConfigContainer Config => ConfigManager.Config;
    public static Logger Logger = new("DispatchServer");

    [HttpGet("query_dispatch")]
    public IActionResult QueryDispatch()
    {
        if (!Config.ServerOption.ServerConfig.RunDispatch)
            return Content(string.Empty, "text/plain");

        try
        {
            var req = HttpContext.Request;
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var remotePort = HttpContext.Connection.RemotePort;
            var ua = req.Headers.UserAgent.ToString();
            var isNewFormat = string.Equals(req.Query["is_new_format"].ToString(), "1", StringComparison.Ordinal);

            Logger.Debug($"query_dispatch begin: {req.Method} {req.Scheme}://{req.Host}{req.PathBase}{req.Path}{req.QueryString} from {remoteIp}:{remotePort} ua=\"{ua}\"");
            Logger.Debug($"query_dispatch format: is_new_format={isNewFormat}");

            foreach (var (key, value) in req.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug($"query_dispatch header: {key}=<redacted>");
                    continue;
                }

                Logger.Debug($"query_dispatch header: {key}={value.ToString()}");
            }

            foreach (var (key, value) in req.Query.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Logger.Debug($"query_dispatch query: {key}={value.ToString()}");
            }

            Logger.Debug($"query_dispatch config: RunDispatch={Config.ServerOption.ServerConfig.RunDispatch}, RunGateway={Config.ServerOption.ServerConfig.RunGateway}, Regions={Config.ServerOption.ServerConfig.Regions.Count}");
        }
        catch (Exception e)
        {
            Logger.Debug("query_dispatch debug prelude failed", e);
        }

        // Keep the payload minimal (match LC/zig behavior): only `region_list`.
        // Some clients appear to be strict about the proto schema and may fail if extra fields are present.
        var data = new Dispatch();

        if (Config.ServerOption.ServerConfig.RunGateway)
        {
            data.RegionList.Add(new RegionInfo
            {
                Name = Config.GameServer.GameServerId,
                Title = Config.GameServer.GameServerName,
                DispatchUrl = $"{Config.HttpServer.GetDisplayAddress()}/query_gateway",
                EnvType = Config.GameServer.EnvType,
                DisplayName = Config.GameServer.GameServerName
            });
        }

        foreach (var region in Config.ServerOption.ServerConfig.Regions)
        {
            data.RegionList.Add(new RegionInfo
            {
                Name = region.GameServerId,
                Title = region.GameServerName,
                DisplayName = region.GameServerName,
                EnvType = region.EnvType.ToString(),
                DispatchUrl = region.GateWayAddress
            });
        }

        Logger.Info("Client request: query_dispatch");

        var bytes = data.ToByteArray();
        var b64 = Convert.ToBase64String(bytes);

        Logger.Debug($"query_dispatch result: regionCount={data.RegionList.Count}, protoBytes={bytes.Length}, base64Length={b64.Length}");
        foreach (var r in data.RegionList)
        {
            Logger.Debug($"query_dispatch region: name={r.Name} env={r.EnvType} display={r.DisplayName} url={r.DispatchUrl}");
        }

        return Content(b64, "text/plain");
    }

    [HttpPost("/account/risky/api/check")]
    public ContentResult RiskyCheck()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"id\":\"none\",\"action\":\"ACTION_NONE\",\"geetest\":null}}",
            ContentType = "application/json"
        };
    }

    // === AUTHENTICATION ===
    [HttpPost("/hkrpg_global/mdk/shield/api/login")]
    [HttpPost("/{gameKey}/mdk/shield/api/login")]
    public JsonResult Login([FromBody] LoginReqJson req)
    {
        Logger.Debug($"shield login: account={req.account} is_crypto={req.is_crypto}");
        return new UsernameLoginHandler().Handle(req.account!, req.password!, req.is_crypto);
    }

    [HttpPost("/hkrpg_global/account/ma-passport/api/appLoginByPassword")]
    [HttpPost("/{gameKey}/account/ma-passport/api/appLoginByPassword")]
    public JsonResult Login([FromBody] NewLoginReqJson req)
    {
        Logger.Debug($"appLoginByPassword: account={req.account}");
        return new NewUsernameLoginHandler().Handle(req.account!, req.password!);
    }

    // The SDK's automatic login with the sToken it saved from appLoginByPassword.
    [HttpPost("/hkrpg_global/account/ma-passport/token/verifySToken")]
    [HttpPost("/{gameKey}/account/ma-passport/token/verifySToken")]
    public JsonResult VerifySToken([FromBody] System.Text.Json.JsonElement body)
    {
        // Read leniently and log only the shape (names and kinds, never the token).
        string? Text(System.Text.Json.JsonElement e) => e.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => e.GetString(),
            System.Text.Json.JsonValueKind.Number => e.GetRawText(),
            _ => null
        };
        string Shape(System.Text.Json.JsonElement e) => e.ValueKind == System.Text.Json.JsonValueKind.Object
            ? "{" + string.Join(",", e.EnumerateObject().Select(p => p.Name + ":" + Shape(p.Value))) + "}"
            : e.ValueKind.ToString();

        string? mid = null, token = null;
        if (body.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var name in new[] { "mid", "aid", "uid" })
                if (mid == null && body.TryGetProperty(name, out var m)) mid = Text(m);
            if (body.TryGetProperty("token", out var t))
                token = t.ValueKind == System.Text.Json.JsonValueKind.Object && t.TryGetProperty("token", out var inner)
                    ? Text(inner)
                    : Text(t);
            if (token == null && body.TryGetProperty("stoken", out var s)) token = Text(s);
        }

        Logger.Debug($"verifySToken: shape={Shape(body)} mid={mid} token_len={token?.Length ?? 0}");
        return new NewTokenLoginHandler().HandleGlobal(mid, token);
    }

    [HttpPost("/hkrpg_global/mdk/shield/api/verify")]
    [HttpPost("/{gameKey}/mdk/shield/api/verify")]
    public JsonResult Verify([FromBody] VerifyReqJson req)
    {
        Logger.Debug($"shield verify: uid={req.uid} token_len={(req.token?.Length ?? 0)}");
        return new TokenLoginHandler().Handle(req.uid!, req.token!);
    }

    [HttpPost("/hkrpg_global/combo/granter/login/v2/login")]
    [HttpPost("/{gameKey}/combo/granter/login/v2/login")]
    public JsonResult LoginV2([FromBody] LoginV2ReqJson req)
    {
        Logger.Debug($"combo login v2: app_id={req.app_id} channel_id={req.channel_id} data_len={(req.data?.Length ?? 0)}");
        return new ComboTokenGranterHandler().Handle(req.app_id, req.channel_id, req.data!, req.device!, req.sign!);
    }

    [HttpPost("/account/ma-cn-passport/app/loginByPassword")]
    public ContentResult NewLogin([FromBody] NewLoginReqJson req)
    {
        Logger.Debug($"ma loginByPassword: account={req.account}");
        return new NewUsernameLoginHandler2().Handle(req.account!, req.password!);
    }

    [HttpPost("/account/ma-cn-session/app/verify")]
    public ContentResult NewVerify([FromBody] VerifySTokenReqJson req)
    {
        Logger.Debug($"ma verify: mid={req.mid}, token={req.token.token}");
        return new NewTokenLoginHandler().Handle(req.mid!, req.token.token!);
    }

    [HttpGet("/hkrpg_global/combo/granter/api/getConfig")]
    [HttpGet("/{gameKey}/combo/granter/api/getConfig")]
    public ContentResult GetConfig()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"protocol\":true,\"qr_enabled\":false,\"log_level\":\"INFO\",\"announce_url\":\"\",\"push_alias_type\":0,\"disable_ysdk_guard\":true,\"enable_announce_pic_popup\":false,\"app_name\":\"崩�??RPG\",\"qr_enabled_apps\":{\"bbs\":false,\"cloud\":false},\"qr_app_icons\":{\"app\":\"\",\"bbs\":\"\",\"cloud\":\"\"},\"qr_cloud_display_name\":\"\",\"enable_user_center\":true,\"functional_switch_configs\":{}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/hkrpg_global/combo/red_dot/list")]
    [HttpPost("/hkrpg_global/combo/red_dot/list")]
    [HttpGet("/{gameKey}/combo/red_dot/list")]
    [HttpPost("/{gameKey}/combo/red_dot/list")]
    public ContentResult RedDot()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"infos\":[]}}", ContentType = "application/json"
        };
    }

    [HttpGet("/common/hkrpg_global/announcement/api/getAlertAnn")]
    [HttpGet("/common/{gameKey}/announcement/api/getAlertAnn")]
    public ContentResult AlertAnn()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"alert\":false,\"alert_id\":0,\"remind\":false,\"extra_remind\":false}}",
            ContentType = "application/json"
        };
    }


    [HttpGet("/common/hkrpg_global/announcement/api/getAlertPic")]
    [HttpGet("/common/{gameKey}/announcement/api/getAlertPic")]
    public ContentResult AlertPic()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"total\":0,\"list\":[]}}",
            ContentType = "application/json"
        };
    }


    [HttpGet("/hkrpg_global/mdk/shield/api/loadConfig")]
    [HttpGet("/{gameKey}/mdk/shield/api/loadConfig")]
    public ContentResult LoadConfig()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"id\":24,\"game_key\":\"hkrpg_global\",\"client\":\"PC\",\"identity\":\"I_IDENTITY\",\"guest\":false,\"ignore_versions\":\"\",\"scene\":\"S_NORMAL\",\"name\":\"崩�??RPG\",\"disable_regist\":false,\"enable_email_captcha\":false,\"thirdparty\":[\"fb\",\"tw\",\"gl\",\"ap\"],\"disable_mmt\":false,\"server_guest\":false,\"thirdparty_ignore\":{},\"enable_ps_bind_account\":false,\"thirdparty_login_configs\":{\"tw\":{\"token_type\":\"TK_GAME_TOKEN\",\"game_token_expires_in\":2592000},\"ap\":{\"token_type\":\"TK_GAME_TOKEN\",\"game_token_expires_in\":604800},\"fb\":{\"token_type\":\"TK_GAME_TOKEN\",\"game_token_expires_in\":2592000},\"gl\":{\"token_type\":\"TK_GAME_TOKEN\",\"game_token_expires_in\":604800}},\"initialize_firebase\":false,\"bbs_auth_login\":false,\"bbs_auth_login_ignore\":[],\"fetch_instance_id\":false,\"enable_flash_login\":false}}",
            ContentType = "application/json"
        };
    }


    // === EXTRA ===

    [HttpPost("/hkrpg_global/combo/granter/api/compareProtocolVersion")]
    [HttpPost("/{gameKey}/combo/granter/api/compareProtocolVersion")]
    public ContentResult CompareProtocolVer()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"modified\":false,\"protocol\":null}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/hkrpg_global/mdk/agreement/api/getAgreementInfos")]
    [HttpGet("/{gameKey}/mdk/agreement/api/getAgreementInfos")]
    public ContentResult GetAgreementInfo()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"marketing_agreements\":[]}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/combo/box/api/config/sdk/combo")]
    public ContentResult Combo()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"vals\":{\"kibana_pc_config\":\"{ \\\"enable\\\": 0, \\\"level\\\": \\\"Info\\\",\\\"modules\\\": [\\\"download\\\"] }\\n\",\"network_report_config\":\"{ \\\"enable\\\": 0, \\\"status_codes\\\": [206], \\\"url_paths\\\": [\\\"dataUpload\\\", \\\"red_dot\\\"] }\\n\",\"list_price_tierv2_enable\":\"false\\n\",\"pay_payco_centered_host\":\"bill.payco.com\",\"telemetry_config\":\"{\\n \\\"dataupload_enable\\\": 0,\\n}\",\"enable_web_dpi\":\"true\"}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/combo/box/api/config/sw/precache")]
    public ContentResult Precache()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"vals\":{\"url\":\"\",\"enable\":\"false\"}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/device-fp/api/getFp")]
    public JsonResult GetFp([FromQuery] string device_fp)
    {
        return new FingerprintHandler().GetFp(device_fp);
    }

    [HttpGet("/device-fp/api/getExtList")]
    public ContentResult GetExtList()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"code\":200,\"msg\":\"ok\",\"ext_list\":[],\"pkg_list\":[],\"pkg_str\":\"/vK5WTh5SS3SAj8Zm0qPWg==\"}}",
            ContentType = "application/json"
        };
    }

    [HttpPost("/data_abtest_api/config/experiment/list")]
    public ContentResult GetExperimentList()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"success\":true,\"message\":\"\",\"data\":[{\"code\":1000,\"type\":2,\"config_id\":\"14\",\"period_id\":\"6125_197\",\"version\":\"1\",\"configs\":{\"cardType\":\"direct\"}}]}",
            ContentType = "application/json"
        };
    }
}
