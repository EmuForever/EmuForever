// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using System;

namespace Game.Chat.Commands
{
    [CommandGroup("bnetaccount")]
    class BNetAccountCommands
    {
        [Command("create", RBACPermissions.CommandBnetAccountCreate, true)]
        static bool HandleAccountCreateCommand(CommandHandler handler, string accountName, string password, bool? createGameAccount)
        {
            if (accountName.IsEmpty() || !accountName.Contains('@'))
            {
                handler.SendSysMessage(ForeverStrings.AccountInvalidBnetName);
                return false;
            }

            bool withGameAccount = createGameAccount.GetValueOrDefault(true);
            string gameAccountName;
            switch (Global.BNetAccountMgr.CreateBattlenetAccount(accountName, password, withGameAccount, out gameAccountName))
            {
                case AccountOpResult.Ok:
                    if (withGameAccount)
                        handler.SendSysMessage(ForeverStrings.AccountCreatedBnetWithGame, accountName, gameAccountName);
                    else
                        handler.SendSysMessage(ForeverStrings.AccountCreated, accountName);

                    if (handler.GetSession() != null)
                    {
                        Log.outInfo(LogFilter.Player, 
                            $"Account: {handler.GetSession().GetAccountId()} (IP: {handler.GetSession().GetRemoteAddress()}) " +
                            $"Character:[{handler.GetSession().GetPlayer().GetName()}] ({handler.GetSession().GetPlayer().GetGUID()}) " +
                            $"created Battle.net account " +
                            $"{accountName}{(withGameAccount ? " with game account " : "")}{(withGameAccount ? gameAccountName : "")}");
                    }
                    break;
                case AccountOpResult.NameTooLong:
                    handler.SendSysMessage(ForeverStrings.AccountNameTooLong);
                    return false;
                case AccountOpResult.PassTooLong:
                    handler.SendSysMessage(ForeverStrings.AccountPassTooLong);
                    return false;
                case AccountOpResult.NameAlreadyExist:
                    handler.SendSysMessage(ForeverStrings.AccountAlreadyExist);
                    return false;
                case AccountOpResult.DBInternalError:
                    handler.SendSysMessage(ForeverStrings.AccountNotCreatedSqlError, accountName);
                    return false;
                default:
                    handler.SendSysMessage(ForeverStrings.AccountNotCreated, accountName);
                    return false;
            }

            return true;
        }

        [Command("gameaccountcreate", RBACPermissions.CommandBnetAccountCreateGame, true)]
        static bool HandleGameAccountCreateCommand(CommandHandler handler, string bnetAccountName)
        {
            int accountId = Global.BNetAccountMgr.GetId(bnetAccountName);
            if (accountId == 0)
            {
                handler.SendSysMessage(ForeverStrings.AccountNotExist, bnetAccountName);
                return false;
            }

            byte index = (byte)(Global.BNetAccountMgr.GetMaxIndex(accountId) + 1);
            string accountName = accountId.ToString() + '#' + index;

            // Generate random hex string for password, these accounts must not be logged on with GRUNT
            byte[] randPassword = Array.Empty<byte>().GenerateRandomKey(8);
            switch (Global.AccountMgr.CreateAccount(accountName, randPassword.ToHexString(), bnetAccountName, accountId, index))
            {
                case AccountOpResult.Ok:
                    handler.SendSysMessage(ForeverStrings.AccountCreated, accountName);
                    if (handler.GetSession() != null)
                    {
                        Log.outInfo(LogFilter.Player, 
                            $"Account: {handler.GetSession().GetAccountId()} (IP: {handler.GetSession().GetRemoteAddress()}) " +
                            $"Character:[{handler.GetSession().GetPlayer().GetName()}] ({handler.GetSession().GetPlayer().GetGUID()}) " +
                            $"created Account {accountName} (Email: '{bnetAccountName}')");
                    }
                    break;
                case AccountOpResult.NameTooLong:
                    handler.SendSysMessage(ForeverStrings.AccountNameTooLong);
                    return false;
                case AccountOpResult.PassTooLong:
                    handler.SendSysMessage(ForeverStrings.AccountPassTooLong);
                    return false;
                case AccountOpResult.NameAlreadyExist:
                    handler.SendSysMessage(ForeverStrings.AccountAlreadyExist);
                    return false;
                case AccountOpResult.DBInternalError:
                    handler.SendSysMessage(ForeverStrings.AccountNotCreatedSqlError, accountName);
                    return false;
                default:
                    handler.SendSysMessage(ForeverStrings.AccountNotCreated, accountName);
                    return false;
            }

            return true;
        }

