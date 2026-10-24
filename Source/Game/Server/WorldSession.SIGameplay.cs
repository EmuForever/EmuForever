using Framework.Constants;
using Game.Entities;
using Game.Networking.Packets;
using Game.Spells;
using System.Linq;

namespace Game
{
    public partial class WorldSession
    {
        internal void AcceptPartyInviteForSI()
        {
            if (!IsServerControlled || _player?.GetGroupInvite() is not { } group ||
                group.GetLeaderGUID() == _player.GetGUID()) return;
            HandlePartyInviteResponse(new PartyInviteResponse(null) { Accept = true, PartyIndex = group.GetGroupCategory() });
        }

        internal bool PetAttackForSI(Unit target)
        {
            Pet pet = _player?.GetPet();
            if (!IsServerControlled || pet == null || !pet.IsAlive() || !pet.IsAIEnabled() || target == null ||
                pet.GetOwnerGUID() != _player.GetGUID() || !_player.IsValidAttackTarget(target)) return false;
            ConfigureSIPetAutocasts(pet, pet.GetMap().GetDifficultyID());
            HandlePetActionHelper(pet, pet.GetGUID(), (int)CommandStates.Attack, ActiveStates.Command, target.GetGUID(), 0, 0, 0);
            return pet.GetCharmInfo()?.IsCommandAttack() == true && pet.GetVictim() == target;
        }

        internal static void ConfigureSIPetAutocasts(Pet pet, Difficulty difficulty)
        {
            // New pet spells default to autocast disabled. SI owners have no client
            // pet bar to enable them; let PetAI choose valid casts and honor costs/cooldowns.
            foreach (var learned in pet.m_spells)
            {
                if (learned.Value.UpdateState == PetSpellState.Removed) continue;
                SpellInfo ability = Global.SpellMgr.GetSpellInfo(learned.Key, difficulty);
                if (ability?.IsAutocastable() != true) continue;
                pet.ToggleAutocast(ability, true);
                pet.GetCharmInfo().SetSpellAutocast(ability, true);
            }
        }

        internal void PetFollowForSI(ObjectGuid previousTarget)
        {
            Pet pet = _player?.GetPet();
            if (!IsServerControlled || pet?.IsAlive() != true || pet.GetOwnerGUID() != _player.GetGUID() ||
                pet.GetVictim()?.GetGUID() != previousTarget) return;
            HandlePetActionHelper(pet, pet.GetGUID(), (int)CommandStates.Follow, ActiveStates.Command, ObjectGuid.Empty, 0, 0, 0);
        }

        // Same request path as CMSG_USE_ITEM: ProcessItemCast validates the item (CanUseItem, combat, binding)
        // and the cast spends charges/stacks and applies the normal cooldown.
        internal bool UseItemForSI(Item item, int spellId, WorldObject target = null)
        {
            if (!IsServerControlled || item == null || _player == null) return false;
            SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(spellId, _player.GetMap().GetDifficultyID());
            if (spellInfo == null || !_player.CanRequestSpellCast(spellInfo, _player)) return false;
            var request = new SpellCastRequestPkt
            {
                SpellID = spellId,
                CastID = ObjectGuid.Create(HighGuid.Cast, SpellCastSource.Normal, _player.GetMapId(), spellId,
                    _player.GetMap().GenerateLowGuid(HighGuid.Cast))
            };
            request.Target = BuildSIItemTargets(spellInfo.GetExplicitTargetMask(), _player, target);
            _player.RequestSpellCast(new SpellCastRequest(request, _player.GetGUID(),
                new SpellCastRequestItemData(item.InventoryBagSlot, item.InventorySlot, item.GetGUID())));
            return true;
        }

