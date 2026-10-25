using Framework.Configuration;
using Framework.Constants;
using Framework.Database;
using Framework.IO;
using Game.Entities;
using Game.Networking.Packets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Game.Chat.Commands
{
    // Server-controlled player bots. Each bot has its own game account named "SI_<hex>".
    [CommandGroup("si")]
    partial class SICommands
    {
        const RBACPermissions Permission = RBACPermissions.RoleModerator;
        const string AccountPrefix = "SI_";
        static string NewBotAccountName() => AccountPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

        static (string First, string Surname) ParseFullName(string fullName)
        {
            string[] words = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 1 or > 2 || !TryName(words[0], 2, 12, out string first))
                throw new ArgumentException("Use \"first surname\"; first name must have 2-12 letters.");
            string surname = "";
            if (words.Length == 2 && !TryName(words[1], 1, 32, out surname))
                throw new ArgumentException("Surname must have 1-32 letters.");
            return (first, surname);
        }
        static readonly ConcurrentDictionary<string, byte> Creating = new(StringComparer.OrdinalIgnoreCase);
        static void ReportCreationFailure(CommandHandler handler, string message)
        {
            Log.outError(LogFilter.Server, message);
            handler.SendSysMessage(message);
        }
        static bool IsOperator(CommandHandler h) => h.GetSession() == null || h.GetSession().GetSecurity() >= AccountTypes.Moderator;
        static bool Enabled(CommandHandler h)
        {
            if (!IsOperator(h)) return false;
            if (ConfigMgr.GetDefaultValue("SI.Enabled", true)) return true;
            h.SendSysMessage("SI is disabled. Set SI.Enabled = 1 and restart worldserver.");
            return false;
        }
        static bool Run(CommandHandler h, Action action)
        {
            if (!IsOperator(h)) return false;
            try { action(); return true; }
            catch (Exception e) { h.SendSysMessage("SI: " + e.Message); return false; }
        }
        static WorldSession NewSession(int account)
        {
            if (Global.WorldMgr.FindSession(account) != null)
                throw new InvalidOperationException($"Account {unchecked((uint)account)} is already online. Log it out before spawning SI.");
            PreparedStatement query = new("SELECT username,COALESCE(battlenet_account,0),expansion FROM account WHERE id=@0");
            query.SetUInt32(0, unchecked((uint)account));
            SQLResult result = DB.Login.Query(query);
            if (result.IsEmpty()) throw new InvalidOperationException($"The linked game account {unchecked((uint)account)} does not exist.");
            string name = result.Read<string>(0);
            int bnet = result.Read<int>(1);
            Expansion expansion = (Expansion)result.Read<byte>(2);
            result.Close();
            return new WorldSession(account, name, bnet, null, AccountTypes.Player, expansion,
                ServerTime.Zero, "SI", TimeSpan.Zero, Locale.enUS, 0, false, serverControlled: true);
        }

        static bool TryName(string value, int min, int max, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(value) || value.Length < min || value.Length > max || value.Any(c => !char.IsAsciiLetter(c)))
                return false;
            name = char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
            return true;
        }

        // Game accounts that belong to bots.
        static Dictionary<int, string> BotAccounts()
        {
            var accounts = new Dictionary<int, string>();
            PreparedStatement query = new("SELECT id,username FROM account WHERE username LIKE @0");
            query.SetString(0, AccountPrefix.Replace("_", "\\_") + "%");
            SQLResult rows = DB.Login.Query(query);
            if (!rows.IsEmpty())
                do { accounts[unchecked((int)rows.Read<uint>(0))] = rows.Read<string>(1); } while (rows.NextRow());
            rows.Close();
            return accounts;
        }

        // Account-wide data (appearances, toys, mounts, battle pets) is keyed by the Battle.net account, so every bot
        // game account needs one, as a normal player's does. Linked synchronously: the session reads it on creation.
        static void EnsureBattlenetAccount(int account)
        {
            PreparedStatement query = new("SELECT username,COALESCE(battlenet_account,0) FROM account WHERE id=@0");
            query.SetUInt32(0, unchecked((uint)account));
            SQLResult row = DB.Login.Query(query);
            if (row.IsEmpty()) throw new InvalidOperationException($"Game account {unchecked((uint)account)} does not exist.");
            string accountName = row.Read<string>(0);
            int linked = unchecked((int)row.Read<uint>(1));
            row.Close();
            if (linked != 0) return;
            string email = accountName.ToLowerInvariant() + "@si.bot";
            int bnet = Global.BNetAccountMgr.GetId(email);
            if (bnet == 0)
            {
                var created = Global.BNetAccountMgr.CreateBattlenetAccount(email, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), false, out _);
                if (created != AccountOpResult.Ok) throw new InvalidOperationException($"Could not create the bot's Battle.net account: {created}.");
                bnet = Global.BNetAccountMgr.GetId(email);
            }
            if (bnet == 0) throw new InvalidOperationException("The bot's Battle.net account could not be loaded.");
            // Battle pet tables store the id as a signed int; an id past 2147483647 cannot be saved.
            if (bnet < 0)
                throw new InvalidOperationException($"auth.bnot_accounts handed out id {unchecked((uint)bnet)}, above the signed range. Lower its AUTO_INCREMENT and retry.");
            PreparedStatement link = new("UPDATE account SET battlenet_account=@0, battlenet_index=1 WHERE id=@1");
            link.SetUInt32(0, unchecked((uint)bnet));
            link.SetUInt32(1, unchecked((uint)account));
            if (!DB.Login.DirectExecute(link)) throw new InvalidOperationException("Could not link the bot's Battle.net account.");
        }

        static bool IsBotAccount(int account)
        {
            PreparedStatement query = new("SELECT username FROM account WHERE id=@0");
            query.SetUInt32(0, unchecked((uint)account));
            SQLResult row = DB.Login.Query(query);
            bool bot = !row.IsEmpty() && row.Read<string>(0).StartsWith(AccountPrefix, StringComparison.OrdinalIgnoreCase);
            row.Close();
            return bot;
        }

        [Command("create", Permission, true)]
        static bool Create(CommandHandler h, StringArguments arguments)
        {
            if (!Enabled(h)) return false;
            return Run(h, () =>
            {
                var words = ParseArguments(arguments.GetString());
                if (words.Count >= 1 && words[0].Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    QueueRandomCreation(h, words);
                    return;
                }
                if (words.Count != 3) throw new ArgumentException("Use .si create random <horde|alliance> [count], or .si create \"first surname\" <race> <class>.");
                CreateNamed(h, words[0], words[1], words[2]);
            });
        }

        static void CreateNamed(CommandHandler h, string fullName, string race, string playerClass,
            Gender? gender = null, Action<bool> completed = null)
        {
            var (name, last) = ParseFullName(fullName);
            race = race.Replace("_", "").Replace("-", "");
            playerClass = playerClass.Replace("_", "").Replace("-", "");
            if (!Enum.TryParse(race, true, out Race raceId) || !Enum.IsDefined(raceId) || raceId == Race.None
                || !Enum.TryParse(playerClass, true, out Class classId) || !Enum.IsDefined(classId) || classId == Class.None
                || Global.ObjectMgr.GetPlayerInfo(raceId, classId) == null)
                throw new ArgumentException("That race/class combination is not supported by this world.");
            string displayName = (name + " " + last).Trim();
            if (Global.CharacterCacheStorage.GetCharacterCacheByFirstName(name) != null)
                throw new InvalidOperationException("That first name is already taken. Choose another first name.");
            if (!Creating.TryAdd(name, 0)) throw new InvalidOperationException("Creation is already in progress for this name.");
            byte expansionValue = (byte)WorldConfig.Values[WorldCfg.Expansion].Int32;
            Task<int> preparation = Task.Run(() =>
            {
                string accountName = NewBotAccountName();
                var created = Global.AccountMgr.CreateAccount(accountName, Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)));
                if (created != AccountOpResult.Ok)
                    throw new InvalidOperationException($"Could not create the bot account: {created}.");
                int account = Global.AccountMgr.GetId(accountName);
                if (account == 0) throw new InvalidOperationException("The bot account could not be loaded.");
                PreparedStatement expansion = new("UPDATE account SET expansion=@0 WHERE id=@1");
                expansion.SetUInt8(0, expansionValue);
                expansion.SetUInt32(1, unchecked((uint)account));
                if (!DB.Login.DirectExecute(expansion)) throw new InvalidOperationException("Could not set the bot account expansion.");
                EnsureBattlenetAccount(account);
                return account;
            });
            Work.Enqueue(() =>
            {
                if (!preparation.IsCompleted) return false;
                bool queued = false;
                try
                {
                    int account = preparation.GetAwaiter().GetResult();
                    WorldSession session = NewSession(account);
                    CharacterCreateInfo info = new() { Name = name, Surname = last, RaceId = raceId, ClassId = classId,
                        Sex = gender ?? (RandomNumberGenerator.GetInt32(2) == 0 ? Gender.Male : Gender.Female) };
                    if (session.TrySelectSIAppearance(raceId, classId, info.Sex, new(), out var appearance))
                        foreach (var choice in appearance) info.Customizations.Add(choice);
                    session.SetSICreation(info, (result, guid) =>
                    {
                        try
                        {
                            if (result != ResponseCodes.CharCreateSuccess)
                                ReportCreationFailure(h, $"SI creation {displayName}: {result} ({(int)result}).");
                            else
                                h.SendSysMessage($"Created SI player {displayName}: character {guid.GetCounter()}, account {unchecked((uint)account)}. Use .si spawn {name}.");
                        }
                        finally { Creating.TryRemove(name, out _); completed?.Invoke(result == ResponseCodes.CharCreateSuccess); }
                    });
                    Global.WorldMgr.AddSession(session);
                    queued = true;
                }
                catch (Exception error) { ReportCreationFailure(h, $"SI creation {displayName} failed: {error.GetBaseException().Message}"); }
                finally
                {
                    if (!queued) { Creating.TryRemove(name, out _); completed?.Invoke(false); }
                }
                return true;
            });
            h.SendSysMessage($"Creating SI player {displayName}.");
        }

        [Command("spawn", Permission, true)]
        static bool Spawn(CommandHandler h, StringArguments a)
        {
            if (!Enabled(h)) return false;
            return Run(h, () =>
            {
                string target = TargetText(a);
                if (target.Equals("all", StringComparison.OrdinalIgnoreCase)) { QueueSpawnAll(h); return; }
                SpawnCharacter(h, ResolveTarget(h, target));
            });
        }

        static void SpawnCharacter(CommandHandler h, ObjectGuid guid)
        {
            if (Editing.Contains(guid)) throw new InvalidOperationException("This SI character has a pending rename/delete.");
            var c = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
            if (c == null || c.IsDeleted) throw new InvalidOperationException("Character not found. Select an SI player or supply its GUID/name.");
            if (Global.WorldMgr.FindSession(c.AccountId) != null)
                throw new InvalidOperationException($"Account {unchecked((uint)c.AccountId)} is already online or loading.");
            EnsureBattlenetAccount(c.AccountId);
            WorldSession session = NewSession(c.AccountId);
            session.SetSICharacter(guid);
            Global.WorldMgr.AddSession(session);
            h.SendSysMessage($"Spawning {siPlayer.NormalizeStoredName(c.Name, c.Surname)} {c.Surname}, character {guid.GetCounter()}, at its saved location; initial goal: questing.");
        }

        [Command("despawn", Permission, true)]
        static bool Despawn(CommandHandler h, StringArguments a) => Run(h, () =>
        {
            string target = TargetText(a);
            if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                ++_spawnGeneration;
                var sessions = Global.WorldMgr.GetAllSessions().Where(s => s.IsServerControlled).ToArray();
                foreach (var session in sessions) session.KickPlayer("SI despawn all requested");
                h.SendSysMessage($"Saved logout queued for {sessions.Length} SI sessions.");
                return;
            }
            siPlayer p = Find(h, target); p.GetSession().KickPlayer("siPlayer despawn requested");
            h.SendSysMessage($"Saved logout queued for {p.GetName()}.");
        });
        [Command("info", Permission, true)]
        static bool Info(CommandHandler h, StringArguments a) => Run(h, () => ShowInfo(h, TargetText(a)));
        static void ShowInfo(CommandHandler h, string target)
        {
            ObjectGuid guid = ResolveTarget(h, target);
            var c = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
            if (c == null || c.IsDeleted) throw new InvalidOperationException("SI character not found.");
            siPlayer p = Global.ObjAccessor.FindConnectedPlayer(guid) as siPlayer;
            h.SendSysMessage($"{c.Name} {c.Surname}: character {guid.GetCounter()}, account {unchecked((uint)c.AccountId)}, level {c.Level}, {(p == null ? "offline" : "online")}.");
            if(p!=null)
            {
                int active=0;for(ushort slot=0;slot<SharedConst.MaxQuestLogSize;++slot)
                    if(p.GetQuestSlotQuestId(slot)!=0&&p.GetQuestStatus(p.GetQuestSlotQuestId(slot)) is QuestStatus.Incomplete or QuestStatus.Complete)++active;
                h.SendSysMessage($"Level {p.GetLevel()}; experience {p.GetXP()}/{p.GetXPForNextLevel()}; active quests: {active}; lifetime rewarded: {p.LifetimeQuestsCompleted}; failed: {p.LifetimeQuestsFailed}; abandoned: {p.LifetimeQuestsAbandoned}. Outcome tracking since {DateTimeOffset.FromUnixTimeSeconds(p.QuestHistorySince):u}; earlier rewarded IDs included.");
            }
            if(p?.Controller!=null)h.SendSysMessage($"Last behavior: {p.Controller.LastBehaviorState}; next expected: {p.Controller.NextExpectedState}; free bag slots: {p.GetFreeInventorySlotCount()}.");
            if(p?.Controller!=null)h.SendSysMessage("Effective SI settings: "+p.Controller.EffectiveSettings);
            else
            {
                var saved=DB.Characters.Query("SELECT c.level,c.xp,(SELECT COUNT(*) FROM character_queststatus q WHERE q.guid=c.guid AND q.status IN (1,3)),COALESCE(h.completed,(SELECT COUNT(*) FROM character_queststatus_rewarded r WHERE r.guid=c.guid)),COALESCE(h.failed,0),COALESCE(h.abandoned,0) FROM characters c LEFT JOIN character_si_quest_history h ON h.guid=c.guid WHERE c.guid={0}",guid.GetCounter());
                if(!saved.IsEmpty())h.SendSysMessage($"Level {saved.Read<int>(0)}; experience {saved.Read<int>(1)}/{Global.ObjectMgr.GetXPForLevel(saved.Read<int>(0))}; active quests: {saved.Read<long>(2)}; recorded lifetime rewarded/failed/abandoned: {saved.Read<long>(3)}/{saved.Read<long>(4)}/{saved.Read<long>(5)}.");
                h.SendSysMessage("Last behavior: unavailable while offline; next expected: login and preparation.");
            }
            if (p != null) h.SendSysMessage($"{p.Controller?.Status}; behavior: {p.Controller?.Behavior}; health: {p.GetHealthPct():F0}%; mana: {p.GetPowerPct(PowerType.Mana):F0}%; plan: {p.Controller?.Plan}; navigation: {(p.NavigationBlocked ? "blocked" : p.IsStopped() ? "stopped" : "moving")} ({p.NavigationMode}); damage: {p.Controller?.DamageDelivered}; quests accepted/completed: {p.Controller?.QuestsAccepted}/{p.Controller?.QuestsCompleted}; items looted: {p.Controller?.ItemsLooted}");
        }
        [Command("status", Permission, true)]
        static bool Status(CommandHandler h, StringArguments a) => Run(h, () =>
        {
            string target = TargetText(a);
            if (!target.Equals("all", StringComparison.OrdinalIgnoreCase) && (target.Length != 0 || !SelectedSI(h).IsEmpty())) { ShowInfo(h, target); return; }
            var sessions = Global.WorldMgr.GetAllSessions().Where(s => s.IsServerControlled).ToArray();
            h.SendSysMessage($"SI: {BotAccounts().Count} bot accounts, {sessions.Count(s => s.GetPlayer() != null)} online, {sessions.Count(s => s.PlayerLoading())} loading.");
            h.SendSysMessage(Game.AI.SIMovement.SIMovementRuntime.Statistics);
        });
        [Command("list", Permission, true)]
        static bool List(CommandHandler h, StringArguments a) => Run(h, () =>
        {
            string target = TargetText(a);
            if (!target.Equals("all", StringComparison.OrdinalIgnoreCase) && (target.Length != 0 || !SelectedSI(h).IsEmpty())) { ShowInfo(h, target); return; }
            var accounts = BotAccounts();
            if (accounts.Count == 0) { h.SendSysMessage("No SI players yet. Use .si create \"first surname\" <race> <class>."); return; }
            SQLResult rows = DB.Characters.Query("SELECT guid,name,surname,level,account FROM characters WHERE deleteInfos_Name IS NULL AND account IN (" +
                string.Join(',', accounts.Keys.Select(id => unchecked((uint)id))) + ") ORDER BY name LIMIT 100");
            if (rows.IsEmpty()) { h.SendSysMessage("Bot accounts exist but have no characters."); return; }
            do
            {
                long counter = rows.Read<long>(0);
                bool online = Global.ObjAccessor.FindConnectedPlayer(ObjectGuid.Create(HighGuid.Player, counter)) is siPlayer;
                h.SendSysMessage($"{rows.Read<string>(1)} {rows.Read<string>(2)}: character {counter}, level {rows.Read<byte>(3)}, account {rows.Read<uint>(4)}: {(online ? "online" : "offline")}");
            } while (rows.NextRow());
            rows.Close();
        });
        static bool Order(CommandHandler h, string name, string action) => Run(h, () =>
        {
            siPlayer p = Find(h, name);
            if (p.Controller == null) throw new InvalidOperationException("The SI controller is not ready.");
            ObjectGuid target = ObjectGuid.Empty;
            if (action is "follow" or "attack" or "teleport")
            {
                Player issuer = h.GetSession()?.GetPlayer();
                if (issuer == null) throw new InvalidOperationException("This command requires an in-game issuer.");
                target = action == "attack" ? issuer.GetTarget() : issuer.GetGUID();
                if (action == "attack" && target == p.GetGUID()) target = p.GetVictim()?.GetGUID() ?? p.GetTarget();
                if (action == "attack")
                {
                    Unit unit = Global.ObjAccessor.GetUnit(p, target);
                    if (unit == null || !unit.IsCreature() || !unit.IsAlive() || unit.GetMap() != p.GetMap()
                        || !p.CanSeeOrDetect(unit, distanceCheck: true) || !p.IsValidAttackTarget(unit))
                        throw new InvalidOperationException("Select a living hostile creature visible to that SI player.");
                }
                if (action == "teleport" && p.IsBeingTeleported()) throw new InvalidOperationException("That SI player is already transferring.");
            }
            p.Controller.Order(action, target); h.SendSysMessage($"{p.GetName()}: {action} requested.");
        });
        [Command("follow", Permission, true)]
        static bool Follow(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "follow");
        [Command("guard", Permission, true)]
        static bool Guard(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "guard");
        [Command("grind", Permission, true)]
        static bool Grind(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "grind");
        [Command("attack", Permission, true)]
        static bool Attack(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "attack");
        [Command("pause", Permission, true)]
        static bool Pause(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "pause");
        [Command("resume", Permission, true)]
        static bool Resume(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "resume");
        [Command("quest", Permission, true)]
        static bool Quest(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "quest");
        [Command("rest", Permission, true)]
        static bool Rest(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "rest");
        [Command("save", Permission, true)]
        static bool Save(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "save");
        [Command("teleport", Permission, true)]
        static bool Teleport(CommandHandler h, StringArguments a) => Order(h, TargetText(a), "teleport");
        [Command("order", Permission, true)]
        static bool ExplicitOrder(CommandHandler h, StringArguments a)
        {
            string[] words = TargetText(a).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 1 || words[^1] is not ("follow" or "guard" or "grind" or "attack" or "pause" or "resume" or "quest" or "rest" or "save" or "teleport")) return false;
            return Order(h, string.Join(' ', words[..^1]), words[^1]);
        }
        [Command("", Permission, true)]
        [Command("help", Permission, true)]
        static bool Help(CommandHandler h)
        {
            h.SendSysMessage(".si create random <horde|alliance> [count=1, max=1000]; .si create \"first surname\" <race> <class>.");
            h.SendSysMessage(".si spawn|despawn all; .si spawncount <1-10000>; .si delete [GUID|\"name\"]; .si rename [GUID|\"name\"] <random|\"first surname\">.");
            h.SendSysMessage(".si quest|grind|pause|resume|follow|guard|attack|rest|save|teleport|info [GUID|\"name\"]; .si list [all]; .si status [all].");
            h.SendSysMessage("An SI target takes priority over a supplied GUID/name. With it selected: .si quest; .si rename random; .si rename \"first surname\". Explicit all applies to the roster. Online renames save and relog the bot.");
            return true;
        }
        static string TargetText(StringArguments a) => string.Join(' ', ParseArguments(a.GetString()));
        static siPlayer Find(CommandHandler h, string target) => Global.ObjAccessor.FindConnectedPlayer(ResolveTarget(h, target)) as siPlayer
            ?? throw new InvalidOperationException("That SI player is offline or does not exist. Spawn it first.");
        static ObjectGuid SelectedSI(CommandHandler h) => h.GetSelectedPlayer() is siPlayer selected ? selected.GetGUID() : ObjectGuid.Empty;
        static ObjectGuid ResolveTarget(CommandHandler h, string identifier)
        {
            ObjectGuid selected = SelectedSI(h);
            return !selected.IsEmpty() ? selected : ResolveCharacter(identifier);
        }
        static ObjectGuid ResolveCharacter(string identifier)
        {
            string[] words = (identifier ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1 && long.TryParse(words[0], out long number))
            {
                if (number <= 0) return ObjectGuid.Empty;
                ObjectGuid guid = ObjectGuid.Create(HighGuid.Player, number);
                var c = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                return c != null && !c.IsDeleted ? guid : ObjectGuid.Empty;
            }
            if (words.Length is < 1 or > 2 || !ObjectManager.NormalizePlayerName(ref words[0])) return ObjectGuid.Empty;
            var character = Global.CharacterCacheStorage.GetCharacterCacheByFirstName(words[0]);
            return character != null && !character.IsDeleted && (words.Length == 1
                || string.Equals(character.Surname, words[1], StringComparison.OrdinalIgnoreCase)) ? character.Guid : ObjectGuid.Empty;
        }
    }
}
