using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Comprehensive level 60 Paladin routine for WoW Forever.
    // Covers common tools from Retribution, Protection and Holy.
    // Higher priority number = higher priority.
    public sealed class PaladinRoutine : SICombatRoutine
    {
        public override Class Class => Class.Paladin;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;

        static readonly SICombatRule[] Rotation =
        {
            // ──────────────────────────────────────────────
            // 1. PULL / OPENER
            // ──────────────────────────────────────────────
            Enemy("pull", 95, SICombatCondition.Pull,
                "judgement", "judgment",
                "holy strike",
                "exorcism",
                "hammer of justice"),

            // ──────────────────────────────────────────────
            // 2. EMERGENCY SURVIVAL (highest priority)
            // ──────────────────────────────────────────────
            Self("lay on hands", 130, SICombatCondition.CriticalHealth,
                "lay on hands"),

            Self("divine shield", 125, SICombatCondition.Combat | SICombatCondition.CriticalHealth,
                "divine shield"),

            Self("divine protection", 122, SICombatCondition.Combat | SICombatCondition.CriticalHealth,
                "divine protection"),

            Self("emergency heal", 118, SICombatCondition.CriticalHealth,
                "flash of light", "holy light", "holy shock"),

            // ──────────────────────────────────────────────
            // 3. CONTROL & INTERRUPTS
            // ──────────────────────────────────────────────
            Enemy("hammer of justice", 115, SICombatCondition.CastingEnemy,
                "hammer of justice"),

            Enemy("repentance", 112, SICombatCondition.None,          // talent
                "repentance"),

            // ──────────────────────────────────────────────
            // 4. MOBILITY & CLEANSING
            // ──────────────────────────────────────────────
            Self("freedom", 110, SICombatCondition.Rooted,
                "hand of freedom", "blessing of freedom"),

            Self("cleanse", 108, SICombatCondition.SelfDebuff,
                "cleanse", "purify"),

            // ──────────────────────────────────────────────
            // 5. SELF HEALS (non-critical)
            // ──────────────────────────────────────────────
            Self("healing", 100, SICombatCondition.LowHealth,
                "flash of light", "holy light", "holy shock"),

            // ──────────────────────────────────────────────
            // 6. MAINTAIN SEALS (very important in Forever)
            // ──────────────────────────────────────────────
            // One intent per exclusive family. Separate rules made upkeep cast
            // every learned member, continually replacing (or stacking) seals.
            Buff("seal", 92,
                "seal of command", "seal of fury", "seal of righteousness",
                "seal of the crusader", "seal of light", "seal of wisdom", "seal of justice"),

            // ──────────────────────────────────────────────
            // 7. MAINTAIN AURAS
            // ──────────────────────────────────────────────
            Buff("aura", 84,
                "retribution aura", "devotion aura", "concentration aura",
                "shadow resistance aura", "frost resistance aura", "fire resistance aura"),

            // ──────────────────────────────────────────────
            // 8. MAINTAIN BLESSINGS
            // ──────────────────────────────────────────────
            Buff("blessing", 78,
                "blessing of might", "blessing of kings", "blessing of wisdom", "blessing of sanctuary"),

            // ──────────────────────────────────────────────
            // 9. TANK TOOLS (Protection)
            // ──────────────────────────────────────────────
            Self("righteous fury", 74, SICombatCondition.None,
                "righteous fury"),

            Self("holy shield", 73, SICombatCondition.Combat,       // Prot talent
                "holy shield"),

            // ──────────────────────────────────────────────
            // 10. EXECUTE
            // ──────────────────────────────────────────────
            Enemy("hammer of wrath", 72, SICombatCondition.Execute,
                "hammer of wrath"),

            // ──────────────────────────────────────────────
            // 11. CORE DAMAGE / THREAT BUTTONS
            // ──────────────────────────────────────────────
            Enemy("judgement", 68, SICombatCondition.None,
                "judgement", "judgment",
                "judgement of command", "judgment of command",
                "judgement of righteousness", "judgment of righteousness",
                "judgement of light", "judgment of light",
                "judgement of wisdom", "judgment of wisdom",
                "judgement of justice", "judgment of justice",
                "judgement of the crusader", "judgment of the crusader"),

            Enemy("holy strike", 65, SICombatCondition.Melee,
                "holy strike"),

            // ──────────────────────────────────────────────
            // 12. HOLY DAMAGE / HEALING BUTTONS
            // ──────────────────────────────────────────────
            Enemy("holy shock", 60, SICombatCondition.None,         // Holy talent (damage version)
                "holy shock"),

            Enemy("exorcism", 55, SICombatCondition.None,           // Undead / Demons
                "exorcism"),

            Enemy("holy wrath", 52, SICombatCondition.None,
                "holy wrath"),

            // ──────────────────────────────────────────────
            // 13. AOE / MULTI-TARGET
            // ──────────────────────────────────────────────
            Enemy("consecration", 48, SICombatCondition.None,
                "consecration"),

            // ──────────────────────────────────────────────
            // 14. FILLERS / LOWER PRIORITY
            // ──────────────────────────────────────────────
            Enemy("turn undead", 40, SICombatCondition.None,
                "turn undead"),

            Self("divine intervention", 35, SICombatCondition.CriticalHealth,  // last resort
                "divine intervention"),

            // Out-of-combat utility (very low priority so it only fires when safe)
            Self("redemption", 10, SICombatCondition.None,
                "redemption")
        };
    }
}
