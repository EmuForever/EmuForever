// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Game.Spells
{
    // Classic 1.60 seal -> judgement (client SpellEffect: seal effect 2 is a dummy whose points are the judgement spell). 20154, the Forever
    // Seal of Righteousness rank 1 without that effect, unleashes Judgement of Righteousness rank 1 like 21084. (TC-forever SpellInfo.cpp)
    public static class ClassicSeals
    {
        static readonly Dictionary<int, int> SealJudgements = new()
        {
            { 20154, 20187 }, { 21084, 20187 },                                             // Seal of Righteousness
            { 20287, 20280 }, { 20288, 20281 }, { 20289, 20282 }, { 20290, 20283 }, { 20291, 20284 }, { 20292, 20285 }, { 20293, 20286 },
            { 21082, 21183 }, { 20162, 20188 }, { 20305, 20300 }, { 20306, 20301 }, { 20307, 20302 }, { 20308, 20303 }, // Seal of the Crusader
            { 20164, 20184 },                                                               // Seal of Justice
            { 20165, 20185 }, { 20347, 20344 }, { 20348, 20345 }, { 20349, 20346 },         // Seal of Light
            { 20166, 20186 }, { 20356, 20354 }, { 20357, 20355 },                           // Seal of Wisdom
            { 20375, 20425 }, { 20915, 20962 }, { 20918, 20961 }, { 20919, 20967 }, { 20920, 20968 }, // Seal of Command
            { 407798, 407803 },                                                             // Seal of Martyrdom
            { 1311649, 1311650 }, { 1311656, 1311655 }, { 20163, 20183 }, { 20419, 20411 }, // Seal of Fury (Forever, effect 2 names the judgement)
            { 20421, 20412 }, { 20422, 20413 }, { 20423, 20414 },
        };

        public static bool IsSeal(int spellId) { return SealJudgements.ContainsKey(spellId); }

        public static int GetJudgement(int sealSpellId) { return SealJudgements.TryGetValue(sealSpellId, out int judgement) ? judgement : 0; }
    }
}
