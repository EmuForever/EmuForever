using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.AI
{
    // The "professions" goal: learn two professions that suit the character when it has none, train new ranks
    // and recipes as soon as a trainer offers them, and level crafting by making the best recipe whose materials
    // it can get: from its bags, its bank, a vendor, the auction house (when it has the gold), or by gathering
    // them itself. With nothing left to craft it levels its gathering professions until the next trainer visit.
    public sealed partial class SIPlayer
    {
        sealed record SIRecipe(SpellInfo Spell, SkillLineAbilityRecord Ability)
        {
            public SkillType Skill => Ability.SkillLine;
            public int Product => Spell.GetEffects().FirstOrDefault(effect => effect.IsEffect(SpellEffectName.CreateItem))?.ItemType ?? 0;
            public bool Enchant => Spell.HasEffect(SpellEffectName.EnchantItem);
        }

        enum SIProfessionStep { None, Craft, Focus, Withdraw, Vendor, Auction, Gather, Capped }

        long _nextProfessionTrainingScan, _nextProfessionPlan, _craftCastAt;
        readonly Dictionary<SkillType,int> _professionSkillAtTrainingScan=new();
        bool _professionTrainingProgressDue;
        internal void ProfessionSkillAdvanced(SkillType skill,int before,int after)
        {
            if(!SIProfessionKnowledge.IsProfession(skill))return;
            if(!_professionSkillAtTrainingScan.TryGetValue(skill,out int scanned))
                _professionSkillAtTrainingScan[skill]=scanned=before;
            if(after<scanned){_professionSkillAtTrainingScan[skill]=after;return;}
            if(after-scanned>=5){_professionTrainingProgressDue=true;_nextProfessionTrainingScan=0;}
        }
        ulong _recipeRevision = ulong.MaxValue;
        readonly Dictionary<SkillType, List<SIRecipe>> _recipes = new();
        readonly HashSet<SkillType> _professionRewardsChecked = new();
        readonly Dictionary<int, long> _recipeRetryAt = new();
        readonly Dictionary<int, long> _auctionTriedAt = new();
        readonly Dictionary<int, int> _professionShopping = new();
        readonly Dictionary<int, int> _professionSupplies = new();
        readonly Dictionary<int,int> _professionWithdraw = new();
        long _bankWithdrawRetryAt;
        SIProfessionStep _professionStep;
        SIRecipe _professionRecipe;
        SIGatherPlan _professionGather;
        SIRecipe _professionBatchRoot,_craftRecipe;
        int _professionBatchCraftsLeft,_professionBatchSkillGoal;
        readonly Dictionary<int,int> _professionGatherTargets=new();
        readonly Dictionary<int,int> _craftReagentsBefore=new();
        SISpawn? _professionAuctioneer;
        SISpawn? _professionFocusDestination;
        int _professionFocusType;
        string _professionSummary = "Planning profession work";

        static void EnsureProfessionKnowledge()
        {
            if (SIQuestKnowledge.IsBuilt && !SIProfessionKnowledge.IsBuilt && System.Threading.Interlocked.Exchange(ref _professionKnowledgeStarted, 1) == 0)
                _ = Task.Run(() => SIProfessionKnowledge.Instance);
        }
        static int _professionKnowledgeStarted;

        void ResetProfessionGoal()
        {
            _nextProfessionPlan = 0; _nextProfessionTrainingScan = 0; _professionStep = SIProfessionStep.None;
            _professionRecipe = null; _professionGather = null; _professionAuctioneer = null; _gatherTaskLabel = null; _craftCastAt = 0;
            _professionShopping.Clear(); _professionSupplies.Clear(); _professionWithdraw.Clear();
            _professionBatchRoot=null;_professionBatchCraftsLeft=0;_professionGatherTargets.Clear();_craftRecipe=null;_craftReagentsBefore.Clear();
            _professionFocusDestination=null;_professionFocusType=0;
            ResetSocialGoal();
        }

        static string SkillName(SkillType skill) => skill == SIProfessionKnowledge.FirstAid ? "First Aid" : skill.ToString();

        bool HasProfession(SkillType skill) => me.GetSkillValue(skill) > 0;
        static bool IsGathering(SkillType skill) => Array.IndexOf(SIProfessionKnowledge.GatheringProfessions, skill) >= 0;
        IEnumerable<SkillType> KnownProfessions() =>
            SIProfessionKnowledge.PrimaryProfessions.Concat(SIProfessionKnowledge.SecondaryProfessions).Where(HasProfession);

        // Classic's natural pairs: a gathering profession feeding the crafting one, or Tailoring with Enchanting
        // (cloth drops from humanoids; tailored greens disenchant). Racial bonuses decide for Tauren and Gnomes.
        static SkillType[] PreferredProfessions(Class playerClass, Race race)
        {
            if (race == Race.Tauren) return new[] { SkillType.Herbalism, SkillType.Alchemy };
            if (race == Race.Gnome) return new[] { SkillType.Mining, SkillType.Engineering };
            return playerClass switch
            {
                Class.Warrior or Class.Paladin or Class.DeathKnight => new[] { SkillType.Mining, SkillType.Blacksmithing },
                Class.Rogue or Class.Hunter or Class.Druid or Class.Shaman => new[] { SkillType.Skinning, SkillType.Leatherworking },
                _ => new[] { SkillType.Tailoring, SkillType.Enchanting }
            };
        }

        // The professions this bot works on: what it knows plus First Aid, which every SI player learns, topped up
        // to two primary professions in the professions goal. Other goals only add First Aid.
        List<SkillType> WantedProfessions()
        {
            var wanted = KnownProfessions().ToList();
            if (!wanted.Contains(SIProfessionKnowledge.FirstAid)) wanted.Add(SIProfessionKnowledge.FirstAid);
            if (_goal != "professions") return wanted;
            int primary = wanted.Count(skill => Array.IndexOf(SIProfessionKnowledge.PrimaryProfessions, skill) >= 0);
            foreach (SkillType skill in PreferredProfessions(me.GetClass(), me.GetRace()))
                if (primary < 2 && !wanted.Contains(skill)) { wanted.Add(skill); ++primary; }
            return wanted;
        }

        // ---- Training -------------------------------------------------------------------------------------

        // Native teachability handles rank, level and skill requirements. New
        // recipes are eligible at each progress scan even while older ones work.
        internal bool ProfessionLessonWanted(TrainerSpell spell)
        {
            SkillType skill = SIProfessionKnowledge.TrainerSpellSkill(spell);
            if (skill == SkillType.None || !WantedProfessions().Contains(skill)) return false;
            var abilities = Global.SpellMgr.GetSkillLineAbilityMapBounds(spell.SpellId);
            foreach (var ability in abilities)
                if (ability.IsSkillTradeSpell) return HasProfession(skill);
            return true;
        }

        bool CanAdvanceKnownRecipes(SkillType skill) => HasProfession(skill) &&
            me.GetSkillValue(skill) < me.GetMaxSkillValue(skill) &&
            KnownRecipePotential(skill);

        bool KnownRecipePotential(SkillType skill) =>
            KnownRecipes(skill).Any(recipe => RecipeColor(recipe) < 3 && (!recipe.Enchant || EnchantTarget(recipe) != null));

        // A trainer trip; questing and grinding only take a short detour and resume their task afterwards.
        bool PlanProfessionTraining(float maxDistance = float.MaxValue)
        {
            if(CommittedToJourney())return false;
            foreach(var skill in KnownProfessions())_professionSkillAtTrainingScan[skill]=me.GetPureSkillValue(skill);
            _professionTrainingProgressDue=false;
            _nextProfessionTrainingScan = Now + (maxDistance == float.MaxValue ? 60000 : 300000);
            var withLessons = new List<int>();
            var checkedTrainers = new Dictionary<int, TrainerSpell[]>();
            foreach (SkillType skill in WantedProfessions())
                foreach (int entry in SIProfessionKnowledge.Instance.Trainers(skill))
                {
                    if (!FriendlyTemplate(entry)) continue;
                    float discount = PriceDiscount(entry);
                    bool lesson = false;
                    foreach (int id in Global.ObjectMgr.GetCreatureTrainerIds(entry))
                    {
                        if (!checkedTrainers.TryGetValue(id, out var lessons))
                        {
                            Trainer trainer = Global.ObjectMgr.GetTrainer(id);
                            lessons = trainer != null && trainer.GetTrainerType() == TrainerType.Tradeskills
                                ? trainer.GetTeachableSpells(me).Where(ProfessionLessonWanted).ToArray() : Array.Empty<TrainerSpell>();
                            checkedTrainers[id] = lessons;
                        }
                        lesson |= lessons.Any(spell => (long)(spell.MoneyCost * discount) <= SpareMoney());
                    }
                    if (lesson) withLessons.Add(entry);
                }
            _professionTrainerLessons.Clear();
            _professionTrainerLessons.UnionWith(withLessons);
            if (withLessons.Count == 0) return false;
            SISpawn? spawn = NearestServiceSpawn(withLessons, SITaskKind.ProfessionTrain);
            if (spawn == null) { _professionSummary = "A profession trainer has lessons, but none stands on this continent"; return false; }
            if (Distance(spawn.Value.Position) > maxDistance) return false;
            var task = new SITask { Kind = SITaskKind.ProfessionTrain, Destination = spawn, Creatures = new[] { spawn.Value.Entry },
                Label = $"Train professions at {CreatureName(spawn.Value.Entry)}" };
            if (Blacklisted(task.Key)) return false;
            _suspendedTask = null;
            _professionReturnTask = _goal is "quest" or "grind" or "gather" or "professions" && _task != null && !TaskDone(_task) ? _task : null;
            _professionReturnTask?.Suspend(Now);
            SetTask(task);
            return true;
        }
        SITask _professionReturnTask;

        bool ProfessionTrainAt(Creature trainer)
        {
            if (trainer == null) return false;
            FaceTarget(trainer);
            float discount = me.GetReputationPriceDiscount(trainer);
            int learned = me.GetSession().TrainProfessionForSI(trainer, spell => ProfessionLessonWanted(spell) &&
                (long)(spell.MoneyCost * discount) <= SpareMoney());
            if (learned > 0)
            {
                _spellCount = -1; _recipeRevision = ulong.MaxValue; _nextMaterialPlan = 0; _recipeRetryAt.Clear();
                NoteGameplayProgress();
                Report("profession_trained", $"Learned {learned} profession lesson(s) from {trainer.GetName()}");
            }
            Status = learned > 0 ? $"Learned {learned} profession lesson(s) from {trainer.GetName()}" : $"Nothing affordable to learn from {trainer.GetName()}";
            _nextProfessionPlan = 0;
            return true;
        }

        // ---- Recipes --------------------------------------------------------------------------------------

        List<SIRecipe> KnownRecipes(SkillType skill)
        {
            // Starting recipes such as Bolt of Linen Cloth are skill rewards,
            // not vendor patterns. Let native rules restore eligible defaults.
            if (HasProfession(skill) && _professionRewardsChecked.Add(skill))
                me.LearnSkillRewardedSpells(skill, me.GetSkillValue(skill), me.GetRace());
            if (_recipeRevision != me.SpellBook.Revision) { _recipes.Clear(); _recipeRevision = me.SpellBook.Revision; }
            if (_recipes.TryGetValue(skill, out var cached)) return cached;
            var recipes = new List<SIRecipe>();
            foreach (var (spellId, spell) in me.GetSpellMap())
            {
                if (spell.Disabled || spell.State == PlayerSpellState.Removed) continue;
                foreach (var ability in Global.SpellMgr.GetSkillLineAbilityMapBounds(spellId))
                {
                    if (ability.SkillLine != skill || !ability.IsSkillTradeSpell) continue;
                    SpellInfo info = Global.SpellMgr.GetSpellInfo(spellId, Difficulty.None);
                    if (info != null && (info.Reagent.Any(item => item > 0) || info.HasEffect(SpellEffectName.EnchantItem))) recipes.Add(new SIRecipe(info, ability));
                    break;
                }
            }
            return _recipes[skill] = recipes;
        }

        // 0 orange, 1 yellow, 2 green, 3 grey: the same thresholds the core uses for skill-up chances.
        int RecipeColor(SIRecipe recipe)
        {
            int skill = me.GetSkillValue(recipe.Skill), high = recipe.Ability.TrivialSkillLineRankHigh, low = recipe.Ability.TrivialSkillLineRankLow;
            if (skill >= high) return 3;
            if (skill >= (low + high) / 2) return 2;
            return skill >= low ? 1 : 0;
        }

        int MissingReagents(SIRecipe recipe)
        {
            int missing = 0;
            for (int i = 0; i < recipe.Spell.Reagent.Length; ++i)
                if (recipe.Spell.Reagent[i] > 0) missing += Math.Max(0, recipe.Spell.ReagentCount[i] - me.GetItemCount(recipe.Spell.Reagent[i], true));
            return missing;
        }

        bool HasReagentsInBags(SIRecipe recipe) => HasReagentsInBags(recipe.Spell);
        bool HasReagentsInBags(SpellInfo spell)
        {
            for (int i = 0; i < spell.Reagent.Length; ++i)
                if (spell.Reagent[i] > 0 && me.GetItemCount(spell.Reagent[i]) < spell.ReagentCount[i]) return false;
            return true;
        }

        // An equipped item the enchantment fits, preferring one without a permanent enchantment yet.
        Item EnchantTarget(SIRecipe recipe)
        {
            Item best = null;
            for (byte slot = EquipmentSlot.Start; slot < EquipmentSlot.End; ++slot)
            {
                Item item = me.GetItemByPos(slot);
                if (item == null || !item.IsFitToSpellRequirements(recipe.Spell)) continue;
                if (item.GetEnchantmentId(EnchantmentSlot.EnhancementPermanent) == 0) return item;
                best ??= item;
            }
            return best;
        }

        bool NearSpellFocus(int focusType) => NearbyObjects(10).Any(obj => obj.IsSpawned() &&
            obj.GetGoInfo().GetSpellFocusType() == focusType && me.GetExactDist(obj) <= 5);

        // ---- Planning -------------------------------------------------------------------------------------

        void PlanProfessionWork()
        {
            _nextProfessionPlan = Now + 5000;
            _professionStep = SIProfessionStep.None; _professionRecipe = null; _professionGather = null;
            _professionSupplies.Clear(); _professionWithdraw.Clear();
            _professionGatherTargets.Clear();
            PruneExpired(_recipeRetryAt);
            if(_professionBatchRoot is { } retained&&_professionBatchCraftsLeft>0&&me.GetSkillValue(retained.Skill)<_professionBatchSkillGoal&&
                !_recipeRetryAt.ContainsKey(retained.Spell.Id)&&ResolveRecipe(retained,_goal!="social",new SIProfessionRecipePath(),_professionBatchCraftsLeft))return;
            _professionBatchRoot=null;_professionBatchCraftsLeft=0;
            bool capped = false;
            var candidates = new List<SIRecipe>();
            foreach (SkillType skill in KnownProfessions().Where(skill => !IsGathering(skill)))
            {
                if (me.GetSkillValue(skill) >= me.GetMaxSkillValue(skill)) { capped = true; continue; }
                var recipes = KnownRecipes(skill).Where(recipe => RecipeColor(recipe) < 3 && !_recipeRetryAt.ContainsKey(recipe.Spell.Id))
                    .OrderBy(RecipeColor).ThenBy(MissingReagents).ThenByDescending(recipe => recipe.Ability.MinSkillLineRank).ToList();
                candidates.AddRange(recipes);
            }
            // Try owned/vendor/AH materials for every useful recipe before any
            // recipe may send the bot gathering. A gatherable first recipe must
            // not hide an affordable AH craft later in the candidate list.
            if (SIProfessionProcurement.TryRecipes(candidates, (recipe, allowGather) =>
            {
                _professionSupplies.Clear();_professionWithdraw.Clear();_professionGatherTargets.Clear();_professionShopping.Clear();
                if(allowGather&&_goal=="social"||_recipeRetryAt.ContainsKey(recipe.Spell.Id))return false;
                int crafts=SIProfessionBatchPolicy.Crafts(me.GetSkillValue(recipe.Skill),me.GetMaxSkillValue(recipe.Skill),
                    recipe.Ability.TrivialSkillLineRankLow,recipe.Ability.TrivialSkillLineRankHigh,recipe.Ability.NumSkillUps);
                if(crafts==0||!ResolveRecipe(recipe,allowGather,new SIProfessionRecipePath(),crafts))return false;
                _professionBatchRoot=recipe;_professionBatchCraftsLeft=crafts;
                _professionBatchSkillGoal=SIProfessionBatchPolicy.SkillTarget(me.GetSkillValue(recipe.Skill),me.GetMaxSkillValue(recipe.Skill),recipe.Ability.TrivialSkillLineRankHigh);
                return true;
            })) return;
            if (_goal != "social" && (KnownProfessions().Any(IsGathering) || KnownProfessions().Any(skill => skill is SkillType.Tailoring || skill == SIProfessionKnowledge.FirstAid)))
            {
                _professionGather = AnyGatherPlan();
                if (_professionGather != null)
                {
                    _professionStep = SIProfessionStep.Gather;
                    _professionSummary = capped ? "Crafting is at its rank cap; gathering until the next trainer rank" : "Nothing to craft; levelling gathering";
                    return;
                }
            }
            _professionStep = SIProfessionStep.Capped;
            _professionSummary = capped ? "Profession skill is at its rank cap; level up to train the next rank" : "No profession work available";
        }

        // Decides how to make the recipe's crafts now, or how to get what it is missing.
        bool ResolveRecipe(SIRecipe recipe, bool allowGather, SIProfessionRecipePath path,int crafts=1)
        {
            if (!path.Enter(recipe.Spell.Id)) return false;
            try { return ResolveRecipeMaterials(recipe, allowGather, path,crafts); }
            finally { path.Leave(recipe.Spell.Id); }
        }

        bool ResolveRecipeMaterials(SIRecipe recipe, bool allowGather, SIProfessionRecipePath path,int crafts)
        {
            if (recipe.Enchant && EnchantTarget(recipe) == null) { _recipeRetryAt[recipe.Spell.Id] = Now + 600000; return false; }

            // tools: hammer, mining pick, runed rod
            var supplies = new Dictionary<int, int>();
            for (int i = 0; i < recipe.Spell.Totem.Length; ++i)
                if (recipe.Spell.Totem[i] > 0 && !me.HasItemCount(recipe.Spell.Totem[i]))
                {
                    if (Knowledge.VendorsSelling(recipe.Spell.Totem[i]).Length == 0) { _recipeRetryAt[recipe.Spell.Id] = Now + 600000; return false; }
                    supplies[recipe.Spell.Totem[i]] = 1;
                }
            for (int i = 0; i < recipe.Spell.TotemCategory.Length; ++i)
                if (recipe.Spell.TotemCategory[i] > 0 && !me.HasItemTotemCategory(recipe.Spell.TotemCategory[i]))
                {
                    int tool = ToolForCategory(recipe.Spell.TotemCategory[i]);
                    if (tool == 0) { _recipeRetryAt[recipe.Spell.Id] = Now + 600000; return false; }
                    supplies[tool] = 1;
                }

            var missing = new Dictionary<int, int>();
            bool withdraw = false;
            for (int i = 0; i < recipe.Spell.Reagent.Length; ++i)
            {
                int item = recipe.Spell.Reagent[i], count = SIProfessionBatchPolicy.Reagents(recipe.Spell.ReagentCount[i],crafts);
                if (item <= 0 || count <= 0 || me.GetItemCount(item) >= count) continue;
                int owned = me.GetItemCount(item, true);
                if (owned >= count)
                {
                    if(Now<_bankWithdrawRetryAt){_recipeRetryAt[recipe.Spell.Id]=_bankWithdrawRetryAt;return false;}
                    withdraw = true; _professionWithdraw[item] = count; continue;
                }
                missing[item] = count - owned;
            }

            if (supplies.Count == 0 && missing.Count == 0 && !withdraw)
            {
                _professionRecipe = recipe;
                _professionStep = recipe.Spell.RequiresSpellFocus != 0 && !NearSpellFocus(recipe.Spell.RequiresSpellFocus) ? SIProfessionStep.Focus : SIProfessionStep.Craft;
                _professionSummary = $"Crafting {recipe.Spell.SpellName[Locale.enUS]} ({SkillName(recipe.Skill)} {me.GetSkillValue(recipe.Skill)})";
                return true;
            }
            if (withdraw && missing.Count == 0 && supplies.Count == 0)
            {
                _professionStep = SIProfessionStep.Withdraw; _professionRecipe = recipe;
                _professionSummary = $"Collecting {SkillName(recipe.Skill)} materials from the bank";
                return true;
            }

            // Check actual affordable AH listings before choosing intermediates or
            // any gathering source. An empty market does not cause a wasted trip.
            if (TryPlanProfessionAuction(missing))
            {
                _professionStep = SIProfessionStep.Auction; _professionRecipe = recipe;
                _professionSummary = $"Buying available materials for {recipe.Spell.SpellName[Locale.enUS]} at the nearest auction house";
                return true;
            }

            var gather = new List<int>();
            var known = KnownProfessions().SelectMany(KnownRecipes).ToArray();
            foreach (var (item, count) in missing)
            {
                var makers = known.Where(other => other.Product == item).ToArray();
                if (SIProfessionProcurement.ResolveMaterial(makers,
                    intermediate => !_recipeRetryAt.ContainsKey(intermediate.Spell.Id) && ResolveRecipe(intermediate, allowGather, path,
                        (int)Math.Ceiling((double)count/Math.Max(1,intermediate.Spell.GetEffects().FirstOrDefault(e=>e.IsEffect(SpellEffectName.CreateItem))?.BasePoints??1))),
                    () =>
                    {
                        if (SIProfessionBatchPolicy.VendorAllowed(item)&&Knowledge.VendorsSelling(item).Length != 0) supplies[item] = count;
                        else gather.Add(item);
                        return false;
                    })) return true;
            }
            if(supplies.Count!=0&&gather.Count!=0&&GatherPlanFor(gather)==null)return false;
            if (supplies.Count != 0 && SupplyVendor(supplies.Keys) != null)
            {
                foreach (var pair in supplies) _professionSupplies[pair.Key] = me.GetItemCount(pair.Key)+pair.Value;
                _professionStep = SIProfessionStep.Vendor; _professionRecipe = recipe;
                _professionSummary = $"Buying supplies for {recipe.Spell.SpellName[Locale.enUS]}";
                return true;
            }
            gather.AddRange(supplies.Keys.Where(missing.ContainsKey));
            if (!allowGather) return false;
            SIGatherPlan plan = gather.Count != 0 ? GatherPlanFor(gather) : null;
            if (plan != null)
            {
                foreach(int item in gather.Distinct())if(missing.TryGetValue(item,out int needed))_professionGatherTargets[item]=me.GetItemCount(item,true)+needed;
                _professionStep = SIProfessionStep.Gather; _professionGather = plan; _professionRecipe = recipe;
                _professionSummary = $"Gathering {string.Join(", ", gather.Select(ItemName))} for {recipe.Spell.SpellName[Locale.enUS]}";
                return true;
            }
            _recipeRetryAt[recipe.Spell.Id] = Now + 600000;
            return false;
        }

        bool TryPlanProfessionAuction(IReadOnlyDictionary<int, int> missing)
        {
            _professionAuctioneer = null;
            if (missing.Count == 0 || ShoppingAllowance() <= 0 || me.GetFreeInventorySlotCount() == 0 ||
                me.GetLevel() < WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32) return false;
            var check = missing.Where(pair => SIAuctionShoppingPolicy.MayScanMaterial(Now,
                _auctionTriedAt.TryGetValue(pair.Key, out long last) ? last : null)).ToArray();
            if (check.Length == 0) return false;
            foreach (var pair in check)
            {
                _professionShopping[pair.Key] = me.GetItemCount(pair.Key, true) + pair.Value;
                _auctionTriedAt[pair.Key] = Now;
            }
            _nextMaterialPlan = 0;
            var checkedFactions = new HashSet<int>();
            foreach (var spawn in ServiceSpawns(Knowledge.Auctioneers, SITaskKind.Vendor, SIVendorPurpose.Auction))
            {
                int faction = Global.ObjectMgr.GetCreatureTemplate(spawn.Entry).Faction;
                if (!checkedFactions.Add(faction) || !ScanAuctionPurchases(faction, true, true)) continue;
                _professionAuctioneer = spawn;
                return true;
            }
            return false;
        }

        // The vendor-sold item of a tool category (Blacksmith Hammer, Mining Pick, Runed Copper Rod...).
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> ToolsByCategory = new();
        static int ToolForCategory(int category) => ToolsByCategory.GetOrAdd(category, key =>
        {
            foreach (ItemTemplate template in Global.ObjectMgr.GetItemTemplates().Values)
                if (template.GetTotemCategory() != 0 && Global.DB2Mgr.IsTotemCategoryCompatibleWith(template.GetTotemCategory(), key) &&
                    Knowledge.VendorsSelling(template.GetId()).Length != 0) return template.GetId();
            return 0;
        });

        SISpawn? SupplyVendor(IEnumerable<int> items)
        {
            var wanted=items.Where(SIProfessionBatchPolicy.VendorAllowed).Distinct().ToHashSet();
            var live=NearbyCreatures(200).Where(c=>c.IsAlive()&&c.HasNpcFlag(NPCFlags1.Vendor)&&!me.IsHostileTo(c)&&!c.IsHostileTo(me)&&
                me.CanSeeOrDetect(c,distanceCheck:false)&&SupplyEntryHasStock(c.GetEntry(),wanted)).Select(SpawnOf);
            var entries = wanted.SelectMany(Knowledge.VendorsSelling).Distinct().Where(entry=>SupplyEntryHasStock(entry,wanted)).ToArray();
            return SIServiceSelection.Nearest(Here,ServiceSpawns(entries,SITaskKind.Vendor,SIVendorPurpose.Supplies).Concat(live)
                .DistinctBy(s=>(s.Map,s.SpawnId,s.Entry,s.Position)),s=>SpawnAvailable(s)&&SocialServiceAllowed(s)&&
                    !Blacklisted(new SITask{Kind=SITaskKind.Vendor,VendorPurpose=SIVendorPurpose.Supplies,Destination=s}.Key))
                .Select(s=>(SISpawn?)s).FirstOrDefault();
        }
        readonly Dictionary<(int Vendor,int Item),long> _supplyVendorRetryAt=new();
        bool SupplyEntryHasStock(int entry,IReadOnlySet<int> items)
        {
            var stock=Global.ObjectMgr.GetNpcVendorItemList(entry);
            for(int slot=0;stock!=null&&slot<stock.GetItemCount();++slot)
            {
                var item=stock.GetItem(slot);
                if(item!=null&&item.Type==ItemVendorType.Item&&item.ExtendedCost==0&&items.Contains(item.item)&&SIProfessionBatchPolicy.VendorAllowed(item.item)&&
                    _supplyVendorRetryAt.GetValueOrDefault((entry,item.item))<=Now)return true;
            }
            return false;
        }

        // ---- Execution ------------------------------------------------------------------------------------

        SIBehaviorResult TickProfessionsBehavior()
        {
            EnsureProfessionKnowledge();
            if (!SIQuestKnowledge.IsBuilt || !SIProfessionKnowledge.IsBuilt) { Status = "Loading profession knowledge"; return SIBehaviorResult.Running; }
            if (TickProfessionTrainingTask()) return SIBehaviorResult.Running;
            if (TickCrafting()) return SIBehaviorResult.Running;
            if(CommittedToJourney()&&_professionStep==SIProfessionStep.Focus&&_professionRecipe!=null)
            {TravelToSpellFocus(_professionRecipe);return SIBehaviorResult.Running;}
            if(_task?.Kind==SITaskKind.Gather&&_professionGatherTargets.Count>0&&_professionGatherTargets.All(p=>me.GetItemCount(p.Key,true)>=p.Value))
            {
                SetTask(null);_professionGatherTargets.Clear();_professionGather=null;_gatherTaskLabel=null;
                _professionStep=SIProfessionStep.None;_nextProfessionPlan=0;
            }
            if(_professionGather!=null&&_task?.Kind==SITaskKind.Gather&&!Blacklisted(_task.Key)&&!_movementFailed&&
                (CommittedToJourney()||_professionGatherTargets.Any(p=>me.GetItemCount(p.Key,true)<p.Value)))
                return TickGather(_professionGather);
            if (Now >= _nextProfessionTrainingScan && PlanProfessionTraining()) return SIBehaviorResult.Running;
            if (TickDisenchant()) return SIBehaviorResult.Running;
            if (Now >= _nextProfessionPlan) PlanProfessionWork();
            Status = _professionSummary;
            if (_professionStep == SIProfessionStep.Gather) return TickGather(_professionGather);
            // Nothing to craft, buy or gather: drop a leftover gathering trip.
            if (!RunProfessionStep(city: false) && _professionStep is SIProfessionStep.None or SIProfessionStep.Capped && _task != null) SetTask(null);
            return SIBehaviorResult.Running;
        }

        // Carries out the planned craft, crafting-station walk, or materials visit (bank, vendor, auction house).
        // City work (the social goal) labels its visits as city errands.
        bool RunProfessionStep(bool city)
        {
            switch (_professionStep)
            {
                case SIProfessionStep.Craft: StartCraft(_professionRecipe); return _craftCastAt != 0;
                case SIProfessionStep.Focus: TravelToSpellFocus(_professionRecipe); return true;
                case SIProfessionStep.Withdraw:
                    StartProfessionVisit(SIVendorPurpose.Withdraw, NearestServiceSpawn(Knowledge.Bankers, SITaskKind.Vendor, SIVendorPurpose.Withdraw),
                        city ? "Collect city crafting materials" : "Collect materials from the bank");
                    return true;
                case SIProfessionStep.Vendor:
                    StartProfessionVisit(SIVendorPurpose.Supplies, SupplyVendor(_professionSupplies.Keys), city ? "Buy city crafting supplies" : "Buy profession supplies");
                    return true;
                case SIProfessionStep.Auction:
                    foreach (int item in _professionShopping.Keys) _auctionTriedAt[item] = Now;
                    _nextMaterialPlan = 0; _nextShoppingScan = 0;
                    StartProfessionVisit(SIVendorPurpose.Auction, _professionAuctioneer, city ? "Buy city crafting materials" : "Buy profession materials at the auction house");
                    return true;
                default: return false;
            }
        }

        bool TickProfessionTrainingTask()
        {
            if (_task?.Kind != SITaskKind.ProfessionTrain) return false;
            _task.Resume(Now);
            if (TaskDone(_task) || VisitNpc(_task) == SIBehaviorResult.Failure)
            {
                SITask resume = _professionReturnTask; _professionReturnTask = null;
                SetTask(resume != null && !TaskDone(resume) ? resume : null); _nextProfessionPlan = 0; _nextReplan = 0;
            }
            return true;
        }

        void StartProfessionVisit(SIVendorPurpose purpose, SISpawn? spawn, string label)
        {
            if(OwnedServiceTrip&&!TaskDone(_task))return;
            _professionStep = SIProfessionStep.None; _nextProfessionPlan = Now + 2000;
            if (spawn == null) { _recipeRetryAt[_professionRecipe?.Spell.Id ?? 0] = Now + 600000; Status = label + ": nobody on this continent"; return; }
            var task = new SITask { Kind = SITaskKind.Vendor, VendorPurpose = purpose, Destination = spawn, Creatures = new[] { spawn.Value.Entry }, Label = label };
            if (Blacklisted(task.Key)) { _recipeRetryAt[_professionRecipe?.Spell.Id ?? 0] = Now + 120000; return; }
            _suspendedTask = null;
            SetTask(task);
        }

        void TravelToSpellFocus(SIRecipe recipe)
        {
            int focus = recipe.Spell.RequiresSpellFocus;
            if (NearSpellFocus(focus)) { ReleaseJourney();_professionFocusDestination=null;_professionStep = SIProfessionStep.Craft; return; }
            if(_professionFocusType!=focus){_professionFocusType=focus;_professionFocusDestination=null;}
            SISpawn? spawn = _professionFocusDestination??=NearestSpawn(Array.Empty<int>(), SIProfessionKnowledge.Instance.SpellFocusObjects(focus), false);
            if (spawn == null) { _recipeRetryAt[recipe.Spell.Id] = Now + 600000; _nextProfessionPlan = 0; return; }
            if (Travel(P(spawn.Value.Position), $"Walk to a crafting station for {recipe.Spell.SpellName[Locale.enUS]}", 3) == SIBehaviorResult.Failure)
            { MarkMissing(spawn.Value, 300000);_professionFocusDestination=null; _nextProfessionPlan = 0; }
            Status = "Walking to a crafting station";
        }

        void StartCraft(SIRecipe recipe)
        {
            if (recipe == null || !HasReagentsInBags(recipe)) { _nextProfessionPlan = 0; return; }
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true) || me.IsMoving() && NavigationPending) return;
            CancelTravel(); StandUp(); Dismount();
            int before = me.GetSkillValue(recipe.Skill);
            _craftReagentsBefore.Clear();
            foreach(int item in recipe.Spell.Reagent.Where(id=>id>0).Distinct())_craftReagentsBefore[item]=me.GetItemCount(item);
            SpellCastResult result = recipe.Enchant && EnchantTarget(recipe) is Item target
                ? me.CastSpell(target, recipe.Spell.Id)
                : me.CastSpell((CastSpellTargetArg)null, recipe.Spell.Id);
            if (result != SpellCastResult.SpellCastOk)
            {
                _recipeRetryAt[recipe.Spell.Id] = Now + 120000; _nextProfessionPlan = 0;
                Report("craft_failed", $"{recipe.Spell.SpellName[Locale.enUS]}: {result}");
                return;
            }
            _craftCastAt = Now;
            _craftRecipe=recipe;
            Status = $"Crafting {recipe.Spell.SpellName[Locale.enUS]} ({SkillName(recipe.Skill)} {before})";
        }

        // Waits for a craft or disenchant to finish, then takes what it left in a loot window.
        bool TickCrafting()
        {
            if (_craftCastAt == 0) return false;
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true) && Now - _craftCastAt < 30000) return true;
            _craftCastAt = 0;
            if(_craftRecipe!=null&&_professionBatchRoot?.Spell.Id==_craftRecipe.Spell.Id&&_craftReagentsBefore.Any(p=>me.GetItemCount(p.Key)<p.Value))
                _professionBatchCraftsLeft=Math.Max(0,_professionBatchCraftsLeft-1);
            _craftRecipe=null;_craftReagentsBefore.Clear();
            me.GetSession().LootOpenViewForSI();
            NoteGameplayProgress();
            _nextProfessionPlan = 0;
            return false;
        }

        // Enchanters turn spare green and blue items into enchanting materials.
        bool TickDisenchant()
        {
            if (!HasProfession(SkillType.Enchanting) || me.IsNonMeleeSpellCast(false, skipAutorepeat: true) || me.IsInCombat()) return false;
            SpellInfo disenchant = KnownSpellWithEffect(SpellEffectName.Disenchant);
            if (disenchant == null) return false;
            Item spare = FirstInventoryItem(item => item.GetQuality() is ItemQuality.Uncommon or ItemQuality.Rare && !item.IsInTrade() &&
                item.GetDisenchantLoot(me) is { } loot && loot.SkillRequired <= me.GetSkillValue(SkillType.Enchanting) &&
                ItemDisposition(item) != SIItemDisposition.Carry && !_storageItemRetryAt.ContainsKey(item.GetGUID()));
            if (spare == null) return false;
            CancelTravel();
            if (me.CastSpell(spare, disenchant.Id) != SpellCastResult.SpellCastOk) { _storageItemRetryAt[spare.GetGUID()] = Now + 600000; return false; }
            _craftCastAt = Now;
            Status = $"Disenchanting {spare.GetTemplate().GetName()}";
            return true;
        }

        readonly Dictionary<SpellEffectName, SpellInfo> _effectSpells = new();
        ulong _effectSpellRevision = ulong.MaxValue;
        SpellInfo KnownSpellWithEffect(SpellEffectName effect, int miscValue = -1)
        {
            if (_effectSpellRevision != me.SpellBook.Revision) { _effectSpells.Clear(); _gatherSpells.Clear(); _effectSpellRevision = me.SpellBook.Revision; }
            if (miscValue < 0 && _effectSpells.TryGetValue(effect, out SpellInfo cached)) return cached;
            SpellInfo found = null;
            foreach (var (spellId, spell) in me.GetSpellMap())
            {
                if (spell.Disabled || spell.State == PlayerSpellState.Removed) continue;
                SpellInfo info = Global.SpellMgr.GetSpellInfo(spellId, Difficulty.None);
                if (info != null && info.GetEffects().Any(e => e.IsEffect(effect) && (miscValue < 0 || e.MiscValue == miscValue))) { found = info; break; }
            }
            if (miscValue < 0) _effectSpells[effect] = found;
            return found;
        }

        // Profession materials and products are kept for crafting and selling later instead of being auctioned
        // on the way, while the bot is working on professions.
        bool KeepsTradeGoods => _goal is "professions" or "gather" or "social";

        // Profession supplies (thread, flux, vials, tools) from the vendor this visit stands at.
        bool BuyProfessionSupplies(Creature vendor)
        {
            FaceTarget(vendor);
            int bought = 0;
            foreach (var (item, count) in _professionSupplies.ToArray())
            {
                if(!SupplyEntryHasStock(vendor.GetEntry(),new HashSet<int>{item}))continue;
                ItemTemplate template = Global.ObjectMgr.GetItemTemplate(item);
                if (template == null) continue;
                int need = count - me.GetItemCount(item);
                int buyCount = Math.Max(1, (int)template.GetBuyCount());
                for (int i = 0; i < 256 && need > 0; ++i)
                {
                    if (template.GetBuyPrice() > SpareMoney()) break;
                    if (!me.GetSession().BuyItemForSI(vendor, item, (byte)buyCount)) break;
                    need -= buyCount; ++bought;
                }
                if (me.GetItemCount(item) >= count) _professionSupplies.Remove(item);
                else _supplyVendorRetryAt[(vendor.GetEntry(),item)]=Now+60000;
            }
            if (bought > 0) { NoteGameplayProgress(); Report("vendor_purchase", $"Bought profession supplies from {vendor.GetName()}"); }
            Status = bought > 0 ? $"Bought profession supplies from {vendor.GetName()}" : $"{vendor.GetName()} has none of the supplies we can afford";
            if (bought==0&&_professionSupplies.Count != 0 && _professionRecipe != null) _recipeRetryAt[_professionRecipe.Spell.Id] = Now + 60000;
            _professionSupplies.Clear(); _nextProfessionPlan = 0;
            return true;
        }

        // Profession materials kept in the bank go back to the bags while the bank is open.
        int WithdrawProfessionMaterials(Creature banker)
        {
            if (_professionWithdraw.Count == 0) return 0;
            var items = new List<Item>();
            me.ForEachItem(ItemSearchLocation.Bank, item => { if (_professionWithdraw.ContainsKey(item.GetEntry())) items.Add(item); return true; });
            int moved = 0;
            foreach (Item item in items)
            {
                int needed=_professionWithdraw[item.GetEntry()]-me.GetItemCount(item.GetEntry());
                if(needed<=0)continue;
                int before=me.GetItemCount(item.GetEntry());
                if (me.GetSession().WithdrawBankItemForSI(banker, item,needed))
                {
                    ++moved;
                    Report("bank_withdraw",$"Withdrew {me.GetItemCount(item.GetEntry())-before}x item {item.GetEntry()} at {banker.GetName()}");
                }
            }
            bool blocked=_professionWithdraw.Any(pair=>me.GetItemCount(pair.Key)<pair.Value);
            if(blocked)
            {
                _bankWithdrawRetryAt=Now+60000;
                if(_professionRecipe!=null&&!HasReagentsInBags(_professionRecipe))_recipeRetryAt[_professionRecipe.Spell.Id]=Now+60000;
                Report("bank_withdraw_blocked","Bank materials could not fit or be moved; delaying further bank visits for one minute.");
            }
            _professionWithdraw.Clear(); _nextProfessionPlan = 0;
            return moved;
        }

        Item SpareBag() => FirstInventoryItem(item => item.IsBag() && !item.IsNotEmptyBag());

        // A purchased bank bag slot (the first tab is the bank itself) that holds no bag yet.
        bool EmptyBankBagSlot()
        {
            byte tabs = me.GetCharacterBankTabCount();
            for (byte slot = (byte)(InventorySlots.BankBagStart + 1); slot < InventorySlots.BankBagStart + tabs && slot < InventorySlots.BankBagEnd; ++slot)
                if (me.GetItemByPos(slot) == null) return true;
            return false;
        }

        // Another bank bag slot helps only with a bag to put in it, and is bought only with gold to spare.
        bool CanExpandBank()
        {
            if (SpareBag() == null) return false;
            if (EmptyBankBagSlot()) return true;
            var tab = NextBankTab(me.GetCharacterBankTabCount());
            return tab != null && (long)tab.Cost <= me.GetMoney() - RequiredRepairCost();
        }

        // A full bank gets the next bank bag slot when the bot can pay for it, and a spare bag to fill it.
        void ExpandBank(Creature banker)
        {
            Item spare = SpareBag();
            bool emptySlot = EmptyBankBagSlot();
            if (!emptySlot && spare != null && me.GetSession().BuyBankTabForSI(banker, RequiredRepairCost()))
            {
                emptySlot = true;
                Report("bank_tab", $"Bought bank slot {me.GetCharacterBankTabCount()}");
            }
            if (emptySlot && spare != null && me.GetSession().BankBagForSI(banker, spare))
                Report("bank_bag", $"Placed {spare.GetTemplate().GetName()} in the bank");
        }

        internal static BankTabRecord NextBankTab(byte count) => count == 0 || count >= Player.MaxCharacterBankTabs ? null :
            CliDB.BankTabStorage.Values.FirstOrDefault(tab => tab.BankType == 0 && tab.OrderIndex == count);
    }
}
