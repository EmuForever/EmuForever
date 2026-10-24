/*
 * This file is part of the TrinityCore Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the
 * Free Software Foundation; either version 2 of the License, or (at your
 * option) any later version.
 *
 * This program is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
 * more details.
 *
 * You should have received a copy of the GNU General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 */

// C# port of TrinityCore wotlk_classic.
using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using static Scripts.Kalimdor.Durotar.DurotarData;

namespace Scripts.Kalimdor.Durotar
{
    [Script]
    class npc_lazy_peon : ScriptedAI
    {
        TimeSpan _rebuffTimer;
        bool _work;
        public npc_lazy_peon(Creature creature) : base(creature) { }
        public override void Reset() { _rebuffTimer = TimeSpan.Zero; _work = false; }
        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (id == 1) _work = true;
        }
        public override void SpellHit(WorldObject caster, SpellInfo spell)
        {
            if (spell.Id != SPELL_AWAKEN_PEON) return;
            Player player = caster.ToPlayer();
            if (player == null || player.GetQuestStatus(QUEST_LAZY_PEONS) != QuestStatus.Incomplete) return;
            player.KilledMonsterCredit(me.GetEntry(), me.GetGUID());
            Talk(SAY_SPELL_HIT, caster);
            me.RemoveAllAuras();
            GameObject lumber = me.FindNearestGameObject(GO_LUMBERPILE, 20);
            if (lumber != null)
                me.GetMotionMaster().MovePoint(1, lumber.GetPositionX() - 1, lumber.GetPositionY(), lumber.GetPositionZ());
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (_work) me.HandleEmoteCommand(Emote.OneshotWorkChopwood);
            if (_rebuffTimer <= diff)
            {
                DoCastSelf(SPELL_BUFF_SLEEP);
                _rebuffTimer = TimeSpan.FromSeconds(25);
            }
            else _rebuffTimer -= diff;
            UpdateVictim();
        }
    }

    [Script]
    class npc_mithaka : ScriptedAI
    {
        bool _shipInPort;
        public npc_mithaka(Creature creature) : base(creature) { }
        public override void SetData(int type, int data) => _shipInPort = data == DATA_SHIP_DOCKED;
        public override bool OnGossipHello(Player player)
        {
            player.InitGossipMenu(GOSSIP_MENU_MITHAKA);
            if (!_shipInPort)
                player.AddGossipItem(GOSSIP_MENU_MITHAKA, 0, eTradeskill.GossipSenderMain, eTradeskill.GossipActionInfoDef + 1);
            player.SendGossipMenu(GOSSIP_TEXT_MITHAKA, me.GetGUID());
            return true;
        }
    }

    [Script]
    class spell_durotar_summon_zuni : SpellScript
    {
        void SetDest(ref SpellDestination dest) => dest.Relocate(new Position(-1173.4531f, -5266.401f, 0.85905945f, 0));
        public override void Register() => OnDestinationTargetSelect.Add(new(SetDest, 0, Targets.DestNearbyEntry));
    }

    [Script]
    class npc_durotar_tiki_target : ScriptedAI
    {
        bool _credited;
        static readonly int[] Masks = { SPELL_TIKI_TARGET_VISUAL_1, SPELL_TIKI_TARGET_VISUAL_2, SPELL_TIKI_TARGET_VISUAL_3 };
        public npc_durotar_tiki_target(Creature creature) : base(creature) { }
        public override void JustAppeared()
        {
            me.SetReactState(ReactStates.Passive);
            DoCastSelf(Masks[Random.Shared.Next(Masks.Length)]);
            DoCastSelf(SPELL_ARCANE_MISSILES_TRAINER);
        }
        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (me.GetHealth() > damage) return;
            damage = 0;
            me.SetHealth(1);
            if (_credited) return;
            _credited = true;
            DoCastSelf(SPELL_TIKI_TARGET_DEATH);
            attacker.ToPlayer()?.KilledMonsterCredit(me.GetEntry());
            me.DespawnOrUnsummon(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(13));
        }
    }
}
