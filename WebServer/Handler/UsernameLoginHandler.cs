using HyacineCore.Server.Database.Account;
using HyacineCore.Server.Util;
using HyacineCore.Server.WebServer.Objects;
using Microsoft.AspNetCore.Mvc;
using static HyacineCore.Server.WebServer.Objects.LoginResJson;

namespace HyacineCore.Server.WebServer.Handler;

public class UsernameLoginHandler
{
    public JsonResult Handle(string account, string password, bool isCrypto)
    {
        LoginResJson res = new();
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
                return new JsonResult(new LoginResJson { message = "Account not found", retcode = -201 });
            }
        }

        if (accountData != null)
        {
            res.message = "OK";
            res.data = new VerifyData(accountData.Uid.ToString(), accountData.Username + "@egglink.me",
                accountData.GenerateDispatchToken());
            res.data.account.name = accountData.Username;
            res.data.account.is_email_verify = "1";
        }

        return new JsonResult(res);
    }
}
