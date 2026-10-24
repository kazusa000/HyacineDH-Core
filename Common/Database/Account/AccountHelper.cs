using HyacineCore.Server.Util;

namespace HyacineCore.Server.Database.Account;

public static class AccountHelper
{
    public static void CreateAccount(string username, int uid)
    {
        if (AccountData.GetAccountByUserName(username) != null) throw new Exception("Account already exists");
        if (AccountData.GetAccountByUid(uid) != null)
        {
        }

        var newUid = uid;
        if (uid == 0)
        {
            newUid = 10001; // start from 10001
            while (AccountData.GetAccountByUid(newUid) != null) newUid++;
        }

        var per = ConfigManager.Config.ServerOption.DefaultPermissions;
        var perStr = string.Join(",", per);
        var account = new AccountData
        {
            Uid = newUid,
            Username = username,
            Permissions = perStr
        };
        DatabaseHelper.SaveInstance(account);
    }

    // The client encrypts the username differently on every login, so a local single-player server can pin all
    // username logins to one account (ServerOption.LocalAccountUid) instead of creating a new one each time.
    public static AccountData? GetLoginAccount(string username)
    {
        var uid = ConfigManager.Config.ServerOption.LocalAccountUid;
        return uid > 0 ? AccountData.GetAccountByUid(uid) : AccountData.GetAccountByUserName(username);
    }
}
