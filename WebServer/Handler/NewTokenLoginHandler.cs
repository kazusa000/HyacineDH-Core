using HyacineCore.Server.Database.Account;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer.Objects;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using static HyacineCore.Server.WebServer.Objects.NewLoginResJson;

namespace HyacineCore.Server.WebServer.Handler;

public class NewTokenLoginHandler
{
    // Global SDK (ma-passport verifySToken): same reply as appLoginByPassword. The saved token is kept rather than
    // rotated, so a session killed before the database is saved still finds the token the SDK holds.
    public JsonResult HandleGlobal(string? mid, string? token)
    {
        var account = int.TryParse(mid, out var uid) ? AccountData.GetAccountByUid(uid) : null;
        if (account == null || string.IsNullOrEmpty(token) || account.DispatchToken != token)
            return new JsonResult(new NewLoginResJson { message = "Account not found", retcode = -201 });

        var res = new NewLoginResJson
        {
            message = "OK",
            data = new VerifyData(account.Uid.ToString(), account.Username + "@egglink.me", token)
        };
        res.data.user_info.account_name = account.Username ?? "";
        res.data.user_info.area_code = "**";
        res.data.user_info.country = "US";
        return new JsonResult(res);
    }

    public ContentResult Handle(string mid, string token)
    {

        var account = AccountData.GetAccountByUid(int.Parse(mid));
        var res = new LoginResJson();
        string rsp = "";
        if (account == null || !account?.DispatchToken?.Equals(token) == true)
        {
            var errorRsp = "{\"retcode\":-201,\"message\":\"Account not found\"}";
            return new ContentResult()
            {
                ContentType = "application/json",
                Content = errorRsp,
            };
        }
        else
        {
            string uid = account.Uid.ToString();
            string dispatchToken = account.GenerateDispatchToken();
            string email = account.Username + "@egglink.me";

            rsp = $"{{\"retcode\":0,\"message\":\"OK\",\"data\":{{\"user_info\":{{\"aid\":\"{uid}\",\"mid\":\"{uid}\",\"account_name\":\"{account.Username}\",\"email\":\"{email}\",\"is_email_verify\":0,\"area_code\":\"**\",\"mobile\":\"\",\"safe_area_code\":\"\",\"safe_mobile\":\"\",\"realname\":\"\",\"identity_code\":\"\",\"rebind_area_code\":\"\",\"rebind_mobile\":\"\",\"rebind_mobile_time\":\"1\",\"links\":[],\"country\":\"CN\",\"password_time\":\"1\",\"is_adult\":0,\"unmasked_email\":\"\",\"unmasked_email_type\":0}},\"token\":{{\"token_type\":1,\"token\":\"{dispatchToken}\"}},\"ext_user_info\":{{\"guardian_email\":\"\",\"birth\":\"0\"}}}}}}";
        }

        return new ContentResult()
        {
            ContentType = "application/json",
            Content = rsp,
        };
    }
}
