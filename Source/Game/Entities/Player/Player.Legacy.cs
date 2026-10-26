using Framework.Constants;
using Game.DataStorage;
using System.Collections.Generic;
using System.Linq;
using Game.Systems.Legacy;
using Game.Networking.Packets;
using Framework.Database;
using Game.Spells;
using System;

namespace Game.Entities
{
    public partial class Player
    {
        // Classic 1.60: the Legacy system (micro menu "Legacy", LegacyMicroButtonMixin:IsLegacySystemUnlocked) opens with renown level 1
        // of the Legacy reward track faction 2802; renown level = quantity of its renown currency 3485. The client says it unlocks at
        // level 25. Port of the reference Player::UpdateClassicLegacyUnlock (E:\trinitycore_new Player.cpp).
        const int LegacyRenownCurrencyID = LegacyManager.RenownCurrencyId;
        const int LegacyRewardTrackFactionID = LegacyManager.RewardFactionId;
        const int LegacyTraitSystemID = LegacyManager.TraitSystemId;
        const int SpellCreateLegacyTraitConfig = 1282612; // SpellEffectName.CreateTraitTreeConfig, tree 1188
        const int LegacyUnlockLevel = 25;

        public LegacyAccountProgress LegacyProgress { get; private set; }
        int _legacyVersion = -1;
        bool _legacyInPvp;
        int _legacySavedRuleset = -1;
        const int LegacyResetGossipOption = 2146000045;

        public TraitConfig GetLegacyConfig()
        {
            int index = m_activePlayerData.TraitConfigs.FindIndexIf(config => config.Type == (int)TraitConfigType.Generic && config.TraitSystemID == LegacyManager.TraitSystemId);
            return index >= 0 ? m_activePlayerData.TraitConfigs[index] : null;
        }

        public bool CanResetLegacyAt(Creature trainer) => IsAlive() && !IsInCombat() && trainer != null &&
            GetNPCIfCanInteractWith(trainer.GetGUID(), NPCFlags1.Trainer, NPCFlags2.None) != null &&
            trainer.GetCreatureTemplate().TrainerClass == GetClass();

        public bool ResetLegacyTraits()
        {
            TraitConfig config = GetLegacyConfig();
            if (config == null || _legacyCommitPending || !IsAlive() || IsInCombat()) return false;
            foreach (TraitEntry entry in config.Entries)
            {
                TraitDefinitionRecord definition = CliDB.TraitDefinitionStorage.LookupByKey(CliDB.TraitNodeEntryStorage.LookupByKey(entry.TraitNodeEntryID)?.TraitDefinitionID ?? 0);
                if (definition != null && ((definition.SpellID != 0 && GetSpellHistory().HasCooldown(definition.SpellID)) ||
                    (definition.VisibleSpellID != 0 && GetSpellHistory().HasCooldown(definition.VisibleSpellID)))) return false;
            }
            TraitConfigPacket reset = new(config);
            reset.Entries.Clear();
            foreach (TraitEntry granted in TraitMgr.GetGrantedTraitEntriesForConfig(reset, this)) reset.Entries.Add(new TraitEntryPacket(granted));
            return CommitLegacyTraits(reset);
        }

        void LoadLegacyProgress()
        {
            LegacyProgress = LegacyManager.LoadAccount(this);
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_LEGACY_CHARACTER_STATE);
            stmt.SetInt64(0, GetGUID().GetCounter());
            SQLResult result = DB.Characters.Query(stmt);
            if (!result.IsEmpty()) _legacySavedRuleset = result.Read<int>(0);
            SynchronizeLegacyAchievements();
        }