        internal static SpellTargetData BuildSIItemTargets(SpellCastTargetFlags mask, WorldObject caster, WorldObject target)
        {
            var data = new SpellTargetData { Flags = target is GameObject ? SpellCastTargetFlags.Gameobject : SpellCastTargetFlags.Unit,
                Unit = (target ?? caster).GetGUID() };
            if ((mask & SpellCastTargetFlags.DestLocation) != 0)
            {
                WorldObject location = target ?? caster;
                data.Flags |= SpellCastTargetFlags.DestLocation;
                data.DstLocation = new TargetLocation { Transport = ObjectGuid.Empty,
                    Location = new(location.GetPositionX(), location.GetPositionY(), location.GetPositionZ()) };
            }
            if ((mask & SpellCastTargetFlags.SourceLocation) != 0)
            {
                data.Flags |= SpellCastTargetFlags.SourceLocation;
                data.SrcLocation = new TargetLocation { Transport = ObjectGuid.Empty,
                    Location = new(caster.GetPositionX(), caster.GetPositionY(), caster.GetPositionZ()) };
            }
            return data;
        }

        // Same validation as CMSG_SELL_ITEM (vendor interaction range, sellable, buyback).
        internal bool SellItemForSI(Creature vendor, Item item)
        {
            if (!IsServerControlled || vendor == null || item == null) return false;
            ObjectGuid guid = item.GetGUID();
            long money = _player.GetMoney();
            HandleSellItem(new SellItem(null) { VendorGUID = vendor.GetGUID(), ItemGUID = item.GetGUID(), Amount = item.GetCount() });
            bool inInventory = false;
            _player.ForEachItem(ItemSearchLocation.Inventory, candidate =>
            {
                if (candidate.GetGUID() != guid) return true;
                inInventory = true; return false;
            });
            return !inInventory && _player.GetMoney() > money;
        }

