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
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.Kalimdor.Maraudon
{
    [Script]
    class instance_maraudon : InstanceMapScript
    {
        public instance_maraudon() : base(nameof(instance_maraudon), 349) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new MaraudonInstance(map);

        class MaraudonInstance : InstanceScript
        {
            public MaraudonInstance(InstanceMap map) : base(map)
            {
                SetHeaders("Maraudon_v1");
                SetBossNumber(8);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0, 422), new(1, 423), new(2, 427), new(3, 424),
                    new(4, 425), new(5, 426), new(6, 428), new(7, 429)
                });
            }
            public override void OnUnitDeath(Unit unit)
            {
                switch (unit.GetEntry())
                {
                    case 12258: SetBossState(1, EncounterState.Done); break;
                    case 13601: SetBossState(2, EncounterState.Done); break;
                    case 12236: SetBossState(3, EncounterState.Done); break;
                    case 13596: SetBossState(6, EncounterState.Done); break;
                }
            }
        }
    }

    [Script]
    class celebras_the_cursed : BossAI
    {
        TimeSpan _wrath, _roots, _corrupt;
        public celebras_the_cursed(Creature creature) : base(creature, 4) => Initialize();
        void Initialize()
        {
            _wrath = TimeSpan.FromSeconds(8);
            _roots = TimeSpan.FromSeconds(2);
            _corrupt = TimeSpan.FromSeconds(30);
        }
        public override void Reset() { base.Reset(); Initialize(); }
        public override void JustDied(Unit killer)
        {
            base.JustDied(killer);
            me.SummonCreature(13716, new Position(me.GetPositionX(), me.GetPositionY(), me.GetPositionZ(), 0), TempSummonType.TimedDespawn, TimeSpan.FromMinutes(10));
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            if (_wrath <= diff)
            {
                Unit target = SelectTarget(SelectTargetMethod.Random, 0);
                if (target != null) DoCast(target, 21807);
                _wrath = TimeSpan.FromSeconds(8);
            }
            else _wrath -= diff;
            if (_roots <= diff) { DoCastVictim(12747); _roots = TimeSpan.FromSeconds(20); }
            else _roots -= diff;
            if (_corrupt <= diff)
            {
                me.InterruptNonMeleeSpells(false);
                DoCastSelf(21968);
                _corrupt = TimeSpan.FromSeconds(20);
            }
            else _corrupt -= diff;
        }
    }

    [Script]
    class boss_landslide : BossAI
    {
        TimeSpan _knockAway, _trample, _landslide;
        public boss_landslide(Creature creature) : base(creature, 5) => Initialize();
        void Initialize() { _knockAway = TimeSpan.FromSeconds(8); _trample = TimeSpan.FromSeconds(2); _landslide = TimeSpan.Zero; }
        public override void Reset() { base.Reset(); Initialize(); }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            if (_knockAway <= diff) { DoCastVictim(18670); _knockAway = TimeSpan.FromSeconds(15); }
            else _knockAway -= diff;
            if (_trample <= diff) { DoCastSelf(5568); _trample = TimeSpan.FromSeconds(8); }
            else _trample -= diff;
            if (HealthBelowPct(50))
            {
                if (_landslide <= diff)
                {
                    me.InterruptNonMeleeSpells(false);
                    DoCastSelf(21808);
                    _landslide = TimeSpan.FromSeconds(60);
                }
                else _landslide -= diff;
            }
        }
    }

    [Script]
    class boss_noxxion : BossAI
    {
        TimeSpan _volley, _uppercut, _adds, _invisibleTimer;
        bool _invisible;
        public boss_noxxion(Creature creature) : base(creature, 0) => Initialize();
        void Initialize()
        {
            _volley = TimeSpan.FromSeconds(7);
            _uppercut = TimeSpan.FromSeconds(16);
            _adds = TimeSpan.FromSeconds(19);
            _invisibleTimer = TimeSpan.FromSeconds(15);
            _invisible = false;
        }
        public override void Reset() { base.Reset(); Initialize(); }
        void SummonAdds(Unit victim)
        {
            // Trinity's irand includes both endpoints.
            Creature add = DoSpawnCreature(13456, Random.Shared.Next(-7, 8), Random.Shared.Next(-7, 8), 0, 0,
                TempSummonType.TimedOrCorpseDespawn, TimeSpan.FromSeconds(90));
            add?.GetAI().AttackStart(victim);
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (_invisible && _invisibleTimer <= diff)
            {
                me.SetFaction(14);
                me.SetUninteractible(false);
                me.SetDisplayId(11172);
                _invisible = false;
            }
            else if (_invisible) { _invisibleTimer -= diff; return; }
            if (!UpdateVictim()) return;
            if (_volley <= diff) { DoCastVictim(21687); _volley = TimeSpan.FromSeconds(9); }
            else _volley -= diff;
            if (_uppercut <= diff) { DoCastVictim(22916); _uppercut = TimeSpan.FromSeconds(12); }
            else _uppercut -= diff;
            if (!_invisible && _adds <= diff)
            {
                me.InterruptNonMeleeSpells(false);
                me.SetFaction(35);
                me.SetUninteractible(true);
                me.SetDisplayId(11686);
                for (int i = 0; i < 5; ++i) SummonAdds(me.GetVictim());
                _invisible = true;
                _invisibleTimer = TimeSpan.FromSeconds(15);
                _adds = TimeSpan.FromSeconds(40);
            }
            else _adds -= diff;
        }
    }

    [Script]
    class boss_princess_theradras : BossAI
    {
        TimeSpan _dust, _boulder, _thrash, _gaze;
        public boss_princess_theradras(Creature creature) : base(creature, 7) => Initialize();
        void Initialize()
        {
            _dust = TimeSpan.FromSeconds(8); _boulder = TimeSpan.FromSeconds(2);
            _thrash = TimeSpan.FromSeconds(5); _gaze = TimeSpan.FromSeconds(23);
        }
        public override void Reset() { base.Reset(); Initialize(); }
        public override void JustDied(Unit killer)
        {
            base.JustDied(killer);
            me.SummonCreature(12238, new Position(28.1887f, 62.3964f, -123.161f, 4.31096f), TempSummonType.TimedDespawn, TimeSpan.FromMinutes(10));
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            if (_dust <= diff) { DoCastSelf(21909); _dust = TimeSpan.FromSeconds(14); }
            else _dust -= diff;
            if (_boulder <= diff)
            {
                Unit target = SelectTarget(SelectTargetMethod.Random, 0);
                if (target != null) DoCast(target, 21832);
                _boulder = TimeSpan.FromSeconds(10);
            }
            else _boulder -= diff;
            if (_gaze <= diff) { DoCastVictim(21869); _gaze = TimeSpan.FromSeconds(20); }
            else _gaze -= diff;
            if (_thrash <= diff) { DoCastSelf(3391); _thrash = TimeSpan.FromSeconds(18); }
            else _thrash -= diff;
        }
    }
}
