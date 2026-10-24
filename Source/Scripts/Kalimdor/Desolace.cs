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

namespace Scripts.Kalimdor.Desolace
{
    [Script]
    class npc_aged_dying_ancient_kodo : ScriptedAI
    {
        public npc_aged_dying_ancient_kodo(Creature creature) : base(creature) { }

        public override void MoveInLineOfSight(Unit who)
        {
            if (who.GetEntry() == 11596 && me.IsWithinDistInMap(who, 10.0f) && !me.HasAura(18362))
            {
                me.GetMotionMaster().Clear();
                DoCast(me, 18362, true);
                who.ToCreature()?.GetAI().Talk(0);
            }
        }

        public override void SpellHit(WorldObject caster, SpellInfo spellInfo)
        {
            Unit unitCaster = caster.ToUnit();
            if (unitCaster == null)
                return;
            if (spellInfo.Id == 18153)
            {
                if (!unitCaster.HasAura(18172) && !me.HasAura(18377) &&
                    (me.GetEntry() == 4700 || me.GetEntry() == 4701 || me.GetEntry() == 4702))
                {
                    unitCaster.CastSpell(unitCaster, 18172, true);
                    DoCast(me, 18377, true);
                    me.UpdateEntry(11627);
                    me.CombatStop();
                    me.SetFaction(35);
                    me.SetSpeedRate(UnitMoveType.Run, 0.6f);
                    EngagementOver();
                    me.GetMotionMaster().MoveFollow(unitCaster, SharedConst.PetFollowDist, me.GetFollowAngle());
                    me.SetActive(true);
                    me.RemoveNpcFlag(NPCFlags1.Gossip);
                }
            }
            else if (spellInfo.Id == 18362)
            {
                me.SetNpcFlag(NPCFlags1.Gossip);
                me.SetHomePosition(me.GetPosition());
                me.GetMotionMaster().Clear();
                me.GetMotionMaster().MoveIdle();
                me.SetActive(false);
                me.DespawnOrUnsummon(TimeSpan.FromSeconds(60));
            }
        }

        public override bool OnGossipHello(Player player)
        {
            if (player.HasAura(18172) && me.HasAura(18377))
            {
                player.TalkedToCreature(me.GetEntry(), ObjectGuid.Empty);
                player.RemoveAurasDueToSpell(18172);
            }
            player.SendGossipMenu(player.GetGossipTextId(me), me.GetGUID());
            return true;
        }
    }
}
