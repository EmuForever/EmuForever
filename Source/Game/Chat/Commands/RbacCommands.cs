// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Accounts;
using System.Collections.Generic;

namespace Game.Chat.Commands
{
    [CommandGroup("rbac")]
    class RbacComands
    {
        [Command("list", RBACPermissions.CommandRbacList, true)]
        static bool HandleRBACListPermissionsCommand(CommandHandler handler, int? permId)
        {
            if (!permId.HasValue)
            {
                var permissions = Global.AccountMgr.GetRBACPermissionList();
                handler.SendSysMessage(ForeverStrings.RbacListPermissionsHeader);
                foreach (var (_, permission) in permissions)
                    handler.SendSysMessage(ForeverStrings.RbacListElement, permission.GetId(), permission.GetName());
            }
            else
            {
                RBACPermission permission = Global.AccountMgr.GetRBACPermission((RBACPermissions)permId.Value);
                if (permission == null)
                {
                    handler.SendSysMessage(ForeverStrings.RbacWrongParameterId, permId.Value);
                    return false;
                }

                handler.SendSysMessage(ForeverStrings.RbacListPermissionsHeader);
                handler.SendSysMessage(ForeverStrings.RbacListElement, permission.GetId(), permission.GetName());
                handler.SendSysMessage(ForeverStrings.RbacListPermsLinkedHeader);
                foreach (var linkedPerm in permission.GetLinkedPermissions())
                {
                    RBACPermission rbacPermission = Global.AccountMgr.GetRBACPermission(linkedPerm);
                    if (rbacPermission != null)
                        handler.SendSysMessage(ForeverStrings.RbacListElement, rbacPermission.GetId(), rbacPermission.GetName());
                }
            }

            return true;
        }