        [Command("link", RBACPermissions.CommandBnetAccountLink, true)]
        static bool HandleAccountLinkCommand(CommandHandler handler, string bnetAccountName, string gameAccountName)
        {
            switch (Global.BNetAccountMgr.LinkWithGameAccount(bnetAccountName, gameAccountName))
            {
                case AccountOpResult.Ok:
                    handler.SendSysMessage(ForeverStrings.AccountBnetLinked, bnetAccountName, gameAccountName);
                    break;
                case AccountOpResult.NameNotExist:
                    handler.SendSysMessage(ForeverStrings.AccountOrBnetDoesNotExist, bnetAccountName, gameAccountName);
                    break;
                case AccountOpResult.BadLink:
                    handler.SendSysMessage( ForeverStrings.AccountAlreadyLinked, gameAccountName);
                    break;
                default:
                    break;
            }

            return true;
        }

        [Command("listgameaccounts", RBACPermissions.CommandBnetAccountListGameAccounts, true)]
        static bool HandleListGameAccountsCommand(CommandHandler handler, string battlenetAccountName)
        {
            PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.SEL_BNET_GAME_ACCOUNT_LIST);
            stmt.SetString(0, battlenetAccountName);

            SQLResult accountList = DB.Login.Query(stmt);
            if (!accountList.IsEmpty())
            {
                var formatDisplayName = new Func<string, string>(name =>
                {
                    int index = name.IndexOf('#');
                    if (index > 0)
                        return "WoW" + name[++index..];
                    else
                        return name;
                });

                handler.SendSysMessage("----------------------------------------------------");
                handler.SendSysMessage(ForeverStrings.AccountBnetListHeader);
                handler.SendSysMessage("----------------------------------------------------");
                do
                {
                    handler.SendSysMessage($"| {accountList.Read<uint>(0),10} | {accountList.Read<string>(1),16} | {formatDisplayName(accountList.Read<string>(1)),16} |");
                } while (accountList.NextRow());
                handler.SendSysMessage("----------------------------------------------------");
            }
            else
                handler.SendSysMessage(ForeverStrings.AccountBnetListNoAccounts, battlenetAccountName);

            return true;
        }

        [Command("password", RBACPermissions.CommandBnetAccountPassword, true)]
        static bool HandleAccountPasswordCommand(CommandHandler handler, string oldPassword, string newPassword, string passwordConfirmation)
        {
            // We compare the old, saved password to the entered old password - no Chance for the unauthorized.
            if (!Global.BNetAccountMgr.CheckPassword(handler.GetSession().GetBattlenetAccountId(), oldPassword))
            {
                handler.SendSysMessage(ForeverStrings.CommandWrongoldpassword);

                Log.outInfo(LogFilter.Player, 
                    $"Battle.net account: {handler.GetSession().GetBattlenetAccountId()} (IP: {handler.GetSession().GetRemoteAddress()}) " +
                    $"Character:[{handler.GetSession().GetPlayer().GetName()}] ({handler.GetSession().GetPlayer().GetGUID()}) " +
                    $"Tried to change password, but the provided old password is wrong.");
                return false;
            }

            // Making sure that newly entered password is correctly entered.
            if (newPassword != passwordConfirmation)
            {
                handler.SendSysMessage(ForeverStrings.NewPasswordsNotMatch);
                return false;
            }

            // Changes password and prints result.
            AccountOpResult result = Global.BNetAccountMgr.ChangePassword(handler.GetSession().GetBattlenetAccountId(), newPassword);
            switch (result)
            {
                case AccountOpResult.Ok:
                    handler.SendSysMessage(ForeverStrings.CommandPassword);
                    Log.outInfo(LogFilter.Player, 
                        $"Battle.net account: {handler.GetSession().GetBattlenetAccountId()} (IP: {handler.GetSession().GetRemoteAddress()}) " +
                        $"Character:[{handler.GetSession().GetPlayer().GetName()}] ({handler.GetSession().GetPlayer().GetGUID()}) Changed Password.");
                    break;
                case AccountOpResult.PassTooLong:
                    handler.SendSysMessage(ForeverStrings.PasswordTooLong);
                    return false;
                default:
                    handler.SendSysMessage(ForeverStrings.CommandNotchangepassword);
                    return false;
            }

            return true;
        }

