// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;

namespace Game.DataStorage
{
    public sealed class EmotesRecord
    {
        public int Id;
        public string EmoteSlashCommand;
        private short _animId = default;
        public uint EmoteFlags;
        public int EmoteSpecProc;
        public uint EmoteSpecProcParam;
        public uint EventSoundID;
        public uint SpellVisualKitId;
        public int ClassMask;
        private int[] _raceMask = new int[2];

        #region Properties
        public RaceMask RaceMask => _raceMask.ToRaceMask();
        public Anim AnimId => (Anim)_animId;
        #endregion
    }

    public sealed class EmotesTextRecord
    {
        public int Id;
        public string Name;
        private ushort _emoteId = default;

        #region Properties
        public Emote EmoteId => (Emote)_emoteId;
        #endregion
    }

    public sealed class EmotesTextSoundRecord
    {
        public int Id;
        private byte _raceId = default;
        private byte _classId = default;
        private byte _sexId = default;
        public int SoundId;
        public int EmotesTextId;

        #region Properties
        public Race RaceId => (Race)_raceId;
        public Class ClassId => (Class)_classId;
        public Gender SexId => (Gender)_sexId;
        #endregion
    }

    public sealed class ExpectedStatRecord
    {
        public int Id;
        private int _expansionID = default;
        public float CreatureHealth;
        public float PlayerHealth;
        public float CreatureAutoAttackDps;
        public float CreatureArmor;
        public float PlayerMana;
        public float PlayerPrimaryStat;
        public float PlayerSecondaryStat;
        public float ArmorConstant;
        public float CreatureSpellDamage;
        public int ContentSetID;
        public int Lvl;

        #region Properties
        public Expansion ExpansionID => (Expansion)_expansionID;
        #endregion
    }

    public sealed class ExpectedStatModRecord
    {
        public int Id;
        public float CreatureHealthMod;
        public float PlayerHealthMod;
        public float CreatureAutoAttackDPSMod;
        public float CreatureArmorMod;
        public float PlayerManaMod;
        public float PlayerPrimaryStatMod;
        public float PlayerSecondaryStatMod;
        public float ArmorConstantMod;
        public float CreatureSpellDamageMod;
    }
}