        [CommandGroup("account")]
        class RbacAccountCommands
        {
            [Command("deny", RBACPermissions.CommandRbacAccPermDeny, true)]
            static bool HandleRBACPermDenyCommand(CommandHandler handler, AccountIdentifier account, int permId, int? realmId)
            {
                if (account == null)
                    account = AccountIdentifier.FromTarget(handler);
                if (account == null)
                    return false;

                if (handler.HasLowerSecurityAccount(null, account.GetID(), true))
                    return false;

                if (!realmId.HasValue)
                    realmId = -1;

                RBACCommandData data = GetRBACData(account);

                RBACCommandResult result = data.rbac.DenyPermission((RBACPermissions)permId, realmId.Value);
                RBACPermission  permission = Global.AccountMgr.GetRBACPermission((RBACPermissions)permId);

                switch (result)
                {
                    case RBACCommandResult.CantAddAlreadyAdded:
                        handler.SendSysMessage(ForeverStrings.RbacPermDeniedInList, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.InGrantedList:
                        handler.SendSysMessage(ForeverStrings.RbacPermDeniedInGrantedList, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.OK:
                        handler.SendSysMessage(ForeverStrings.RbacPermDenied, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.IdDoesNotExists:
                        handler.SendSysMessage(ForeverStrings.RbacWrongParameterId, permId);
                        break;
                    default:
                        break;
                }

                return true;
            }

            [Command("grant", RBACPermissions.CommandRbacAccPermGrant, true)]
            static bool HandleRBACPermGrantCommand(CommandHandler handler, AccountIdentifier account, int permId, int? realmId)
            {
                if (account == null)
                    account = AccountIdentifier.FromTarget(handler);
                if (account == null)
                    return false;

                if (handler.HasLowerSecurityAccount(null, account.GetID(), true))
                    return false;

                if (!realmId.HasValue)
                    realmId = -1;

                RBACCommandData data = GetRBACData(account);

                RBACCommandResult result = data.rbac.GrantPermission((RBACPermissions)permId, realmId.Value);
                RBACPermission permission = Global.AccountMgr.GetRBACPermission((RBACPermissions)permId);

                switch (result)
                {
                    case RBACCommandResult.CantAddAlreadyAdded:
                        handler.SendSysMessage(ForeverStrings.RbacPermGrantedInList, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.InDeniedList:
                        handler.SendSysMessage(ForeverStrings.RbacPermGrantedInDeniedList, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.OK:
                        handler.SendSysMessage(ForeverStrings.RbacPermGranted, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.IdDoesNotExists:
                        handler.SendSysMessage(ForeverStrings.RbacWrongParameterId, permId);
                        break;
                    default:
                        break;
                }

                return true;
            }

            [Command("list", RBACPermissions.CommandRbacAccPermList, true)]
            static bool HandleRBACPermListCommand(CommandHandler handler, AccountIdentifier account)
            {
                if (account == null)
                    account = AccountIdentifier.FromTarget(handler);
                if (account == null)
                    return false;

                RBACCommandData data = GetRBACData(account);

                handler.SendSysMessage(ForeverStrings.RbacListHeaderGranted, data.rbac.GetId(), data.rbac.GetName());
                var granted = data.rbac.GetGrantedPermissions();
                if (granted.Empty())
                    handler.SendSysMessage(ForeverStrings.RbacListEmpty);
                else
                {
                    foreach (var id in granted)
                    {
                        RBACPermission  permission = Global.AccountMgr.GetRBACPermission(id);
                        handler.SendSysMessage(ForeverStrings.RbacListElement, permission.GetId(), permission.GetName());
                    }
                }

                handler.SendSysMessage(ForeverStrings.RbacListHeaderDenied, data.rbac.GetId(), data.rbac.GetName());
                var denied = data.rbac.GetDeniedPermissions();
                if (denied.Empty())
                    handler.SendSysMessage(ForeverStrings.RbacListEmpty);
                else
                {
                    foreach (var id in denied)
                    {
                        RBACPermission  permission = Global.AccountMgr.GetRBACPermission(id);
                        handler.SendSysMessage(ForeverStrings.RbacListElement, permission.GetId(), permission.GetName());
                    }
                }
                handler.SendSysMessage(ForeverStrings.RbacListHeaderBySecLevel, data.rbac.GetId(), data.rbac.GetName(), data.rbac.GetSecurityLevel());
                var defaultPermissions = Global.AccountMgr.GetRBACDefaultPermissions(data.rbac.GetSecurityLevel());
                if (defaultPermissions.Empty())
                    handler.SendSysMessage(ForeverStrings.RbacListEmpty);
                else
                {
                    foreach (var id in defaultPermissions)
                    {
                        RBACPermission  permission = Global.AccountMgr.GetRBACPermission(id);
                        handler.SendSysMessage(ForeverStrings.RbacListElement, permission.GetId(), permission.GetName());
                    }
                }

                return true;
            }

            [Command("revoke", RBACPermissions.CommandRbacAccPermRevoke, true)]
            static bool HandleRBACPermRevokeCommand(CommandHandler handler, AccountIdentifier account, int permId, int? realmId)
            {
                if (account == null)
                    account = AccountIdentifier.FromTarget(handler);
                if (account == null)
                    return false;

                if (handler.HasLowerSecurityAccount(null, account.GetID(), true))
                    return false;

                if (!realmId.HasValue)
                    realmId = -1;

                RBACCommandData data = GetRBACData(account);

                RBACCommandResult result = data.rbac.RevokePermission((RBACPermissions)permId, realmId.Value);
                RBACPermission  permission = Global.AccountMgr.GetRBACPermission((RBACPermissions)permId);

                switch (result)
                {
                    case RBACCommandResult.CantRevokeNotInList:
                        handler.SendSysMessage(ForeverStrings.RbacPermRevokedNotInList, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.OK:
                        handler.SendSysMessage(ForeverStrings.RbacPermRevoked, permId, permission.GetName(),
                            realmId.Value, data.rbac.GetId(), data.rbac.GetName());
                        break;
                    case RBACCommandResult.IdDoesNotExists:
                        handler.SendSysMessage(ForeverStrings.RbacWrongParameterId, permId);
                        break;
                    default:
                        break;
                }

                return true;
            }
        }

        static RBACCommandData GetRBACData(AccountIdentifier account)
        {
            if (account.IsConnected())
                return new RBACCommandData() { rbac = account.GetConnectedSession().GetRBACData(), needDelete = false };

            RBACData rbac = new(account.GetID(), account.GetName(), Global.WorldMgr.GetRealmId().Index, (byte)Global.AccountMgr.GetSecurity(account.GetID(), Global.WorldMgr.GetRealmId().Index));
            rbac.LoadFromDB();

            return new RBACCommandData() { rbac = rbac, needDelete = true };
        }

        class RBACCommandData
        {
            public RBACData rbac;
            public bool needDelete;
        }
    }
}