        void AppendLegacyCharacterState(SQLTransaction transaction)
        {
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPSERT_LEGACY_CHARACTER_STATE);
            stmt.SetInt64(0, GetGUID().GetCounter()); stmt.SetInt32(1, LegacyManager.RulesetId); transaction.Append(stmt);
        }

        void SynchronizeLegacyAchievements()
        {
            if (LegacyProgress == null || _legacyVersion == LegacyProgress.Version) return;
            foreach (var completion in LegacyProgress.Snapshot())
                m_achievementSys.ImportLegacyAchievement(completion.Key, completion.Value);
            _legacyVersion = LegacyProgress.Version;
        }

        void UpdateLegacyProgress()
        {
            bool inPvp = GetMap().IsBattlegroundOrArena();
            if (_legacyInPvp != inPvp)
            {
                _legacyInPvp = inPvp;
                GetAuraEffect(1225477, 0)?.RecalculateAmount(this);
            }
            if (LegacyProgress == null || _legacyVersion == LegacyProgress.Version) return;
            SynchronizeLegacyAchievements();
            UpdateClassicLegacyUnlock();
            m_achievementSys.SendAllData(this);
        }

        public int GetLegacyPoints() => LegacyProgress?.Points ?? LegacyManager.EarnedPoints(GetCompletedAchievementIds());

        bool _legacyCommitPending;
        public bool CommitLegacyTraits(TraitConfigPacket state)
        {
            TraitConfig current = GetTraitConfig(state.ID);
            if (_legacyCommitPending || !IsAlive() || IsInCombat() || current == null || !LegacyManager.IsLegacy(new TraitConfigPacket(current)) ||
                !LegacyManager.IsLegacy(state) || TraitMgr.ValidateConfig(state, this) != TalentLearnResult.LearnOk) return false;
            _legacyCommitPending = true;
            SQLTransaction transaction = new();
            long guid = GetGUID().GetCounter();
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_CHAR_TRAIT_ENTRIES);
            stmt.SetInt64(0, guid); stmt.SetInt32(1, state.ID); transaction.Append(stmt);
            stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_CHAR_TRAIT_CONFIGS);
            stmt.SetInt64(0, guid); stmt.SetInt32(1, state.ID); transaction.Append(stmt);
            stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_CHAR_TRAIT_CONFIGS);
            stmt.SetInt64(0, guid); stmt.SetInt32(1, state.ID); stmt.SetInt32(2, (int)TraitConfigType.Generic);
            for (int i = 3; i <= 6; ++i) stmt.SetNull(i);
            stmt.SetInt32(7, LegacyManager.TraitSystemId); stmt.SetString(8, state.Name); transaction.Append(stmt);
            foreach (TraitEntryPacket entry in state.Entries)
            {
                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_CHAR_TRAIT_ENTRIES);
                stmt.SetInt64(0, guid); stmt.SetInt32(1, state.ID); stmt.SetInt32(2, entry.TraitNodeID);
                stmt.SetInt32(3, entry.TraitNodeEntryID); stmt.SetInt32(4, entry.Rank); stmt.SetInt32(5, entry.GrantedRanks);
                transaction.Append(stmt);
            }
            AppendLegacyCharacterState(transaction);
            GetSession().AddTransactionCallback(DB.Characters.AsyncCommitTransaction(transaction)).AfterComplete(success =>
            {
                _legacyCommitPending = false;
                if (!success)
                {
                    SendPacket(new TraitConfigCommitFailed(state.ID, 0, (int)TalentLearnResult.FailedCantDoThatRightNow));
                    Log.outError(LogFilter.Player, $"Legacy config {state.ID} persistence failed for {GetGUID()}.");
                    return;
                }
                if (GetSession().GetPlayer() != this) return;
                UpdateTraitConfig(state, 0, false);
                _legacySavedRuleset = LegacyManager.RulesetId;
                m_traitConfigStates.Remove(state.ID); // this exact snapshot was committed before publishing it
                Log.outInfo(LogFilter.Player, $"Legacy config {state.ID} committed for {GetGUID()} ({state.Entries.Sum(x => x.Rank)} purchased ranks).");
            });
            return true;
        }

        public int GetTraitDefinitionRank(int definitionId)
        {
            int rank = 0;
            foreach (TraitConfig config in m_activePlayerData.TraitConfigs)
            {
                if ((TraitConfigType)(int)config.Type == TraitConfigType.Combat
                    && config.ID.GetValue() != m_activePlayerData.ActiveCombatTraitConfigID.GetValue()) continue;
                foreach (TraitEntry entry in config.Entries)
                    if (CliDB.TraitNodeEntryStorage.LookupByKey(entry.TraitNodeEntryID)?.TraitDefinitionID == definitionId)
                        rank = Math.Max(rank, entry.Rank + entry.GrantedRanks);
            }
            return rank;
        }

        public void RefreshLegacyPassive(int definitionId, int spellId)
        {
            if (!LegacyDefinitions.PerkSpells.Contains(spellId)) return;
            SpellInfo spell = Global.SpellMgr.GetSpellInfo(spellId, Difficulty.None);
            if (spell == null || !spell.IsPassive()) return;
            CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
            int rank = GetTraitDefinitionRank(definitionId);
            foreach (TraitDefinitionEffectPointsRecord modifier in TraitMgr.GetTraitDefinitionEffectPointModifiers(definitionId))
            {
                if (modifier.EffectIndex < 0 || modifier.EffectIndex >= spell.GetEffects().Count) continue;
                float amount = Global.DB2Mgr.GetCurveValueAt(modifier.CurveID, rank);
                if (modifier.OperationType == TraitPointsOperationType.Multiply)
                    amount *= spell.GetEffect(modifier.EffectIndex).CalcBaseValue(this, null, 0, -1);
                args.AddSpellMod(SpellValueMod.BasePoint0 + modifier.EffectIndex, (int)amount);
            }
            RemoveAurasDueToSpell(spellId);
            if (rank > 0) CastSpell(this, spellId, args);
            if (spellId == 1225474) InitTalentForLevel();
        }

        public int GetClassicLegacyPerkAmount(int spellId, int effect = 0) => GetAuraEffect(spellId, effect)?.GetAmount() ?? 0;

        public bool IsLegacyReagentExempt(SpellInfo spell, int itemId) => spell.SpellFamilyName != SpellFamilyNames.Generic &&
            HasAura(1225503) && Global.ObjectMgr.IsLegacyVendorReagent(itemId);

        public ulong ApplyClassicLegacyPriceMod(ulong price, int priceType) =>
            LegacyEffectEngine.Price(price, GetTotalAuraModifierByMiscValue((AuraType)620, priceType));

        long _legacyFreeHelpfulUntil;
        public bool HasClassicFreeHelpfulSpells()
        {
            if (IsInCombat()) _legacyFreeHelpfulUntil = 0;
            return Environment.TickCount64 < _legacyFreeHelpfulUntil;
        }

        public void ApplyLegacyKillBenefits(Creature killed)
        {
            int amount = GetClassicLegacyPerkAmount(1225472);
            if (amount <= 0 || GetMap().Instanceable() || Formulas.XPGain(this, killed) <= 0) return;
            SpellInfo regen = Global.SpellMgr.GetSpellInfo(1261483, Difficulty.None);
            if (regen == null || regen.GetEffects().Count < 2 || regen.GetEffect(0).ApplyAuraPeriod <= 0) return;
            int ticks = Math.Max(1, (int)(regen.GetMaxDuration() / regen.GetEffect(0).ApplyAuraPeriod));
            CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
            args.AddSpellMod(SpellValueMod.BasePoint0, Math.Max(1, (int)(GetMaxHealth() * amount / (100.0 * ticks) + 0.5)));
            args.AddSpellMod(SpellValueMod.BasePoint1, GetMaxPower(PowerType.Mana) == 0 ? 0 : Math.Max(1, (int)(GetMaxPower(PowerType.Mana) * amount / (100.0 * ticks) + 0.5)));
            CastSpell(this, 1261483, args);
        }

        public void UseLegacyDedicatedStudy(int essenceCount)
        {
            SkillType lowest = 0;
            ushort value = ushort.MaxValue;
            int primaries = 0;
            bool maxed = true;
            foreach (SkillLineRecord skill in CliDB.SkillLineStorage.Values)
            {
                if (skill.ParentSkillLineID != 0 || (skill.CategoryID != SkillCategory.Profession && skill.CategoryID != SkillCategory.Secondary) || !HasSkill(skill.Id)) continue;
                ushort current = GetPureSkillValue(skill.Id);
                if (skill.CategoryID == SkillCategory.Profession) ++primaries;
                if (current < 300) maxed = false;
                if (current < GetPureMaxSkillValue((SkillType)skill.Id) && current < value) { lowest = (SkillType)skill.Id; value = current; }
            }
            if (maxed && primaries >= 2 && GetPureSkillValue(SkillType.Cooking) >= 300 && GetPureSkillValue(129) >= 300 && GetPureSkillValue(SkillType.Fishing) >= 300)
            {
                int[] essences = [7076, 7078, 7080, 7082];
                AddItem(essences[RandomHelper.IRand(0, essences.Length - 1)], Math.Max(1, essenceCount));
            }
            else if (lowest != 0) SetSkill(lowest, GetSkillStep(lowest), value + 1, GetPureMaxSkillValue(lowest));
        }

        public void UpdateClassicLegacyUnlock()
        {
            // every Legacy page asks for the trait config of the Legacy trees (C_Traits.GetConfigIDByTreeID); without it the window errors
            if (m_activePlayerData.TraitConfigs.FindIndexIf(config =>
                config.Type == (int)TraitConfigType.Generic && config.TraitSystemID == LegacyTraitSystemID) < 0)
                CastSpell(this, SpellCreateLegacyTraitConfig, true);

            // Only distinct challenges in this realm's verified client ruleset contribute account points.
            int earnedLegacyPoints = GetLegacyPoints();

            int renown = GetCurrencyQuantity(LegacyRenownCurrencyID);
            if (earnedLegacyPoints != renown)
                ModifyCurrency(LegacyRenownCurrencyID, earnedLegacyPoints - renown);

            if (earnedLegacyPoints == 0 && GetLevel() < LegacyUnlockLevel)
                return;

            // the reward track shows the progress towards the next renown level from the faction's reputation, so the client must know it
            FactionRecord legacyFaction = CliDB.FactionStorage.LookupByKey(LegacyRewardTrackFactionID);
            if (legacyFaction != null)
                GetReputationMgr().SetVisible(legacyFaction);
        }
    }
}
