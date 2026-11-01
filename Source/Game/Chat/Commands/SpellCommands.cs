// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Spells;
using System.Collections.Generic;

namespace Game.Chat
{
    class SpellCommands
    {
        [CommandNonGroup("cooldown", RBACPermissions.CommandCooldown)]
        static bool HandleCooldownCommand(CommandHandler handler, int? spellIdArg)
        {
            Unit target = handler.GetSelectedUnit();
            if (target == null)
            {
                handler.SendSysMessage(ForeverStrings.PlayerNotFound);
                return false;
            }

            Player owner = target.GetCharmerOrOwnerPlayerOrPlayerItself();
            if (owner == null)
            {
                owner = handler.GetSession().GetPlayer();
                target = owner;
            }

            string nameLink = handler.GetNameLink(owner);
            if (!spellIdArg.HasValue)
            {
                target.GetSpellHistory().ResetAllCooldowns();
                target.GetSpellHistory().ResetAllCharges();
                handler.SendSysMessage(ForeverStrings.RemoveallCooldown, nameLink);
            }
            else
            {
                SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(spellIdArg.Value, target.GetMap().GetDifficultyID());
                if (spellInfo == null)
                {
                    handler.SendSysMessage(ForeverStrings.UnknownSpell, owner == handler.GetSession().GetPlayer() ? handler.GetForeverString(ForeverStrings.You) : nameLink);
                    return false;
                }

                target.GetSpellHistory().ResetCooldown(spellInfo.Id, true);
                target.GetSpellHistory().ResetCharges(spellInfo.ChargeCategoryId);
                handler.SendSysMessage(ForeverStrings.RemoveallCooldown, spellInfo.Id, owner == handler.GetSession().GetPlayer() ? handler.GetForeverString(ForeverStrings.You) : nameLink);
            }
            return true;
        }

        [CommandNonGroup("aura", RBACPermissions.CommandAura)]
        static bool HandleAuraCommand(CommandHandler handler, int spellId)
        {
            Unit target = handler.GetSelectedUnit();
            if (target == null)
            {
                handler.SendSysMessage(ForeverStrings.SelectCharOrCreature);

                return false;
            }

            SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(spellId, target.GetMap().GetDifficultyID());
            if (spellInfo == null)
                return false;

            ObjectGuid castId = ObjectGuid.Create(HighGuid.Cast, SpellCastSource.Normal, target.GetMapId(), spellId, target.GetMap().GenerateLowGuid(HighGuid.Cast));
            AuraCreateInfo createInfo = new(castId, spellInfo, target.GetMap().GetDifficultyID(), SpellConst.MaxEffectMask, target);
            createInfo.SetCaster(target);

            Aura.TryRefreshStackOrCreate(createInfo);

            return true;
        }

        [CommandNonGroup("unaura", RBACPermissions.CommandUnaura)]
        static bool HandleUnAuraCommand(CommandHandler handler, int spellId = 0)
        {
            Unit target = handler.GetSelectedUnit();
            if (target == null)
            {
                handler.SendSysMessage(ForeverStrings.SelectCharOrCreature);

                return false;
            }

            if (spellId == 0)
            {
                target.RemoveAllAuras();
                return true;
            }

            SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(spellId, Difficulty.None);
            if (spellInfo != null)
            {
                target.RemoveAurasDueToSpell(spellInfo.Id);
                return true;
            }

            return true;
        }

        [CommandNonGroup("setskill", RBACPermissions.CommandSetskill)]
        static bool HandleSetSkillCommand(CommandHandler handler, int skillId, int level, int? maxSkillArg)
        {
            Player target = handler.GetSelectedPlayerOrSelf();
            if (target == null)
            {
                handler.SendSysMessage(ForeverStrings.NoCharSelected);
                return false;
            }

            SkillLineRecord skillLine = CliDB.SkillLineStorage.LookupByKey((SkillType)skillId);
            if (skillLine == null)
            {
                handler.SendSysMessage(ForeverStrings.InvalidSkillId, skillId);
                return false;
            }

            bool targetHasSkill = target.GetSkillValue((SkillType)skillId) != 0;

            // If our target does not yet have the skill they are trying to add to them, the chosen level also becomes
            // the max level of the new profession.
            ushort max = (ushort)maxSkillArg.GetValueOrDefault(targetHasSkill ? target.GetPureMaxSkillValue((SkillType)skillId) : level);

            if (level == 0 || level > max)
                return false;

            // If the player has the skill, we get the current skill step. If they don't have the skill, we
            // add the skill to the player's book with step 1 (which is the first rank, in most cases something
            // like 'Apprentice <skill>'.
            target.SetSkill(skillId, targetHasSkill ? target.GetSkillStep(skillId) : 1, level, max);
            handler.SendSysMessage(ForeverStrings.SetSkill, skillId, skillLine.DisplayName[handler.GetSessionDbcLocale()], handler.GetNameLink(target), level, max);
            return true;
        }
    }
}
