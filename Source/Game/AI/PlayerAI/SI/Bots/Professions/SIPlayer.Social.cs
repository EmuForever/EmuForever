using Framework.Constants;
using Game.Chat;
using Game.DataStorage;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // A crafter's Trade-chat advertisement, which other SI players "read" from here: what it can make, and where.
    sealed record SICraftAd(ObjectGuid Crafter, Team Team, int Map, Vector3 Position, int[] Recipes, long PostedAt);

    enum SIServiceState { Requested, Accepted, MaterialsTraded, Delivering, Done, Failed }

    // One order between two SI players in the same city: the customer brings the materials (first trade), the
    // crafter makes the item or puts the enchantment on the customer's item in a second trade.
    sealed class SIServiceJob
    {
        public ObjectGuid Customer, Crafter;
        public int Spell;
        public ObjectGuid EnchantItem;
        public long Tip;
        public SIServiceState State;
        public long StartedAt, StateAt;
        public void Set(SIServiceState state, long now) { State = state; StateAt = now; }
    }

    static class SISocialBoard
    {
        public static readonly ConcurrentDictionary<ObjectGuid, SICraftAd> Ads = new();
        public static readonly ConcurrentDictionary<ObjectGuid, SIServiceJob> Jobs = new();
        static readonly ConcurrentDictionary<Team, long> LastPost = new();
        public static long Clock => Environment.TickCount64;

        // At most one SI Trade-chat advertisement every 20 seconds per faction.
        public static bool TryReservePost(Team team)
        {
            long now = Clock;
            long last = LastPost.GetOrAdd(team, 0);
            return now - last >= 20000 && LastPost.TryUpdate(team, now, last);
        }
    }

    // The "social" goal: stay in the nearest capital by its bank, advertise professions in Trade chat, make
    // things and enchant for other SI players who bring materials, and order enchantments or crafted gear from
    // other SI crafters (buying the materials first).
    public sealed partial class SIPlayer
    {
        // Stormwind, Ironforge, Darnassus, Orgrimmar, Undercity, Thunder Bluff.
        static readonly int[] CapitalZones = { 1519, 1537, 1657, 1637, 1497, 1638 };
        const long ServiceTimeout = 20 * 60000, TradeStepTimeout = 30000;

        SISpawn? _socialAnchor;
        SISpawn? _socialAuctionAnchor;
        Vector3? _socialWalkDestination;
        bool _socialTravellingToCity;
        int _socialAnchorMap = -1;
        long _nextSocialPost, _nextServiceSearch, _nextSocialWander, _tradeBegunAt;
        bool _tradeOpened;
        Channel _tradeChannel;
        SIServiceJob _customerJob;
        readonly Dictionary<int, long> _serviceRetryAt = new();

        internal bool SocialReady => _goal == "social" && me.IsAlive() && !me.IsInCombat();

        void ResetSocialGoal()
        {
            _socialAnchor = null; _socialAnchorMap = -1; _nextSocialPost = Now + Random.Shared.Next(5000, 60001);
            _socialAuctionAnchor = null; _socialWalkDestination = null; _nextSocialWander = Now + Random.Shared.Next(1000, 3001);
            _socialTravellingToCity = false;
            _nextServiceSearch = Now + Random.Shared.Next(20000, 90001);
            FailCustomerJob(null);
            if (SISocialBoard.Jobs.TryRemove(me.GetGUID(), out var job)) job.Set(SIServiceState.Failed, Now);
            SISocialBoard.Ads.TryRemove(me.GetGUID(), out _);
            if (me.GetTradeData() != null) me.GetSession().CancelTradeForSI();
        }

        SIBehaviorResult TickSocialBehavior()
        {
            EnsureProfessionKnowledge();
            if (!SIQuestKnowledge.IsBuilt || !SIProfessionKnowledge.IsBuilt) { Status = "Loading profession knowledge"; return SIBehaviorResult.Running; }
            if (me.GetTradeData() == null) { _tradeOpened = false; _tradeBegunAt = 0; }
            else if (_tradeBegunAt == 0) _tradeBegunAt = Now;
            if (TickCrafting()) return SIBehaviorResult.Running;
            if (TickProfessionTrainingTask()) return SIBehaviorResult.Running;

            if (_socialAnchor == null || _socialAnchorMap != me.GetMapId())
            {
                _socialAnchorMap = me.GetMapId();
                _socialAnchor = CapitalBanker();
                if (_socialAnchor == null) { Status = "No friendly capital on this continent"; return SIBehaviorResult.Running; }
                int city = SpawnZone(_socialAnchor.Value);
                _socialAuctionAnchor = ServiceSpawns(Knowledge.Auctioneers, SITaskKind.Vendor, SIVendorPurpose.Auction)
                    .Where(s => SpawnZone(s) == city && Vector3.DistanceSquared(s.Position, _socialAnchor.Value.Position) <= 400 * 400)
                    .OrderBy(s => Vector3.DistanceSquared(s.Position, _socialAnchor.Value.Position))
                    .Select(s => (SISpawn?)s).FirstOrDefault();
            }
            Vector3 anchor = _socialAnchor.Value.Position;
            bool busy = SISocialBoard.Jobs.ContainsKey(me.GetGUID()) || _customerJob != null;
            if (!busy && !_socialWalkDestination.HasValue && !SISocialMovement.NearHub(Here, anchor, _socialAuctionAnchor?.Position))
            {
                _socialTravellingToCity = me.GetZoneId() != SpawnZone(_socialAnchor.Value);
                LeaveTradeChannel();
                Travel(P(anchor), "Travel to the city", 6);
                Status = "Travelling to " + ZoneName(_socialAnchor.Value); return SIBehaviorResult.Running;
            }
            _socialTravellingToCity = false;
            JoinTradeChannel();

            if (TickCrafterJob()) return SIBehaviorResult.Running;
            if (TickCustomerJob()) return SIBehaviorResult.Running;
            // nobody we are working with: a stray trade window is closed
            if (me.GetTradeData() != null) me.GetSession().CancelTradeForSI();
            if (Now >= _nextProfessionTrainingScan && PlanProfessionTraining()) return SIBehaviorResult.Running;

            AdvertiseProfessions();
            if (Now >= _nextServiceSearch && RequestService()) return SIBehaviorResult.Running;
            if (TickCityProfessionWork()) return SIBehaviorResult.Running;

            if (!_socialWalkDestination.HasValue && Now >= _nextSocialWander)
            {
                for (int attempt = 0; attempt < 8; ++attempt)
                {
                    var candidate = SISocialMovement.Destination(anchor, _socialAuctionAnchor?.Position,
                        Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
                    if (SocialPointAllowed(candidate)) { _socialWalkDestination = candidate; break; }
                }
                if (!_socialWalkDestination.HasValue) _nextSocialWander = Now + 3000;
            }
            if (_socialWalkDestination is Vector3 stroll)
            {
                var result = Travel(P(stroll), "Walking around the bank and auction house", 2, walk: true,
                    destinationTolerance: 1, heightKnown: false, rememberFailure: false);
                if (result != SIBehaviorResult.Running)
                {
                    _socialWalkDestination = null;
                    _nextSocialWander = Now + Random.Shared.Next(3000, 9001);
                }
                else { Status = "Walking around the bank and auction house"; return SIBehaviorResult.Running; }
            }
            Status = SISocialBoard.Ads.ContainsKey(me.GetGUID()) ? "Advertising professions in Trade chat" : "Hanging out in town";
            return SIBehaviorResult.Running;
        }

        int SocialCity => _socialAnchor.HasValue ? SpawnZone(_socialAnchor.Value) :
            Array.IndexOf(CapitalZones, me.GetZoneId()) >= 0 ? me.GetZoneId() : 0;

        bool SocialPointAllowed(Vector3 point) => _goal != "social" || SocialCity != 0 &&
            Global.TerrainMgr.GetZoneId(me.GetPhaseShift(), me.GetMapId(), point.X, point.Y, point.Z) == SocialCity;

        bool SocialServiceAllowed(SISpawn spawn) => _goal != "social" ||
            SISocialMovement.ServiceAllowed(me.GetZoneId(), SocialCity, SpawnZone(spawn));

        bool SocialRouteAllowed(Vector3[] route) => _goal != "social" || _socialTravellingToCity ||
            SISocialMovement.RouteAllowed(SocialCity, false, route.Select(p => Global.TerrainMgr.GetZoneId(
                me.GetPhaseShift(), me.GetMapId(), p.X, p.Y, p.Z)));

        bool TickCityProfessionWork()
        {
            if (Now >= _nextProfessionPlan) PlanProfessionWork();
            return RunProfessionStep(city: true);
        }

        // ---- City and chat -----------------------------------------------------------------------------------

        SISpawn? CapitalBanker()
        {
            SISpawn? best = null;
            float bestDistance = float.MaxValue;
            foreach (SISpawn spawn in Knowledge.Spawns(Knowledge.Bankers, Array.Empty<int>(), me.GetMapId()))
            {
                if (!FriendlyTemplate(spawn.Entry) || !SpawnAvailable(spawn)) continue;
                int zone = Global.TerrainMgr.GetZoneId(PhasingHandler.EmptyPhaseShift, spawn.Map, spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
                if (Array.IndexOf(CapitalZones, zone) < 0) continue;
                float distance = Distance(spawn.Position);
                if (distance < bestDistance) { bestDistance = distance; best = spawn; }
            }
            return best;
        }

        static string ZoneName(SISpawn spawn)
        {
            int zone = SpawnZone(spawn);
            return CliDB.AreaTableStorage.LookupByKey(zone)?.AreaName[Locale.enUS] ?? "the city";
        }

        static int SpawnZone(SISpawn spawn) => Global.TerrainMgr.GetZoneId(PhasingHandler.EmptyPhaseShift,
            spawn.Map, spawn.Position.X, spawn.Position.Y, spawn.Position.Z);

        static readonly Lazy<int> TradeChannelId = new(() => CliDB.ChatChannelsStorage.Values.FirstOrDefault(channel =>
            channel.Flags.HasFlag(ChatChannelFlags.OnlyInCities) && channel.Flags.HasFlag(ChatChannelFlags.AllowItemLinks))?.Id ?? 2);

        void JoinTradeChannel()
        {
            if (_tradeChannel != null || Array.IndexOf(CapitalZones, me.GetZoneId()) < 0) return;
            AreaTableRecord zone = CliDB.AreaTableStorage.LookupByKey(me.GetZoneId());
            _tradeChannel = ChannelManager.ForTeam(me.GetTeam())?.GetSystemChannel(TradeChannelId.Value, zone);
            _tradeChannel?.JoinChannel(me);
        }

        void LeaveTradeChannel()
        {
            if (_tradeChannel == null) return;
            _tradeChannel.LeaveChannel(me, false);
            _tradeChannel = null;
        }

        void SayInTrade(string text)
        {
            if (_tradeChannel != null) _tradeChannel.Say(me.GetGUID(), text, Language.Universal);
            else me.Say(text, Language.Universal);
        }

        static string SpellLink(SpellInfo spell) => $"|cff71d5ff|Hspell:{spell.Id}:0|h[{spell.SpellName[Locale.enUS]}]|h|r";

        // Crafting professions and the recipes behind them, for the advertisement.
        List<SIRecipe> ServiceRecipes() => KnownProfessions().Where(skill => !IsGathering(skill) && skill != SIProfessionKnowledge.FirstAid)
            .SelectMany(KnownRecipes).Where(recipe => recipe.Enchant || recipe.Product != 0).ToList();

        void AdvertiseProfessions()
        {
            if (Now < _nextSocialPost) return;
            var recipes = ServiceRecipes();
            if (recipes.Count == 0) { _nextSocialPost = Now + 600000; return; }
            if (!SISocialBoard.TryReservePost(me.GetTeam())) { _nextSocialPost = Now + Random.Shared.Next(5000, 20001); return; }
            _nextSocialPost = Now + Random.Shared.Next(240000, 480001);
            SISocialBoard.Ads[me.GetGUID()] = new SICraftAd(me.GetGUID(), me.GetTeam(), me.GetMapId(), Here,
                recipes.Select(recipe => recipe.Spell.Id).Take(400).ToArray(), SISocialBoard.Clock);
            var skills = recipes.Select(recipe => recipe.Skill).Distinct().Select(skill => $"{SkillName(skill)} {me.GetSkillValue(skill)}");
            SIRecipe best = recipes.OrderByDescending(recipe => recipe.Ability.MinSkillLineRank).First();
            SayInTrade($"{string.Join(" / ", skills)} - LFW! Can do {SpellLink(best.Spell)} and more. Bring your mats, tips welcome.");
        }

        // ---- Customer side ---------------------------------------------------------------------------------

        // Picks something another SI crafter here advertised that the bot wants: an enchantment for unenchanted
        // gear, or a crafted upgrade it can wear (bind-on-pickup products cannot be handed over).
        bool RequestService()
        {
            _nextServiceSearch = Now + Random.Shared.Next(60000, 180001);
            if (_customerJob != null || me.GetFreeInventorySlotCount() < 2) return false;
            PruneExpired(_serviceRetryAt);
            long fresh = SISocialBoard.Clock - 15 * 60000;
            (SICraftAd Ad, SpellInfo Spell, Item Target, float Gain)? best = null;
            foreach (SICraftAd ad in SISocialBoard.Ads.Values)
            {
                if (ad.Crafter == me.GetGUID() || ad.Team != me.GetTeam() || ad.Map != me.GetMapId() || !SocialPointAllowed(ad.Position) || ad.PostedAt < fresh ||
                    SISocialBoard.Jobs.ContainsKey(ad.Crafter) || Vector3.Distance(ad.Position, Here) > 150) continue;
                if (Global.ObjAccessor.GetPlayer(me, ad.Crafter) is not Player crafter || crafter.Controller?.SocialReady != true) continue;
                foreach (int id in ad.Recipes)
                {
                    if (_serviceRetryAt.ContainsKey(id)) continue;
                    SpellInfo spell = Global.SpellMgr.GetSpellInfo(id, Difficulty.None);
                    if (spell == null) continue;
                    if (spell.HasEffect(SpellEffectName.EnchantItem))
                    {
                        for (byte slot = EquipmentSlot.Start; slot < EquipmentSlot.End; ++slot)
                        {
                            Item item = me.GetItemByPos(slot);
                            if (item == null || item.GetEnchantmentId(EnchantmentSlot.EnhancementPermanent) != 0 || !item.IsFitToSpellRequirements(spell)) continue;
                            float gain = 5 + Global.SpellMgr.GetSkillLineAbilityMapBounds(id).Select(a => (int)a.MinSkillLineRank).DefaultIfEmpty(0).Max() / 10f;
                            if (best == null || gain > best.Value.Gain) best = (ad, spell, item, gain);
                        }
                        continue;
                    }
                    int product = spell.GetEffects().FirstOrDefault(effect => effect.IsEffect(SpellEffectName.CreateItem))?.ItemType ?? 0;
                    ItemTemplate template = product != 0 ? Global.ObjectMgr.GetItemTemplate(product) : null;
                    if (template == null || template.GetBonding() == ItemBondingType.OnAcquire || SlotsFor(template.GetInventoryType()).Length == 0 ||
                        me.CanUseItem(template) != InventoryResult.Ok) continue;
                    float upgrade = ItemScore(template) - EquippedScore(template);
                    if (upgrade > 3 && (best == null || upgrade > best.Value.Gain)) best = (ad, spell, null, upgrade);
                }
            }
            if (best is not { } choice) return false;
            var job = new SIServiceJob { Customer = me.GetGUID(), Crafter = choice.Ad.Crafter, Spell = choice.Spell.Id,
                EnchantItem = choice.Target?.GetGUID() ?? ObjectGuid.Empty, Tip = Math.Min(me.GetMoney() / 20, 10000), StartedAt = Now };
            job.Set(SIServiceState.Requested, Now);
            if (!SISocialBoard.Jobs.TryAdd(choice.Ad.Crafter, job)) return false;
            _customerJob = job;
            string crafterName = Global.ObjAccessor.GetPlayer(me, choice.Ad.Crafter)?.GetName() ?? "anyone";
            SayInTrade($"{crafterName}, could you do {SpellLink(choice.Spell)} for me? I'll get the mats together.");
            Report("service_request", $"Asked {crafterName} for {choice.Spell.SpellName[Locale.enUS]}");
            return true;
        }

        void FailCustomerJob(string reason)
        {
            if (_customerJob == null) return;
            if (reason != null) { Report("service_failed", reason); _serviceRetryAt[_customerJob.Spell] = Now + 3600000; }
            _customerJob.Set(SIServiceState.Failed, Now);
            SISocialBoard.Jobs.TryRemove(new KeyValuePair<ObjectGuid, SIServiceJob>(_customerJob.Crafter, _customerJob));
            _customerJob = null;
            if (me.GetTradeData() != null) me.GetSession().CancelTradeForSI();
        }

        bool TickCustomerJob()
        {
            SIServiceJob job = _customerJob;
            if (job == null) return false;
            Player crafter = Global.ObjAccessor.GetPlayer(me, job.Crafter);
            SpellInfo spell = Global.SpellMgr.GetSpellInfo(job.Spell, Difficulty.None);
            if (job.State == SIServiceState.Done)
            {
                _customerJob = null;
                if (crafter != null) me.Say($"Thanks, {crafter.GetName()}!", Language.Universal);
                _nextEquipCheck = 0; EquipUpgrades(); NoteGameplayProgress();
                Report("service_done", $"{spell?.SpellName[Locale.enUS]} from {crafter?.GetName()}");
                Status = "Order delivered"; return false;
            }
            if (job.State == SIServiceState.Failed) { _serviceRetryAt[job.Spell] = Now + 3600000; _customerJob = null; return false; }
            if (crafter == null || crafter.Controller?.SocialReady != true || spell == null || !SocialPointAllowed(V(crafter.GetPosition()))) { FailCustomerJob("The crafter left the city."); return false; }
            if (Now - job.StartedAt > ServiceTimeout) { FailCustomerJob("The order took too long."); return false; }

            if (job.State is SIServiceState.Requested or SIServiceState.Accepted)
            {
                if (me.GetTradeData() == null && !ObtainServiceMaterials(spell)) return true;
                if (job.State == SIServiceState.Requested) { Status = $"Waiting for {crafter.GetName()} to get ready"; return true; }
                if (!ApproachForTrade(crafter)) return true;
                return TickMaterialTrade(job, crafter, spell);
            }
            if (!ApproachForTrade(crafter)) return true;
            // second trade: the crafter proposes it and offers the product or the enchantment
            TradeData trade = me.GetTradeData();
            if (trade == null) { Status = $"Waiting for {crafter.GetName()} to finish"; return true; }
            if (trade.GetTrader() != crafter || job.State != SIServiceState.Delivering) { me.GetSession().CancelTradeForSI(); return true; }
            BeginTradeOnce();
            if (!job.EnchantItem.IsEmpty() && trade.GetItem(TradeSlots.NonTraded) == null)
            {
                Item target = me.GetItemByGuid(job.EnchantItem);
                if (target == null) { FailCustomerJob("The item to enchant is gone."); return false; }
                me.GetSession().PutTradeItemForSI(target, TradeSlots.NonTraded);
                Status = $"Showing {target.GetTemplate().GetName()} to {crafter.GetName()}"; return true;
            }
            TradeData offer = crafter.GetTradeData();
            bool delivered = job.EnchantItem.IsEmpty() ? Enumerable.Range(0, (int)TradeSlots.TradedCount).Any(slot => offer?.GetItem((TradeSlots)slot) != null)
                : offer?.GetSpell() == job.Spell;
            if (delivered && !trade.IsAccepted() && AcceptCompletesTrade()) job.Set(SIServiceState.Done, Now);
            Status = $"Trading with {crafter.GetName()}";
            return true;
        }

        // First trade: the materials and a tip go to the crafter.
        bool TickMaterialTrade(SIServiceJob job, Player crafter, SpellInfo spell)
        {
            TradeData trade = me.GetTradeData();
            if (trade == null)
            {
                if (crafter.GetTradeData() != null) { Status = $"{crafter.GetName()} is busy trading"; return true; }
                if (!me.GetSession().ProposeTradeForSI(crafter)) { Status = $"Could not open a trade with {crafter.GetName()}"; return true; }
                _tradeOpened = true; _tradeBegunAt = Now;
                return true;
            }
            if (trade.GetTrader() != crafter) { me.GetSession().CancelTradeForSI(); return true; }
            if (Now - _tradeBegunAt > TradeStepTimeout) { me.GetSession().CancelTradeForSI(); return true; }
            if (!trade.IsAccepted())
            {
                byte slot = 0;
                for (int i = 0; i < spell.Reagent.Length; ++i)
                {
                    if (spell.Reagent[i] <= 0) continue;
                    int left = spell.ReagentCount[i];
                    foreach (Item stack in me.GetItemListByEntry(spell.Reagent[i]))
                    {
                        if (left <= 0) break;
                        if (slot >= (byte)TradeSlots.TradedCount) { FailCustomerJob("Too many material stacks for one trade."); return false; }
                        if (!trade.HasItem(stack.GetGUID()) && !me.GetSession().PutTradeItemForSI(stack, (TradeSlots)slot)) { me.GetSession().CancelTradeForSI(); return true; }
                        left -= stack.GetCount(); ++slot;
                    }
                }
                if (job.Tip > 0 && trade.GetMoney() != job.Tip) me.GetSession().SetTradeMoneyForSI(Math.Min(job.Tip, me.GetMoney()));
                if (AcceptCompletesTrade()) { job.Set(SIServiceState.MaterialsTraded, Now); Status = $"Gave the materials to {crafter.GetName()}"; return true; }
            }
            Status = $"Handing materials to {crafter.GetName()}";
            return true;
        }

        // Gets the order's materials into the bags: from the bank, a vendor, or the auction house.
        bool ObtainServiceMaterials(SpellInfo spell)
        {
            if (HasReagentsInBags(spell)) return true;
            if (_task?.Kind == SITaskKind.Vendor) return false;
            _professionSupplies.Clear(); _professionWithdraw.Clear();
            var auction = new List<int>();
            for (int i = 0; i < spell.Reagent.Length; ++i)
            {
                int item = spell.Reagent[i], need = spell.ReagentCount[i];
                if (item <= 0 || me.GetItemCount(item) >= need) continue;
                if (me.GetItemCount(item, true) >= need)
                {
                    if(Now<_bankWithdrawRetryAt){Status="Waiting for bag space before collecting bank materials";return false;}
                    _professionWithdraw[item]=need;
                }
                else if (Knowledge.VendorsSelling(item).Length != 0) _professionSupplies[item] = need;
                else if (SIAuctionShoppingPolicy.MayScanMaterial(Now, _auctionTriedAt.TryGetValue(item, out long last) ? last : null) && ShoppingAllowance() > 0) auction.Add(item);
                else { FailCustomerJob($"Could not get {ItemName(item)} for {spell.SpellName[Locale.enUS]}."); return false; }
            }
            if (_professionWithdraw.Count != 0)
                StartProfessionVisit(SIVendorPurpose.Withdraw, NearestServiceSpawn(Knowledge.Bankers, SITaskKind.Vendor, SIVendorPurpose.Withdraw), "Collect materials from the bank");
            else if (_professionSupplies.Count != 0)
                StartProfessionVisit(SIVendorPurpose.Supplies, SupplyVendor(_professionSupplies.Keys), "Buy materials for an order");
            else if (auction.Count != 0)
            {
                foreach (int item in auction)
                {
                    int need = spell.ReagentCount[Array.IndexOf(spell.Reagent, item)];
                    _professionShopping[item] = me.GetItemCount(item, true) + need - me.GetItemCount(item);
                    _auctionTriedAt[item] = Now;
                }
                _nextMaterialPlan = 0; _nextShoppingScan = 0;
                StartProfessionVisit(SIVendorPurpose.Auction, NearestServiceSpawn(Knowledge.Auctioneers, SITaskKind.Vendor, SIVendorPurpose.Auction), "Buy materials for an order");
            }
            Status = $"Getting materials for {spell.SpellName[Locale.enUS]}";
            return false;
        }

        bool ApproachForTrade(Player other)
        {
            if (me.GetExactDist(other) <= 4) return true;
            if (MoveWithin(other, 3, "Meet " + other.GetName()) == SIBehaviorResult.Failure) FailCustomerJob("Could not reach the crafter.");
            Status = "Walking over to " + other.GetName();
            return false;
        }

        // Opens a trade the partner proposed (the proposer's own window is already open).
        void BeginTradeOnce()
        {
            if (_tradeOpened || me.GetTradeData() == null) return;
            _tradeOpened = true;
            me.GetSession().BeginTradeForSI();
        }

        // Accepts, and reports whether that completed the trade (both sides had accepted).
        bool AcceptCompletesTrade()
        {
            me.GetSession().AcceptTradeForSI();
            return me.GetTradeData() == null;
        }

        // ---- Crafter side ----------------------------------------------------------------------------------

        bool TickCrafterJob()
        {
            if (!SISocialBoard.Jobs.TryGetValue(me.GetGUID(), out SIServiceJob job)) return false;
            Player customer = Global.ObjAccessor.GetPlayer(me, job.Customer);
            SIRecipe recipe = ServiceRecipes().FirstOrDefault(r => r.Spell.Id == job.Spell);
            if (job.State is SIServiceState.Done or SIServiceState.Failed || customer == null || recipe == null || Now - job.StartedAt > ServiceTimeout)
            {
                if (job.State == SIServiceState.Done)
                {
                    NoteGameplayProgress();
                    Report("service_delivered", $"{recipe?.Spell.SpellName[Locale.enUS]} for {customer?.GetName()}");
                }
                else job.Set(SIServiceState.Failed, Now);
                // the customer keeps its own reference and sees the final state
                SISocialBoard.Jobs.TryRemove(new KeyValuePair<ObjectGuid, SIServiceJob>(me.GetGUID(), job));
                if (me.GetTradeData() != null) me.GetSession().CancelTradeForSI();
                return false;
            }
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) { Status = $"Working on {customer.GetName()}'s order"; return true; }
            switch (job.State)
            {
                case SIServiceState.Requested:
                    // room for the materials first: bank what can go, then say yes
                    int stacks = recipe.Spell.Reagent.Count(item => item > 0) + 1;
                    if (me.GetFreeInventorySlotCount() < stacks)
                    {
                        SISpawn? banker = NearestServiceSpawn(Knowledge.Bankers, SITaskKind.Vendor, SIVendorPurpose.Bank);
                        if (banker != null && StorageItems(SIVendorPurpose.Bank).Count != 0)
                        { StartProfessionVisit(SIVendorPurpose.Bank, banker, "Make room for an order"); Status = "Banking to make room for an order"; return true; }
                        job.Set(SIServiceState.Failed, Now);
                        me.Say($"Sorry {customer.GetName()}, my bags are full.", Language.Universal);
                        return true;
                    }
                    job.Set(SIServiceState.Accepted, Now);
                    me.Say($"Sure {customer.GetName()}, bring the mats over.", Language.Universal);
                    return true;
                case SIServiceState.Accepted:
                {
                    // the customer proposes the first trade; take the materials once they are all on offer
                    TradeData trade = me.GetTradeData();
                    if (trade == null) { Status = $"Waiting for {customer.GetName()} to bring materials"; return true; }
                    if (trade.GetTrader() != customer) { me.GetSession().CancelTradeForSI(); return true; }
                    BeginTradeOnce();
                    TradeData offer = customer.GetTradeData();
                    if (offer != null && offer.IsAccepted() && !trade.IsAccepted() && OfferHasReagents(offer, recipe.Spell) && AcceptCompletesTrade())
                        job.Set(SIServiceState.MaterialsTraded, Now);
                    Status = $"Trading materials with {customer.GetName()}";
                    return true;
                }
                case SIServiceState.MaterialsTraded:
                    if (!HasReagentsInBags(recipe)) { job.Set(SIServiceState.Failed, Now); return true; }
                    if (!recipe.Enchant)
                    {
                        if (recipe.Spell.RequiresSpellFocus != 0 && !NearSpellFocus(recipe.Spell.RequiresSpellFocus))
                        {
                            SISpawn? focus = NearestSpawn(Array.Empty<int>(), SIProfessionKnowledge.Instance.SpellFocusObjects(recipe.Spell.RequiresSpellFocus), false);
                            if (focus == null || Distance(focus.Value.Position) > 200) { job.Set(SIServiceState.Failed, Now); return true; }
                            Travel(P(focus.Value.Position), "Walk to a crafting station", 3);
                            Status = "Walking to a crafting station for an order"; return true;
                        }
                        StartCraft(recipe);
                        if (_craftCastAt == 0) { job.Set(SIServiceState.Failed, Now); return true; }
                    }
                    job.Set(SIServiceState.Delivering, Now);
                    return true;
                case SIServiceState.Delivering:
                {
                    if (me.GetExactDist(customer) > 4)
                    {
                        MoveWithin(customer, 3, "Deliver an order");
                        Status = "Bringing the order to " + customer.GetName(); return true;
                    }
                    TradeData trade = me.GetTradeData();
                    if (trade == null)
                    {
                        if (Now - job.StateAt > ServiceTimeout / 2) { job.Set(SIServiceState.Failed, Now); return true; }
                        if (customer.GetTradeData() == null && me.GetSession().ProposeTradeForSI(customer)) { _tradeOpened = true; _tradeBegunAt = Now; }
                        return true;
                    }
                    if (trade.GetTrader() != customer) { me.GetSession().CancelTradeForSI(); return true; }
                    if (Now - _tradeBegunAt > TradeStepTimeout) { me.GetSession().CancelTradeForSI(); return true; }
                    if (!recipe.Enchant)
                    {
                        Item product = me.GetItemListByEntry(recipe.Product).FirstOrDefault(item => !item.IsInTrade() && item.CanBeTraded(false, true));
                        if (trade.GetItem((TradeSlots)0) == null)
                        {
                            if (product == null) { job.Set(SIServiceState.Failed, Now); return true; }
                            me.GetSession().PutTradeItemForSI(product, (TradeSlots)0);
                        }
                    }
                    else if (trade.GetSpell() == 0)
                    {
                        if (customer.GetTradeData()?.GetItem(TradeSlots.NonTraded) == null) { Status = $"Waiting for {customer.GetName()} to show the item"; return true; }
                        SpellCastResult cast = me.GetSession().CastTradeSpellForSI(recipe.Spell.Id);
                        if (cast != SpellCastResult.SpellCastOk && cast != SpellCastResult.DontReport) { Report("service_enchant_failed", cast.ToString()); job.Set(SIServiceState.Failed, Now); }
                        Status = $"Enchanting {customer.GetName()}'s item";
                        return true;
                    }
                    // the customer accepts first once the offer is up; ours completes the trade
                    if (customer.GetTradeData()?.IsAccepted() == true && !trade.IsAccepted() && AcceptCompletesTrade()) job.Set(SIServiceState.Done, Now);
                    Status = $"Delivering {recipe.Spell.SpellName[Locale.enUS]} to {customer.GetName()}";
                    return true;
                }
            }
            return false;
        }

        static bool OfferHasReagents(TradeData offer, SpellInfo spell)
        {
            for (int i = 0; i < spell.Reagent.Length; ++i)
            {
                if (spell.Reagent[i] <= 0) continue;
                int count = 0;
                for (int slot = 0; slot < (int)TradeSlots.TradedCount; ++slot)
                    if (offer.GetItem((TradeSlots)slot) is Item item && item.GetEntry() == spell.Reagent[i]) count += item.GetCount();
                if (count < spell.ReagentCount[i]) return false;
            }
            return true;
        }
    }
}
