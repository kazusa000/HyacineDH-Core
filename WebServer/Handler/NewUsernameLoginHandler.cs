using HyacineCore.Server.Database.Account;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer.Objects;
using Microsoft.AspNetCore.Mvc;
using static HyacineCore.Server.WebServer.Objects.NewLoginResJson;

namespace HyacineCore.Server.WebServer.Handler;

public class NewUsernameLoginHandler
{
    public JsonResult Handle(string account, string password)
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
                return new JsonResult(new NewLoginResJson { message = "Account not found", retcode = -201 });
            }
        }

        if (accountData != null)
        {
            res.message = "OK";
            res.data = new VerifyData(accountData.Uid.ToString(), accountData.Username + "@egglink.me",
                accountData.GenerateDispatchToken());

            res.data.user_info.account_name = accountData.Username;
            res.data.user_info.area_code = "**";
            res.data.user_info.country = "US";
            res.data.user_info.is_email_verify = "1";
        }

        return new JsonResult(res);
    }
}
