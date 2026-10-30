using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // What a gathering trip is after: some items (null = anything the bot can gather), and which node templates
    // and creatures provide them.
    sealed class SIGatherPlan
    {
        public HashSet<int> Items;
        public Func<SIGatherNode, bool> Node;
        public Func<CreatureTemplate, bool> Creature;
        public string Label;
    }

    // Gathering: herb and ore nodes from the world database, skinning corpses, and cloth from humanoids. Used by
    // the "gather" goal, by the "professions" goal when it is short of materials, and opportunistically while
    // questing or grinding (a node or a skinnable corpse right next to the bot).
    public sealed partial class SIPlayer
    {
        const float GatherSearchRadius = 60;
        readonly Dictionary<int, SpellInfo> _gatherSpells = new();
        readonly Dictionary<ObjectGuid, long> _gatherRetryAt = new();
        ObjectGuid _gatherTarget;
        long _gatherCastAt, _gatherStartedAt;
        long _gatherSettleUntil;
        int _gatherAttempts;
        string _gatherTaskLabel;

        int GatherSkill(SkillType skill) => me.GetSkillValue(skill);

        bool CanGatherNode(SIGatherNode node) => GatherSkill(node.Skill) > 0 && GatherSkill(node.Skill) >= node.RequiredSkill;

        // Skinning needs 1 up to level 10, ten points a level up to 20, then five points a level.
        static int SkinningRequirement(int level) => level <= 10 ? 1 : level <= 20 ? (level - 10) * 10 : level * 5;

        bool CanSkin(Creature creature) => GatherSkill(SkillType.Skinning) >= SkinningRequirement(creature.GetLevel());

        // Cloth and other drops come from humanoids; gather mode farms them when the bot can use cloth.
        bool UsesCloth => HasProfession(SkillType.Tailoring) || HasProfession(SIProfessionKnowledge.FirstAid) || _goal == "gather";

        bool LevelAppropriate(CreatureTemplate template) =>
            template.Stats.MinLevel <= me.GetLevel() + 1 && template.Stats.MaxLevel + 8 >= me.GetLevel();

        SIGatherPlan AnyGatherPlan()
        {
            bool skins = HasProfession(SkillType.Skinning), cloth = UsesCloth;
            bool nodes = HasProfession(SkillType.Herbalism) || HasProfession(SkillType.Mining);
            if (!skins && !cloth && !nodes) return null;
            return new SIGatherPlan
            {
                Label = "Gather " + string.Join(", ", new[] { nodes ? "nodes" : null, skins ? "skins" : null, cloth ? "cloth" : null }.Where(s => s != null)),
                // nodes that still give skill first; any openable node when none does
                Node = node => CanGatherNode(node),
                Creature = template => LevelAppropriate(template) &&
                    (skins && SIProfessionKnowledge.Instance.Skinnable(template.Entry) || cloth && template.CreatureType == CreatureType.Humanoid)
            };
        }

        SIGatherPlan GatherPlanFor(IEnumerable<int> items)
        {
            var wanted = items.ToHashSet();
            var nodes = wanted.SelectMany(SIProfessionKnowledge.Instance.NodesGiving).ToHashSet();
            var creatures = wanted.SelectMany(SIProfessionKnowledge.Instance.SkinnersGiving).Where(_ => HasProfession(SkillType.Skinning))
                .Concat(wanted.SelectMany(Knowledge.CreaturesDropping)).ToHashSet();
            if (!nodes.Any(entry => SIProfessionKnowledge.Instance.IsNode(entry, out var node) && CanGatherNode(node)) && creatures.Count == 0) return null;
            return new SIGatherPlan
            {
                Items = wanted,
                Label = "Gather " + string.Join(", ", wanted.Select(ItemName)),
                Node = node => nodes.Contains(node.Entry) && CanGatherNode(node),
                Creature = template => creatures.Contains(template.Entry) && template.Stats.MinLevel <= me.GetLevel() + 2
            };
        }

        SITask GatherTask(SIGatherPlan plan)
        {
            var points = new List<Vector3>();
            int map = me.GetMapId();
            foreach (SIGatherNode node in SIProfessionKnowledge.Instance.Nodes)
            {
                if (!plan.Node(node)) continue;
                // a node that still gives skill counts three times, so skill-up areas win
                int weight = GatherSkill(node.Skill) < node.RequiredSkill + 100 ? 3 : 1;
                foreach (SISpawn spawn in Knowledge.ObjectSpawns(node.Entry))
                    if (spawn.Map == map && SpawnAvailable(spawn))
                        for (int i = 0; i < weight; ++i) points.Add(spawn.Position);
            }
            foreach (SISpawn spawn in Knowledge.CreaturesOnMap(map))
                if (HuntableSpawn(spawn, out var template) && plan.Creature(template)) points.Add(spawn.Position);
            string key = new SITask { Kind = SITaskKind.Gather }.Key;
            points.RemoveAll(point => !SearchAreaAvailable(key, point) || IsLearnedRouteBlocked(P(point)));
            if (points.Count == 0) return null;
            float nearest = points.Min(Distance);
            var hotspots = SIQuestKnowledge.Cluster(points.Where(point => Distance(point) <= nearest + Math.Max(_settings.GrindRadius, 400)), 60)
                .OrderBy(Distance).Take(12).ToList();
            if (hotspots.Count == 0) return null;
            return new SITask { Kind = SITaskKind.Gather, Hotspots = hotspots, Score = hotspots.Min(Distance), Label = plan.Label };
        }

        SIBehaviorResult TickGather(SIGatherPlan plan)
        {
            if (plan == null) { Status = "Nothing this character can gather"; return SIBehaviorResult.Running; }
            if (TickGatherCast()) return SIBehaviorResult.Running;
            if (TrySkin()) return SIBehaviorResult.Running;
            if (StartGatherNode(plan, GatherSearchRadius)) return SIBehaviorResult.Running;
            Creature enemy = GatherEnemy(plan);
            if (enemy != null) { Status = plan.Label; BeginEncounter(enemy); TickEncounter(); return SIBehaviorResult.Running; }
            if (_task?.Kind != SITaskKind.Gather || _gatherTaskLabel != plan.Label)
            {
                if (Now < _nextReplan) return SIBehaviorResult.Running;
                _nextReplan = Now + Random.Shared.Next(1000, 5001);
                SITask task = GatherTask(plan);
                if (task == null) { Status = "No reachable gathering area on this continent; checking again shortly"; return SIBehaviorResult.Running; }
                _gatherTaskLabel = plan.Label;
                SetTask(task);
            }
            Sweep(_task);
            return SIBehaviorResult.Running;
        }

        SIBehaviorResult TickGatherGoal()
        {
            EnsureProfessionKnowledge();
            if (!SIQuestKnowledge.IsBuilt || !SIProfessionKnowledge.IsBuilt) { Status = "Loading profession knowledge"; return SIBehaviorResult.Running; }
            if (TickProfessionTrainingTask()) return SIBehaviorResult.Running;
            if (TickGatherCast()) return SIBehaviorResult.Running;
            if (Now >= _nextProfessionTrainingScan && PlanProfessionTraining()) return SIBehaviorResult.Running;
            _professionGather ??= AnyGatherPlan();
            if (Now >= _nextProfessionPlan) { _nextProfessionPlan = Now + 60000; _professionGather = AnyGatherPlan(); }
            return TickGather(_professionGather);
        }

        Creature GatherEnemy(SIGatherPlan plan) => NearbyCreatures(_settings.TargetSearchRadius)
            .Where(c => c.GetLevel() <= me.GetLevel() + 1 && plan.Creature(c.GetCreatureTemplate()) && OptionalPull(c))
            .OrderBy(c => me.GetDistance(c))
            .FirstOrDefault(c => ReserveQuestTarget(c.GetGUID()));

        // The gathering spell whose OpenLock effect opens this lock type (Herb Gathering, Mining).
        SpellInfo GatherSpell(SIGatherNode node)
        {
            KnownSpellWithEffect(SpellEffectName.OpenLock); // refreshes the caches on a spellbook change
            if (_gatherSpells.TryGetValue(node.LockType, out SpellInfo cached)) return cached;
            return _gatherSpells[node.LockType] = KnownSpellWithEffect(SpellEffectName.OpenLock, node.LockType);
        }

        bool StartGatherNode(SIGatherPlan plan, float radius)
        {
            if (!SIProfessionKnowledge.IsBuilt || !_gatherTarget.IsEmpty() || me.IsInCombat() || me.IsMounted() && me.IsFlying()) return false;
            PruneExpired(_gatherRetryAt);
            GameObject node = NearbyObjects(radius).Where(obj => obj.IsSpawned() && obj.GetGoState() == GameObjectState.Ready && obj.GetLootState()==LootState.Ready &&
                    SIProfessionKnowledge.Instance.IsNode(obj.GetEntry(), out var info) && CanGatherNode(info) && (plan == null || plan.Node(info)) &&
                    !_gatherRetryAt.ContainsKey(obj.GetGUID()) && !IsLearnedRouteBlocked(obj.GetPosition()) && !Unsafe(obj))
                .OrderBy(me.GetExactDist).FirstOrDefault();
            return node != null && BeginGather(node);
        }

        bool BeginGather(WorldObject target)
        {
            _gatherTarget = target.GetGUID(); _gatherAttempts = 0; _gatherCastAt = 0; _gatherSettleUntil = 0; _gatherStartedAt = Now;
            return TickGatherCast();
        }

        // A hostile creature near the node makes it a pull, not a gather.
        bool Unsafe(WorldObject target) => NearbyCreatures(20).Any(c => c.IsAlive() && me.IsValidAttackTarget(c) && c.IsHostileTo(me) &&
            c.GetLevel() >= me.GetLevel() + 3 && c.GetExactDist(target) < 15);

        // Skin the nearest corpse this character may skin.
        bool TrySkin()
        {
            if (!_gatherTarget.IsEmpty() || me.IsInCombat() || !HasProfession(SkillType.Skinning) || !SIProfessionKnowledge.IsBuilt) return false;
            Creature corpse = NearbyCreatures(30).Where(c => !c.IsAlive() && c.HasUnitFlag(UnitFlags.Skinnable) && CanSkin(c) &&
                    (!c.HasLootRecipient() || c.IsTappedBy(me)) && !_gatherRetryAt.ContainsKey(c.GetGUID()) && !IsLearnedRouteBlocked(c.GetPosition()))
                .OrderBy(me.GetExactDist).FirstOrDefault();
            return corpse != null && BeginGather(corpse);
        }

        // Walks to the current node or corpse, casts the gathering spell, waits for it and takes the loot. Mining
        // veins can be worked several times before they despawn.
        bool TickGatherCast()
        {
            if (_gatherTarget.IsEmpty()) return false;
            WorldObject target = Global.ObjAccessor.GetWorldObject(me, _gatherTarget);
            void Finish(long retry)
            {
                if (retry > 0) _gatherRetryAt[_gatherTarget] = Now + retry;
                _gatherTarget = ObjectGuid.Empty; _gatherCastAt = 0; _gatherSettleUntil=0;
            }
            if (me.IsInCombat()) { Finish(0); return false; }
            if(_gatherSettleUntil!=0)
            {
                if(Now<_gatherSettleUntil){Behavior="Gather";Status="Waiting for the gathered node to update";return true;}
                _gatherSettleUntil=0;
                // Re-resolve on this later tick: loot release/despawn may only
                // have updated the object after the previous AI update ended.
                if(target==null||target is GameObject spentNode&&(!spentNode.IsSpawned()||spentNode.GetGoState()!=GameObjectState.Ready||spentNode.GetLootState()!=LootState.Ready)||
                    target is Creature spentBody&&!spentBody.HasUnitFlag(UnitFlags.Skinnable))
                {Finish(250);return false;}
                if(++_gatherAttempts>=3){Finish(300000);return false;}
            }
            if (_gatherCastAt != 0)
            {
                if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true) && Now - _gatherCastAt < 15000) { Behavior = "Gather"; return true; }
                _gatherCastAt = 0;
                int before = ItemsLooted;
                me.GetSession().LootOpenViewForSI();
                if (ItemsLooted > before) { NoteGameplayProgress(); _gatherAttempts = 0; }
                _gatherSettleUntil=Now+250;
                Behavior="Gather";Status="Waiting for the gathered node to update";
                return true;
            }
            bool gone = target == null || target is GameObject obj && (!obj.IsSpawned() || obj.GetGoState() != GameObjectState.Ready) ||
                target is Creature body && (body.IsAlive() || !body.HasUnitFlag(UnitFlags.Skinnable));
            if (gone || Now - _gatherStartedAt > 60000) { Finish(gone ? 0 : 300000); return false; }

            SpellInfo spell = target is GameObject gameObject && SIProfessionKnowledge.Instance.IsNode(gameObject.GetEntry(), out var info)
                ? GatherSpell(info) : KnownSpellWithEffect(SpellEffectName.Skinning);
            if (spell == null) { Finish(600000); return false; }
            string label = (target is Creature ? "Skinning " : "Gathering ") + target.GetName();
            SIBehaviorResult move = MoveWithin(target, target is Creature ? SharedConst.InteractionDistance - 1.5f : 3f, label);
            if (move == SIBehaviorResult.Failure) { Finish(300000); return false; }
            if (move != SIBehaviorResult.Success || me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) { Behavior = "Gather"; Status = label; return true; }
            CancelTravel(); Dismount(); FaceTarget(target, select: false);
            SpellCastResult result = me.CastSpell(target, spell.Id);
            if (result != SpellCastResult.SpellCastOk)
            {
                Report("gather_failed", $"{label}: {result}");
                Finish(result == SpellCastResult.TryAgain ? 3000 : 300000); return false;
            }
            _gatherCastAt = Now;
            Behavior = "Gather"; Status = label;
            return true;
        }

        // Gathering that every working goal does on the way: a node next to the bot, or a corpse it can skin.
        bool TickProfessionOpportunities()
        {
            if (!SIProfessionKnowledge.IsBuilt) { EnsureProfessionKnowledge(); return false; }
            if (TickGatherCast()) return true;
            if (_goal is not ("quest" or "grind" or "gather" or "professions")) return false;
            if(_professionTrainingProgressDue&&(_goal is "gather" or "professions"))return false;
            if (TrySkin()) return true;
            // Opportunistic nodes must also be considered while a profession
            // plan farms a different material, such as cloth from humanoids.
            return StartGatherNode(null,20);
        }
    }
}
