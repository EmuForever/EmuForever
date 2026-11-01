using Framework.Constants;
using Game.Spells;
using System.Collections.Generic;

namespace Game.Entities
{
    // Classic 1.60 (WoW Forever) camping, ported from the reference EmuForever (classic_go_scripts.cpp, MiscHandler.cpp).
    // Sitting within reach of a campfire (spell focus 4) starts a 60 s Campfire Rest. When it runs out the player gets the hidden
    // "Boosted ..." buff of every camp feature placed near that fire, scaled by level, plus the visible Camp Benefits summary,
    // and, still sitting, the next rest starts. Standing up ends the rest, so the minute has to be sat out in one go.
    // The stand-state hook covers every way of sitting (the client's /sit, chairs, eating, SI bots), not only the client packet.
    public partial class Player
    {
        public const int SpellCampfireRest = 1289723;
        const int SpellBoostedRest = 1229451;           // Camp Tent
        const int SpellBoostedCrit = 1229519;           // Camp Chair
        const int SpellBoostedAttackPower = 1230164;    // Lodestone
        const int SpellBoostedManaRegen = 1230587;      // Mana Well
        const int SpellExtraStrength = 1230172;         // Sharpening Wheel
        const int SpellBoostedIntellect = 1229513;      // Incense Candle
        const int SpellBoostedSpirit = 1229718;         // Faction Banner
        const int SpellBoostedStamina = 1230124;        // First Aid Kit
        const int SpellBoostedStatsPct = 1230098;       // Fish Bowl
        const int SpellBoostedStatsLute = 1230653;      // Enchanted Lute: armor, all stats, all resistances
        const int SpellCampBenefits = 1229741;          // visible summary ("Gained the following camp benefits: ...")
        const int SpellFocusCampfire = 4;
        public const float CampfireRange = 15.0f;       // sitting this close to the fire
        const float CampFeatureRange = 20.0f;           // features this close to the fire count

        // Camp features and their buff. The features are the objects summoned by the placement spells (1307229 Camp Chair, ...);
        // the camping trainer's camp has its tent as a creature.
        static readonly (int Entry, bool IsCreature, int Buff)[] CampFeatures =
        [
            (263398, true,  SpellBoostedRest),          // Camp Tent (creature, camping trainer's camp)
            (528996, false, SpellBoostedRest),          // Camp Tent
            (612275, false, SpellBoostedCrit),          // Camp Chair
            (651956, false, SpellBoostedAttackPower),   // Lodestone
            (651948, false, SpellBoostedManaRegen),     // Mana Well
            (651950, false, SpellExtraStrength),        // Sharpening Wheel
            (651955, false, SpellBoostedIntellect),     // Incense Candle
            (612350, false, SpellBoostedSpirit),        // Faction Banner
            (612351, false, SpellBoostedSpirit),        // Faction Banner
            (651953, false, SpellBoostedStamina),       // First Aid Kit
            (651954, false, SpellBoostedStatsPct),      // Fish Bowl
            (651952, false, SpellBoostedStatsLute),     // Enchanted Lute
        ];

        // The campfire within resting reach, or null.
        public GameObject FindCampfire()
        {
            GameObject fire = FindNearestGameObjectOfType(GameObjectTypes.SpellFocus, CampfireRange);
            return fire != null && fire.GetGoInfo().GetSpellFocusType() == SpellFocusCampfire ? fire : null;
        }

        // Called on every stand-state change.
        internal void UpdateCampfireRest()
        {
            if (!IsInWorld || !IsAlive())
                return;
            if (!IsSitState())
                RemoveAurasDueToSpell(SpellCampfireRest);
            else if (!HasAura(SpellCampfireRest) && FindCampfire() != null)
                CastSpell(this, SpellCampfireRest, true);
        }