        [Command("unlink", RBACPermissions.CommandBnetAccountUnlink, true)]
        static bool HandleAccountUnlinkCommand(CommandHandler handler, string gameAccountName)
        {
            switch (Global.BNetAccountMgr.UnlinkGameAccount(gameAccountName))
            {
                case AccountOpResult.Ok:
                    handler.SendSysMessage(ForeverStrings.AccountBnetUnlinked, gameAccountName);
                    break;
                case AccountOpResult.NameNotExist:
                    handler.SendSysMessage(ForeverStrings.AccountNotExist, gameAccountName);
                    break;
                case AccountOpResult.BadLink:
                    handler.SendSysMessage(ForeverStrings.AccountBnetNotLinked, gameAccountName);
                    break;
                default:
                    break;
            }

            return true;
        }

        [CommandGroup("lock")]
        class AccountLockCommands
        {
            [Command("country", RBACPermissions.CommandBnetAccountLockCountry, true)]
            static bool HandleAccountLockCountryCommand(CommandHandler handler, bool state)
            {
                /*if (state)
                {
                    if (IpLocationRecord const* location = sIPLocation->GetLocationRecord(handler->GetSession()->GetRemoteAddress()))
            {
                        LoginDatabasePreparedStatement* stmt = LoginDatabase.GetPreparedStatement(LOGIN_UPD_BNET_ACCOUNT_LOCK_CONTRY);
                        stmt->setString(0, location->CountryCode);
                        stmt->setUInt32(1, handler->GetSession()->GetBattlenetAccountId());
                        LoginDatabase.Execute(stmt);
                        handler->PSendSysMessage(LANG_COMMAND_ACCLOCKLOCKED);
                    }
            else
                    {
                        handler->PSendSysMessage("IP2Location] No information");
                        TC_LOG_DEBUG("server.bnetserver", "IP2Location] No information");
                    }
                }
                else
                {
                    LoginDatabasePreparedStatement* stmt = LoginDatabase.GetPreparedStatement(LOGIN_UPD_BNET_ACCOUNT_LOCK_CONTRY);
                    stmt->setString(0, "00");
                    stmt->setUInt32(1, handler->GetSession()->GetBattlenetAccountId());
                    LoginDatabase.Execute(stmt);
                    handler->PSendSysMessage(LANG_COMMAND_ACCLOCKUNLOCKED);
                }
                */
                return true;
            }

            [Command("ip", RBACPermissions.CommandBnetAccountLockIp, true)]
            static bool HandleAccountLockIpCommand(CommandHandler handler, bool state)
            {
                PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.UPD_BNET_ACCOUNT_LOCK);

                if (state)
                {
                    stmt.SetBool(0, true);                                     // locked
                    handler.SendSysMessage(ForeverStrings.CommandAcclocklocked);
                }
                else
                {
                    stmt.SetBool(0, false);                                    // unlocked
                    handler.SendSysMessage(ForeverStrings.CommandAcclockunlocked);
                }

                stmt.SetInt32(1, handler.GetSession().GetBattlenetAccountId());

                DB.Login.Execute(stmt);
                return true;
            }
        }

        [CommandGroup("set")]
        class AccountSetCommands
        {
            [Command("password", RBACPermissions.CommandBnetAccountSetPassword, true)]
            static bool HandleAccountSetPasswordCommand(CommandHandler handler, string accountName, string password, string passwordConfirmation)
            {
                int targetAccountId = Global.BNetAccountMgr.GetId(accountName);
                if (targetAccountId == 0)
                {
                    handler.SendSysMessage(ForeverStrings.AccountNotExist, accountName);
                    return false;
                }

                if (password != passwordConfirmation)
                {
                    handler.SendSysMessage(ForeverStrings.NewPasswordsNotMatch);
                    return false;
                }

                AccountOpResult result = Global.BNetAccountMgr.ChangePassword(targetAccountId, password);
                switch (result)
                {
                    case AccountOpResult.Ok:
                        handler.SendSysMessage(ForeverStrings.CommandPassword);
                        break;
                    case AccountOpResult.NameNotExist:
                        handler.SendSysMessage(ForeverStrings.AccountNotExist, accountName);
                        return false;
                    case AccountOpResult.PassTooLong:
                        handler.SendSysMessage(ForeverStrings.PasswordTooLong);
                        return false;
                    default:
                        break;
                }
                return true;
            }
        }
    }
}