        internal bool AcceptQuestForSI(WorldObject giver, Quest quest, out string failure)
        {
            failure = null;
            if (!IsServerControlled || giver == null || quest == null) { failure = "Invalid quest interaction"; return false; }
            if (!_player.CanInteractWithQuestGiver(giver)) { failure = DescribeSIInteractionFailure(giver); return false; }
            if (!giver.HasQuest(quest.Id)) { failure = "NPC no longer offers this quest"; return false; }
            if (_player is siPlayer sourcePlayer && quest.SourceItemId > 0 && _player.FindQuestSlot(0) < SharedConst.MaxQuestLogSize && _player.CanTakeQuest(quest, false))
                sourcePlayer.Controller.EnsureQuestItemSpace(quest.SourceItemId, System.Math.Max(1, quest.SourceItemIdCount), true);
            if (!_player.CanTakeQuest(quest, false) || !_player.CanAddQuest(quest, false)) { failure = "Quest prerequisites or inventory/log space rejected acceptance"; return false; }
            HandleQuestgiverAcceptQuest(new QuestGiverAcceptQuest(null) { QuestGiverGUID = giver.GetGUID(), QuestID = quest.Id });
            if (_player.GetQuestStatus(quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete) return true;
            failure = "Quest acceptance produced no quest-log entry";
            return false;
        }

        internal bool RewardQuestForSI(WorldObject giver, Quest quest, LootItemType type, int reward, out string failure)
        {
            failure = null;
            if (!IsServerControlled || giver == null || quest == null) { failure = "Invalid reward interaction"; return false; }
            if (!_player.CanInteractWithQuestGiver(giver)) { failure = DescribeSIInteractionFailure(giver); return false; }
            HandleQuestgiverRequestReward(new QuestGiverRequestReward(null) { QuestGiverGUID = giver.GetGUID(), QuestID = quest.Id });
            if (!_player.CanRewardQuest(quest, type, reward, false)) { failure = "Quest objectives, required items, or reward inventory space rejected turn-in"; return false; }
            HandleQuestgiverChooseReward(new QuestGiverChooseReward(null) { QuestGiverGUID = giver.GetGUID(), QuestID = quest.Id,
                Choice = new QuestChoiceItem { LootItemType = type, Item = new ItemInstance { ItemID = reward }, Quantity = 1 } });
            if (_player.GetQuestRewardStatus(quest.Id)) return true;
            failure = "Reward request did not mark the quest rewarded";
            return false;
        }

        internal string DescribeSIInteractionFailure(WorldObject giver) =>
            $"Core rejected interaction with {giver.GetName()}: distance {_player.GetExactDist(giver):F1}, " +
            $"alive {_player.IsAlive()}, flight {_player.IsInFlight()}, in combat {giver.ToUnit()?.IsInCombat()}, " +
            $"reaction {giver.ToUnit()?.GetReactionTo(_player)}, NPC flags {giver.ToCreature()?.GetCreatureTemplate().Npcflag}";

        // CMSG_AREA_TRIGGER as the client sends it on entering a trigger (radius checked by the handler).
        internal void AreaTriggerForSI(int triggerId)
        {
            if (IsServerControlled) HandleAreaTrigger(new AreaTriggerPkt(null) { AreaTriggerID = triggerId, Entered = true, FromClient = true });
        }

        // CMSG_QUEST_LOG_REMOVE_QUEST (abandon): source items, timers and quest items handled as for a client.
        internal void AbandonQuestForSI(ushort slot)
        {
            if (IsServerControlled && slot < SharedConst.MaxQuestLogSize) HandleQuestLogRemoveQuest(new QuestLogRemoveQuest(null) { Entry = (byte)slot });
        }

        // CMSG_AUTO_EQUIP_ITEM from the item's current bag position.
        internal bool EquipForSI(Item item)
        {
            if (!IsServerControlled || item == null || !item.InventoryPosition.IsInventoryPos) return false;
            ObjectGuid guid = item.GetGUID();
            var packet = new AutoEquipItem(null) { Inv = new InvUpdate { Items = new() { item.InventoryPosition } },
                Slot = item.InventorySlot, PackSlot = item.InventoryBagSlot };
            HandleAutoEquipItem(packet);
            Item moved = _player.GetItemByGuid(guid);
            return moved != null && !moved.InventoryPosition.IsInventoryPos;
        }

        // CMSG_BUY_ITEM by vendor list position; the core checks price, reputation, stock and bag space.
        internal bool BuyItemForSI(Creature vendor, int itemId, byte count)
        {
            if (!IsServerControlled || vendor == null) return false;
            VendorItemData items = Global.ObjectMgr.GetNpcVendorItemList(vendor.GetEntry());
            for (int slot = 0; items != null && slot < items.GetItemCount(); ++slot)
            {
                VendorItem entry = items.GetItem(slot);
                if (entry == null || entry.item != itemId || entry.Type != ItemVendorType.Item) continue;
                long money = _player.GetMoney();
                int before = _player.GetItemCount(itemId);
                _player.BuyItemFromVendorSlot(vendor.GetGUID(), slot, itemId, count, ItemPos.Undefined);
                return _player.GetItemCount(itemId) > before;
            }
            return false;
        }

        // CMSG_REPAIR_ITEM (all) with the reputation discount.
        internal bool RepairForSI(Creature vendor)
        {
            if (!IsServerControlled || vendor == null) return false;
            Creature npc = _player.GetNPCIfCanInteractWith(vendor.GetGUID(), NPCFlags1.Repair, NPCFlags2.None);
            if (npc == null) return false;
            long money = _player.GetMoney();
            Item weapon = _player.GetItemByPos(EquipmentSlot.MainHand);
            bool brokenWeapon = weapon?.IsBroken() == true;
            if (brokenWeapon)
                _player.DurabilityRepair(weapon.InventoryPosition, true, _player.GetReputationPriceDiscount(npc));
            _player.DurabilityRepairAll(true, _player.GetReputationPriceDiscount(npc), false);
            return _player.GetMoney() < money || brokenWeapon && !weapon.IsBroken();
        }

        // CMSG_TRAINER_BUY_SPELL for every spell the trainer can teach now and the player can afford.
        internal int TrainForSI(Creature trainer)
        {
            if (!IsServerControlled || trainer == null) return 0;
            Creature npc = _player.GetNPCIfCanInteractWith(trainer.GetGUID(), NPCFlags1.Trainer, NPCFlags2.None);
            if (npc == null) return 0;
            int learned = 0;
            foreach (Trainer data in Game.AI.SITrainerCatalog.ForCreature(npc.GetEntry(), _player.GetClass()))
            foreach (TrainerSpell spell in data.GetTeachableSpells(_player))
            {
                if (!_player.HasEnoughMoney((long)(spell.MoneyCost * _player.GetReputationPriceDiscount(npc)))) continue;
                int known = _player.GetSpellMap().Count;
                data.TeachSpell(npc, _player, spell.SpellId);
                if (_player.GetSpellMap().Count != known || _player.HasSpell(spell.SpellId)) ++learned;
            }
            return learned;
        }

        internal void UseObjectForSI(GameObject obj)
        {
            if (IsServerControlled) HandleGameObjectUse(new GameObjUse(null) { Guid = obj.GetGUID() });
        }
        internal void TalkForSI(Creature creature)
        {
            if (!IsServerControlled) return;
            HandleGossipHello(new Hello(null) { Unit = creature.GetGUID() });
        }
        internal void LootForSI(Creature creature)
        {
            if (!IsServerControlled) return;
            _player.SetSelection(creature.GetGUID());
            if (!_player.HasUnitState(UnitState.CannotTurn)) _player.SetFacingToObject(creature);
            HandleLoot(new LootUnit(null) { Unit = creature.GetGUID() });
            // The controller leaves the native loot view open for an animation tick
            // before transferring/releasing it, so nearby clients see UnitFlags.Looting.
        }
        internal void LootOpenViewForSI()
        {
            if (!IsServerControlled || !_player.IsAlive() || _player.IsInCombat()) return;
            var views = _player.GetAELootView().Values.ToArray();
            foreach (var loot in views)
            {
                var slots = new System.Collections.Generic.List<int>();
                for (int slot = 0; slot < loot.items.Count && slot <= byte.MaxValue; slot++)
                {
                    var item = loot.LootItemInSlot(slot, _player);
                    if (item != null && !item.is_looted && !item.is_blocked && item.HasAllowedLooter(_player.GetGUID()))
                        slots.Add(slot);
                }
                // Required quest items get first use of space, before ordinary loot can
                // fill the slot that was just cleared for them.
                foreach (int slot in slots.OrderBy(slot => _player is siPlayer bot &&
                    loot.LootItemInSlot(slot, _player) is { } candidate && bot.Controller.NeedsQuestLoot(candidate.itemid) ? 0 : 1))
                {
                    var item = loot.LootItemInSlot(slot, _player);
                    if (item == null || item.is_looted) continue;
                    if (_player is siPlayer bot && bot.Controller.NeedsQuestLoot(item.itemid) &&
                        !bot.Controller.EnsureQuestItemSpace(item.itemid, item.count)) continue;
                    int entry = item.itemid, before = _player.GetItemCount(entry);
                    var packet = new LootItemPkt(null);
                    packet.Loot.Add(new LootRequest { Object = loot.GetGUID(), LootListID = (byte)slot });
                    HandleAutostoreLootItem(packet);
                    int gained = _player.GetItemCount(entry) - before;
                    if (gained > 0 && _player is siPlayer si) si.Controller.ConfirmLoot(entry, gained, loot.GetOwnerGUID());
                }
            }
            long money = _player.GetMoney();
            if (views.Length != 0) HandleLootMoney(new LootMoney(null));
            if (_player.GetMoney() > money && _player is siPlayer player)
                player.Controller.ConfirmMoney(_player.GetMoney() - money);
            foreach (var loot in views) DoLootRelease(loot);
        }

        internal bool DestroyJunkForSI(Item item)
        {
            if (!IsServerControlled || item == null || _player.GetItemByPos(item.InventoryPosition) != item ||
                !item.InventoryPosition.IsInventoryPos || item.IsBag() || item.IsInTrade() ||
                item.GetQuality() != ItemQuality.Poor || item.GetTemplate().HasFlag(ItemFlags.NoUserDestroy)) return false;
            ObjectGuid guid = item.GetGUID();
            HandleDestroyItem(new DestroyItem(null) { ContainerId = item.InventoryBagSlot, SlotNum = item.InventorySlot, Count = 0 });
            return _player.GetItemByGuid(guid) == null;
        }
    }
}

