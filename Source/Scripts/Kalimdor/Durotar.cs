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
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.Kalimdor.Durotar
{
    // 17009 - Voodoo
    [Script]
    class spell_voodoo : SpellScript
    {
        const int SpellBrew = 16712;
        const int SpellGhostly = 16713;
        const int SpellHex1 = 16707;
        const int SpellHex2 = 16708;
        const int SpellHex3 = 16709;
        const int SpellGrow = 16711;
        const int SpellLaunch = 16716;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellBrew, SpellGhostly, SpellHex1, SpellHex2, SpellHex3, SpellGrow, SpellLaunch);
        }

        void HandleDummy(int effIndex)
        {
            int spellId = SelectSpell(Random.Shared);
            Unit target = GetHitUnit();
            if (target != null)
                GetCaster().CastSpell(target, spellId, false);
        }

        internal static int SelectSpell(Random random)
        {
            // Hex is one of five equally likely outcomes; its three forms share that chance.
            int hex = random.Next(3) switch { 0 => SpellHex1, 1 => SpellHex2, _ => SpellHex3 };
            return random.Next(5) switch
            {
                0 => SpellBrew,
                1 => SpellGhostly,
                2 => hex,
                3 => SpellGrow,
                _ => SpellLaunch
            };
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }
}

