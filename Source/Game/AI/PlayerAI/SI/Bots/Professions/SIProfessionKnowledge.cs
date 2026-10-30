using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Loots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Game.AI
{
    // A gathering node template: the gathering skill its lock asks for, and the lowest skill that opens it.
    public readonly record struct SIGatherNode(int Entry, SkillType Skill, int RequiredSkill, int LockType);

    // Profession data from the world database and DB2s, shared by all SI players and built once off the map
    // thread after SIQuestKnowledge: which trainers teach each profession, which node templates need which
    // gathering skill, which items nodes and skinning give, and where spell focus objects (anvils, forges) stand.
    public sealed class SIProfessionKnowledge
    {
        static readonly Lazy<SIProfessionKnowledge> Lazy = new(() => new SIProfessionKnowledge(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static SIProfessionKnowledge Instance => Lazy.Value;
        public static bool IsBuilt => Lazy.IsValueCreated;

        static readonly int[] NoEntries = Array.Empty<int>();

        public static readonly SkillType[] PrimaryProfessions =
        {
            SkillType.Herbalism, SkillType.Mining, SkillType.Skinning, SkillType.Alchemy, SkillType.Blacksmithing,
            SkillType.Enchanting, SkillType.Engineering, SkillType.Leatherworking, SkillType.Tailoring
        };
        public static readonly SkillType[] GatheringProfessions = { SkillType.Herbalism, SkillType.Mining, SkillType.Skinning };
        // First Aid (129) left the SkillType enum after Battle for Azeroth; Classic still has it.
        public const SkillType FirstAid = (SkillType)129;
        public static readonly SkillType[] SecondaryProfessions = { FirstAid };

        readonly Dictionary<SkillType, int[]> _trainers = new();
        readonly Dictionary<int, SIGatherNode> _nodes = new();
        readonly Dictionary<int, int[]> _itemNodes = new();
        readonly Dictionary<int, int[]> _itemSkinners = new();
        readonly HashSet<int> _skinnable = new();
        readonly Dictionary<int, int[]> _focusObjects = new();

        SIProfessionKnowledge()
        {
            var knowledge = SIQuestKnowledge.Instance;
            var trainers = new Dictionary<SkillType, HashSet<int>>();
            var itemNodes = new Dictionary<int, HashSet<int>>();
            var itemSkinners = new Dictionary<int, HashSet<int>>();
            var focus = new Dictionary<int, List<int>>();

            foreach (CreatureTemplate template in Global.ObjectMgr.GetCreatureTemplates().Values)
            {
                if (template.Npcflag.HasFlag(NPCFlags1.Trainer) && template.TrainerClass == Class.None && knowledge.CreatureSpawns(template.Entry).Length != 0)
                    foreach (int id in Global.ObjectMgr.GetCreatureTrainerIds(template.Entry))
                    {
                        Trainer trainer = Global.ObjectMgr.GetTrainer(id);
                        if (trainer == null || trainer.GetTrainerType() != TrainerType.Tradeskills) continue;
                        foreach (TrainerSpell spell in trainer.Spells)
                        {
                            SkillType skill = TrainerSpellSkill(spell);
                            if (skill != SkillType.None) Add(trainers, skill, template.Entry);
                        }
                    }
                int skinLoot = template.Stats.SkinLootID;
                if (skinLoot != 0 && knowledge.CreatureSpawns(template.Entry).Length != 0)
                {
                    _skinnable.Add(template.Entry);
                    LootStorage.Skinning.ForEachDrop(skinLoot, (item, chance, quest) => Add(itemSkinners, item, template.Entry));
                }
            }

            foreach (GameObjectTemplate template in Global.ObjectMgr.GetGameObjectTemplates().Values)
            {
                if (knowledge.ObjectSpawns(template.entry).Length == 0) continue;
                if (template.type == GameObjectTypes.SpellFocus && template.GetSpellFocusType() != 0)
                {
                    if (!focus.TryGetValue(template.GetSpellFocusType(), out var list)) focus[template.GetSpellFocusType()] = list = new();
                    list.Add(template.entry);
                    continue;
                }
                if (template.type != GameObjectTypes.Chest || !CliDB.LockStorage.TryGetValue(template.GetLockId(), out LockRecord lockInfo)) continue;
                for (int i = 0; i < lockInfo.Index.Length; ++i)
                {
                    if (lockInfo.Type[i] != (byte)LockKeyType.Skill) continue;
                    SkillType skill = GatheringSkill(SharedConst.SkillByLockType((LockType)lockInfo.Index[i]));
                    if (skill is not (SkillType.Herbalism or SkillType.Mining)) continue;
                    _nodes[template.entry] = new SIGatherNode(template.entry, skill, lockInfo.Skill[i], lockInfo.Index[i]);
                    int lootId = template.GetLootId();
                    if (lootId != 0) LootStorage.Gameobject.ForEachDrop(lootId, (item, chance, quest) => Add(itemNodes, item, template.entry));
                    break;
                }
            }

            foreach (var pair in trainers) _trainers[pair.Key] = pair.Value.ToArray();
            foreach (var pair in itemNodes) _itemNodes[pair.Key] = pair.Value.ToArray();
            foreach (var pair in itemSkinners) _itemSkinners[pair.Key] = pair.Value.ToArray();
            foreach (var pair in focus) _focusObjects[pair.Key] = pair.Value.ToArray();
        }

        // The expansion lines (Classic Herbalism, ...) count as the profession itself.
        public static SkillType GatheringSkill(SkillType skill) => skill switch
        {
            SkillType.ClassicHerbalism => SkillType.Herbalism,
            SkillType.ClassicMining => SkillType.Mining,
            SkillType.ClassicSkinning => SkillType.Skinning,
            _ => skill
        };

        // The profession a trainer lesson belongs to: its skill requirement, or what the taught spell is part of.
        public static SkillType TrainerSpellSkill(TrainerSpell spell)
        {
            if (IsProfession(spell.ReqSkillLine)) return spell.ReqSkillLine;
            var info = Global.SpellMgr.GetSpellInfo(spell.SpellId, Difficulty.None);
            if (info == null) return SkillType.None;
            foreach (var effect in info.GetEffects())
            {
                if (effect.IsEffect(SpellEffectName.Skill) && IsProfession((SkillType)effect.MiscValue)) return (SkillType)effect.MiscValue;
                int taught = effect.IsEffect(SpellEffectName.LearnSpell) ? effect.TriggerSpell : 0;
                foreach (var ability in Global.SpellMgr.GetSkillLineAbilityMapBounds(taught != 0 ? taught : spell.SpellId))
                    if (IsProfession(ability.SkillLine)) return ability.SkillLine;
                if (taught != 0 && Global.SpellMgr.GetSpellInfo(taught, Difficulty.None) is { } learned)
                    foreach (var learnedEffect in learned.GetEffects())
                        if (learnedEffect.IsEffect(SpellEffectName.Skill) && IsProfession((SkillType)learnedEffect.MiscValue))
                            return (SkillType)learnedEffect.MiscValue;
            }
            return SkillType.None;
        }

        public static bool IsProfession(SkillType skill) => Array.IndexOf(PrimaryProfessions, skill) >= 0 || Array.IndexOf(SecondaryProfessions, skill) >= 0;

        static void Add<TKey>(Dictionary<TKey, HashSet<int>> map, TKey key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>();
            set.Add(value);
        }

        public int[] Trainers(SkillType skill) => _trainers.GetValueOrDefault(skill, NoEntries);
        public bool IsNode(int entry, out SIGatherNode node) => _nodes.TryGetValue(entry, out node);
        public IEnumerable<SIGatherNode> Nodes => _nodes.Values;
        public int[] NodesGiving(int item) => _itemNodes.GetValueOrDefault(item, NoEntries);
        public int[] SkinnersGiving(int item) => _itemSkinners.GetValueOrDefault(item, NoEntries);
        public bool Skinnable(int entry) => _skinnable.Contains(entry);
        public int[] SpellFocusObjects(int focusType) => _focusObjects.GetValueOrDefault(focusType, NoEntries);
    }
}
