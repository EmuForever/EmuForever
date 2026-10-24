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
using System.Numerics;

namespace Scripts.Kalimdor.Mulgore
{
    [Script]
    class npc_eagle_spirit : ScriptedAI
    {
        static readonly Vector3[] FlightPath =
        {
            new(-2884.155f, -71.08681f, 242.0678f),
            new(-2720.592f, -111.0035f, 242.5955f),
            new(-2683.951f, -382.9010f, 231.1792f),
            new(-2619.148f, -484.9288f, 231.1792f),
            new(-2543.868f, -525.3333f, 231.1792f),
            new(-2465.321f, -502.4896f, 190.7347f),
            new(-2343.872f, -401.8281f, -8.320873f)
        };
        public npc_eagle_spirit(Creature creature) : base(creature) { }
        public override void PassengerBoarded(Unit who, sbyte seatId, bool apply)
        {
            if (!apply) return;
            // The C# core exposes exact flying splines through MoveSmoothPath.
            me.GetMotionMaster().MoveSmoothPath(0, FlightPath, FlightPath.Length, fly: true);
            me.CastSpell(me, 69324, false);
        }
        public override void WaypointPathEnded(int pointId, int pathId) => DoCast(50630);
        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type == MovementGeneratorType.Effect && id == 0) DoCast(50630);
        }
    }
}