        // Campfire Rest ran its full minute: grant the benefits of the features around the fire and, still sitting, rest again.
        public void CompleteCampfireRest()
        {
            GameObject fire = FindCampfire();
            if (fire == null)
                return;

            SortedSet<int> buffs = new();
            foreach (var (entry, isCreature, buff) in CampFeatures)
            {
                bool nearFire = isCreature ? fire.FindNearestCreature(entry, CampFeatureRange) != null
                    : fire.FindNearestGameObject(entry, CampFeatureRange) != null;
                if (nearFire && Global.SpellMgr.GetSpellInfo(buff, Difficulty.None) != null)
                    buffs.Add(buff);
            }
            foreach (int buff in buffs)
                CastCampBuff(buff);
            // The Boosted buffs are hidden auras; the visible buff is Camp Benefits, whose tooltip lists them (official beta sniff:
            // both arrive in the same aura update). Its other effects are area dummies, so only the aura is added.
            if (buffs.Count != 0 && Global.SpellMgr.GetSpellInfo(SpellCampBenefits, Difficulty.None) != null)
                AddAura(SpellCampBenefits, this);

            if (IsSitState())
                CastSpell(this, SpellCampfireRest, true);
        }

        // The buffs scale with level in steps (client tooltips: "$?$PL<12[3]?$PL<24[8]...[$1230124s1]"); the spell's own value is the
        // highest step, so -1 keeps it.
        static int CampTier(int level, params (int BelowLevel, int Value)[] tiers)
        {
            foreach (var (belowLevel, value) in tiers)
                if (level < belowLevel)
                    return value;
            return -1;
        }

        void CastCampBuff(int buff)
        {
            int level = GetLevel();
            CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
            void Set(int effect, int value) { if (value >= 0) args.AddSpellMod(SpellValueMod.BasePoint0 + effect, value); }

            switch (buff)
            {
                case SpellBoostedStamina:
                    Set(0, CampTier(level, (12, 3), (24, 8), (36, 21), (48, 34), (60, 45)));
                    break;
                case SpellBoostedAttackPower:
                    Set(0, CampTier(level, (12, 12), (22, 20), (32, 32), (42, 49), (52, 67)));
                    break;
                case SpellBoostedManaRegen:
                    Set(0, CampTier(level, (24, 10), (34, 15), (44, 20), (54, 24)));
                    break;
                case SpellExtraStrength:
                    Set(0, CampTier(level, (24, 6), (38, 11), (52, 20)));
                    break;
                case SpellBoostedIntellect:
                    Set(0, CampTier(level, (14, 2), (28, 6), (42, 12), (56, 18)));
                    break;
                case SpellBoostedSpirit:
                    Set(0, CampTier(level, (40, 14), (50, 19), (60, 27)));
                    break;
                case SpellBoostedStatsLute:
                {
                    Set(0, CampTier(level, (10, 28), (20, 71), (30, 114), (40, 163), (50, 211), (60, 260)));
                    Set(1, CampTier(level, (10, 0), (20, 2), (30, 4), (40, 7), (50, 9), (60, 12)));
                    int resistance = CampTier(level, (30, 0), (40, 6), (50, 12), (60, 16));
                    for (int effect = 2; effect <= 7; ++effect)
                        Set(effect, resistance);
                    break;
                }
                default:    // Boosted Rest, Critical Chance and Stats (%) don't scale
                    break;
            }
            CastSpell(this, buff, args);
        }

        // Boosted Rest, the Camp Tent's benefit: "increase Rested experience to <value>% of a level. No effect if Rested experience
        // already exceeds that value." (client spell text; effect 0 is a dummy with value 5)
        public void ApplyBoostedRest(int percentOfLevel)
        {
            float wanted = m_activePlayerData.NextLevelXP * percentOfLevel / 100.0f;
            if (_restMgr.GetRestBonus(RestTypes.XP) < wanted)
                _restMgr.SetRestBonus(RestTypes.XP, wanted);
        }
    }
}
