using Framework.Constants;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    // First Aid for every SI player, whatever its goal: learn it and its ranks from a nearby trainer, keep about
    // twenty bandages made from the cloth it picks up, and bandage before eating (Survival).
    public sealed partial class SIPlayer
    {
        const int BandageTarget = 20, BandageRestockBelow = 15;
        const int RecentlyBandaged = 11196;
        long _nextBandageCraft;
        bool _restockingBandages;
        ulong _bandageClothRevision = ulong.MaxValue;
        readonly HashSet<int> _bandageCloth = new();

        // Upkeep the main loop runs for every working goal, after vendors and class trainers.
        bool TickFirstAidUpkeep()
        {
            if(CommittedToJourney()||_goal=="professions"&&_professionBatchRoot!=null)return false;
            if (_goal == "social" && (SocialCity == 0 || me.GetZoneId() != SocialCity)) return false;
            if (!SIProfessionKnowledge.IsBuilt) { EnsureProfessionKnowledge(); return false; }
            // spread the first trainer scan of a few hundred bots over five minutes
            if (_nextProfessionTrainingScan == 0 && !_professionTrainingProgressDue && _goal is "quest" or "grind") _nextProfessionTrainingScan = Now + System.Random.Shared.Next(10000, 300001);
            if (TickProfessionTrainingTask()) { Behavior = "Trainer"; return true; }
            if (TickCrafting()) { Behavior = "Crafting"; return true; }
            if(_professionTrainingProgressDue&&(_goal is "gather" or "professions"))return false;
            if (Now >= _nextBandageCraft && CraftBandage()) { Behavior = "Crafting"; return true; }
            // questing and grinding take a trainer detour of up to 800 yards; other goals plan their own trips
            if (_goal is "quest" or "grind" && Now >= _nextProfessionTrainingScan && !CommittedToObjective() &&
                PlanProfessionTraining(800)) { Behavior = "Trainer"; return true; }
            return false;
        }

        List<SIRecipe> BandageRecipes() => HasProfession(SIProfessionKnowledge.FirstAid)
            ? KnownRecipes(SIProfessionKnowledge.FirstAid).Where(recipe => recipe.Product != 0).ToList() : new List<SIRecipe>();


        // Makes the best bandage it has the cloth for, starting below 15 and going on to 20.
        bool CraftBandage()
        {
            _nextBandageCraft = Now + 5000;
            if (me.IsInCombat() || me.IsNonMeleeSpellCast(false, skipAutorepeat: true) || !me.IsAlive()) return false;
            var recipes = BandageRecipes();
            if (recipes.Count == 0) return false;
            int count = recipes.Select(recipe => recipe.Product).Distinct().Sum(item => me.GetItemCount(item));
            if (count < BandageRestockBelow) _restockingBandages = true;
            if (count >= BandageTarget) _restockingBandages = false;
            bool levelling = _goal == "social" && CanAdvanceKnownRecipes(SIProfessionKnowledge.FirstAid);
            if ((!_restockingBandages && !levelling) || me.GetFreeInventorySlotCount() == 0) return false;
            SIRecipe best = recipes.Where(recipe => !_recipeRetryAt.ContainsKey(recipe.Spell.Id) && HasReagentsInBags(recipe) &&
                    (!levelling || RecipeColor(recipe) < 3) && Global.ObjectMgr.GetItemTemplate(recipe.Product) is { } template && me.CanUseItem(template) == InventoryResult.Ok)
                .OrderBy(RecipeColor).ThenByDescending(recipe => recipe.Ability.MinSkillLineRank).FirstOrDefault();
            if (best == null) return false;
            StartCraft(best);
            if (_craftCastAt == 0) return false;
            _nextBandageCraft = Now + 500;
            Status = $"Making bandages ({count}/{BandageTarget})";
            return true;
        }

        // Cloth for the two best bandages it knows stays in the bags instead of going to a vendor, bank or the AH.
        bool IsBandageCloth(int item)
        {
            if (_bandageClothRevision != me.SpellBook.Revision)
            {
                _bandageClothRevision = me.SpellBook.Revision;
                _bandageCloth.Clear();
                foreach (SIRecipe recipe in BandageRecipes().OrderByDescending(recipe => recipe.Ability.MinSkillLineRank).Take(2))
                    foreach (int reagent in recipe.Spell.Reagent)
                        if (reagent > 0) _bandageCloth.Add(reagent);
            }
            return _bandageCloth.Contains(item);
        }
    }
}
