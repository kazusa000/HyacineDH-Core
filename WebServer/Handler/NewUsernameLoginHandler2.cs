using HyacineCore.Server.Database.Account;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer.Objects;
using Microsoft.AspNetCore.Mvc;

namespace HyacineCore.Server.WebServer.Handler;

public class NewUsernameLoginHandler2
{
    public ContentResult Handle(string account, string password)
    {
        NewLoginResJson res = new();
        var accountData = AccountHelper.GetLoginAccount(account);

        if (accountData == null)
        {
            if (ConfigManager.Config.ServerOption.AutoCreateUser)
            {
                AccountHelper.CreateAccount(account, ConfigManager.Config.ServerOption.LocalAccountUid);
                accountData = AccountHelper.GetLoginAccount(account);
            }
            else
            {
                var errorRsp = "{\"retcode\":-201,\"message\":\"Account not found\"}";
                return new ContentResult()
                {
                    ContentType = "application/json",
                    Content = errorRsp,
                };
            }
        }

        string rsp = "";
        if (accountData != null)
        {
            string uid = accountData.Uid.ToString();
            string dispatchToken = accountData.GenerateDispatchToken();
            string email = accountData.Username + "@egglink.me";

            rsp = $"{{\"retcode\":0,\"message\":\"OK\",\"data\":{{\"user_info\":{{\"aid\":\"{uid}\",\"mid\":\"{uid}\",\"account_name\":\"{accountData.Username}\",\"email\":\"{email}\",\"is_email_verify\":0,\"area_code\":\"**\",\"mobile\":\"\",\"safe_area_code\":\"\",\"safe_mobile\":\"\",\"realname\":\"\",\"identity_code\":\"\",\"rebind_area_code\":\"\",\"rebind_mobile\":\"\",\"rebind_mobile_time\":\"1\",\"links\":[],\"country\":\"CN\",\"password_time\":\"1\",\"is_adult\":0,\"unmasked_email\":\"\",\"unmasked_email_type\":0}},\"token\":{{\"token_type\":1,\"token\":\"{dispatchToken}\"}},\"ext_user_info\":{{\"guardian_email\":\"\",\"birth\":\"0\"}}}}}}";
        }

        return new ContentResult()
        {
            ContentType = "application/json",
            Content = rsp,
        };
    }
}
