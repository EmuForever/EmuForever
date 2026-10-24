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
using Game.Maps;
using Game.Scripting;

namespace Scripts.Kalimdor.RagefireChasm
{
    // Upstream intentionally has no encounter AI here. The instance script is
    // required so the dungeon finder can award completion credit.
    [Script]
    class instance_ragefire_chasm : InstanceMapScript
    {
        public instance_ragefire_chasm() : base(nameof(instance_ragefire_chasm), 389) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new InstanceScript(map);
    }
}
