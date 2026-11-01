using Framework.Constants;
using Framework.Database;
using Framework.IO;
using Game.Cache;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Game.Chat.Commands
{
    partial class SICommands
    {
        static readonly ConcurrentQueue<Func<bool>> Work = new();
        static readonly AsyncCallbackProcessor<TransactionCallback> Transactions = new();
        static readonly HashSet<ObjectGuid> Editing = new();
        static long _nextWork;
        static readonly SISpawnQueueState SpawnQueue = new();

        // Called between session and map updates. Bulk requests advance one bounded
        // operation at a time; database callbacks also run on the world thread.
        internal static void Update()
        {
            Game.AI.SIAuctionWork.Update();
            Transactions.ProcessReadyCallbacks();
            long now = Environment.TickCount64;
            if (now < _nextWork || !Work.TryDequeue(out var step)) return;
            _nextWork = now + 100;
            try { if (!step()) Work.Enqueue(step); }
            catch (Exception error) { Log.outError(LogFilter.SI, $"SI management job failed: {error.Message}"); }
        }

        internal static List<string> ParseArguments(string text)
        {
            var words = new List<string>();
            var word = new StringBuilder();
            char quote = '\0'; bool started = false;
            foreach (char character in text ?? "")
            {
                if (quote != '\0')
                {
                    if (character == quote) quote = '\0'; else word.Append(character);
                }
                else if (character is '\'' or '"') { quote = character; started = true; }
                else if (char.IsWhiteSpace(character))
                {
                    if (started) { words.Add(word.ToString()); word.Clear(); started = false; }
                }
                else { word.Append(character); started = true; }
            }
            if (quote != '\0') throw new ArgumentException("Close the quoted character name.");
            if (started) words.Add(word.ToString());
            return words;
        }

        internal static (Team Team, int Count) RandomCreationOptions(IReadOnlyList<string> words)
        {
            if (words.Count is < 2 or > 3) throw new ArgumentException("Use .si create random <horde|alliance> [count].");
            Team team = words[1].ToLowerInvariant() switch
            {
                "horde" => Team.Horde,
                "alliance" => Team.Alliance,
                _ => throw new ArgumentException("Choose horde or alliance.")
            };
            int count = 1;
            if (words.Count == 3 && (!int.TryParse(words[2], out count) || count is < 1 or > 1000))
                throw new ArgumentException("Creation count must be between 1 and 1000.");
            return (team, count);
        }

        internal static Class[] ClassicClasses(Race race) => race switch
        {
            Race.Human => new[] { Class.Warrior, Class.Paladin, Class.Rogue, Class.Priest, Class.Mage, Class.Warlock },
            Race.Dwarf => new[] { Class.Warrior, Class.Paladin, Class.Hunter, Class.Rogue, Class.Priest },
            Race.NightElf => new[] { Class.Warrior, Class.Hunter, Class.Rogue, Class.Priest, Class.Druid },
            Race.Gnome => new[] { Class.Warrior, Class.Rogue, Class.Mage, Class.Warlock },
            Race.Orc => new[] { Class.Warrior, Class.Hunter, Class.Rogue, Class.Shaman, Class.Warlock },
            Race.Undead => new[] { Class.Warrior, Class.Rogue, Class.Priest, Class.Mage, Class.Warlock },
            Race.Tauren => new[] { Class.Warrior, Class.Hunter, Class.Shaman, Class.Druid },
            Race.Troll => new[] { Class.Warrior, Class.Hunter, Class.Rogue, Class.Priest, Class.Shaman, Class.Mage },
            Race.SkyborneAlliance => new[] { Class.Warrior, Class.Hunter, Class.Rogue, Class.Mage, Class.Druid },
            Race.SkyborneHorde => new[] { Class.Warrior, Class.Hunter, Class.Rogue, Class.Shaman, Class.Druid },
            _ => Array.Empty<Class>()
        };

        internal static Race[] RandomCreationRaces(Team team) => team switch
        {
            Team.Horde => new[] { Race.Orc, Race.Undead, Race.Tauren, Race.Troll, Race.SkyborneHorde },
            Team.Alliance => new[] { Race.Human, Race.Dwarf, Race.NightElf, Race.Gnome, Race.SkyborneAlliance },
            _ => Array.Empty<Race>()
        };

        static readonly ConcurrentDictionary<(Race Race,Gender Gender,byte Type),string[]> CreationNames=new();
        static string[] NativeNames(Race race,Gender gender,byte type)=>CreationNames.GetOrAdd((race,gender,type),key=>
            CliDB.NameGenStorage.Values.Where(n=>n.RaceID==key.Race&&n.Sex==key.Gender&&n.NameType==key.Type)
                .Select(n=>n.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        static (string First,string Surname)? AvailableRandomName(Race race,Gender gender,ObjectGuid own=default)
        {
            var names=NativeNames(race,gender,0).ToArray();RandomNumberGenerator.Shuffle(names.AsSpan());
            var surnames=NativeNames(race,gender,1).ToArray();
            if(surnames.Length==0)surnames=[""];
            RandomNumberGenerator.Shuffle(surnames.AsSpan());
            foreach(string generated in names)
            {
                if(!TryName(generated,2,12,out string first))continue;
                if(ObjectManager.CheckPlayerName(first,Locale.enUS,true)!=ResponseCodes.CharNameSuccess||Global.ObjectMgr.IsReservedName(first))continue;
                foreach(string generatedSurname in surnames)
                {
                    string surname="";
                    if(!string.IsNullOrEmpty(generatedSurname)&&!TryName(generatedSurname,1,48,out surname))continue;
                    string full=Player.FormatFullName(first,surname);
                    if(Creating.ContainsKey(full))continue;
                    var existing=Global.CharacterCacheStorage.GetCharacterCacheByFullName(first,surname);
                    if(existing!=null)continue; // Renames must actually change the full name too.
                    return(first,surname);
                }
            }
            return null;
        }

        static (string First,string Surname) RandomName(Race race,Gender gender,ObjectGuid own=default) =>
            AvailableRandomName(race,gender,own)??throw new InvalidOperationException("No unused native first-name/surname combination remains for this race/gender.");

        static void QueueRandomCreation(CommandHandler h, IReadOnlyList<string> words)
        {
            var options=RandomCreationRequest(words);
            if(options.Race is Race race && options.Class is Class cls)ValidateCreationCombination(race,cls);
            else if(AvailableCreationRaces(options.Team).Length==0)throw new InvalidOperationException("No supported race/class combinations for that faction.");
            int remaining=options.Count,active=0,created=0,failed=0;
            long generation=SpawnQueue.Generation;
            Work.Enqueue(() =>
            {
                if(!SpawnQueue.IsCurrent(generation))return true;
                if(remaining==0)
                {
                    if(active!=0)return false;
                    h.SendSysMessage($"SI random {options.Team}: created {created}/{options.Count}, failed {failed}; successful creations automatically log in.");
                    return true;
                }
                if(active>=2)return false;
                --remaining;
                try
                {
                    ++active;
                    try { CreateRandom(h,options.Team,options.Race,options.Class,(success,guid)=>{--active;if(success)++created;else ++failed;},generation); }
                    catch {--active;throw;}
                }
                catch(Exception error){++failed;ReportCreationFailure(h,"SI creation failed: "+error.GetBaseException().Message);}
                return false;
            });
            h.SendSysMessage($"Queued {options.Count} random {options.Team} SI character(s); creation and automatic login are paced.");
        }

        static void QueueSpawnAll(CommandHandler h)
        {
            if (!SpawnQueue.TryBeginAll(out long generation))
            { h.SendSysMessage("SI spawn all is already pending; continuing that batch."); return; }
            bool queued = false;
            try
            {
                var accounts = BotAccounts();
                if (accounts.Count == 0) { h.SendSysMessage("No known SI characters."); return; }
                using SQLResult rows = DB.Characters.Query("SELECT guid FROM characters WHERE deleteInfos_Name IS NULL AND account IN (" +
                    string.Join(',', accounts.Keys.Select(id => unchecked((uint)id))) + ") ORDER BY guid");
                var pending = new Queue<ObjectGuid>();
                var selectedAccounts = new HashSet<int>();
                int spawned = 0, skipped = 0, failed = 0;
                if (!rows.IsEmpty()) do
                {
                    var guid = ObjectGuid.Create(HighGuid.Player, rows.Read<long>(0));
                    var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                    if (character == null || character.IsDeleted || Editing.Contains(guid) || !selectedAccounts.Add(character.AccountId)) { ++skipped; continue; }
                    var session = Global.WorldMgr.FindSession(character.AccountId);
                    if (SISpawnQueueState.SessionAction(session != null, session?.IsServerControlled == true,
                        session?.PlayerDisconnected() == true, true) == SISpawnSessionAction.Skip) { ++skipped; continue; }
                    pending.Enqueue(guid);
                } while (rows.NextRow());
                if (pending.Count == 0)
                { h.SendSysMessage($"SI spawn all: no eligible offline characters; {skipped} already online/loading or unavailable."); return; }
                int total = pending.Count;
                long logoutDeadline = Environment.TickCount64 + 60000;
                Work.Enqueue(() =>
                {
                    try
                    {
                        if (!SpawnQueue.IsCurrent(generation)) { SpawnQueue.CompleteAll(generation); return true; }
                        if (!pending.TryDequeue(out var guid))
                        {
                            SpawnQueue.CompleteAll(generation);
                            h.SendSysMessage($"SI spawn all: {spawned} login(s) queued, {skipped} already online/loading or unavailable, {failed} failed."); return true;
                        }
                        var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                        if (character == null || character.IsDeleted || Editing.Contains(guid)) { ++skipped; return false; }
                        var session = Global.WorldMgr.FindSession(character.AccountId);
                        var action = SISpawnQueueState.SessionAction(session != null, session?.IsServerControlled == true,
                            session?.PlayerDisconnected() == true, Environment.TickCount64 < logoutDeadline);
                        if (action == SISpawnSessionAction.WaitForLogout) { pending.Enqueue(guid); return false; }
                        if (action == SISpawnSessionAction.Skip) { ++skipped; return false; }
                        try { SpawnCharacter(h, guid); ++spawned; }
                        catch (Exception error) { ++failed; h.SendSysMessage($"SI spawn {guid.GetCounter()}: {error.Message}"); }
                        return false;
                    }
                    catch { SpawnQueue.CompleteAll(generation); throw; }
                });
                queued = true;
                h.SendSysMessage($"Queued spawn all for {total} eligible SI characters; closing SI sessions are allowed to finish logout first.");
            }
            finally { if (!queued) SpawnQueue.CompleteAll(generation); }
        }

        internal static int SpawnCountArgument(string text)
        {
            if (!int.TryParse(text.Trim(), out int count) || count is < 1 or > 10000)
                throw new ArgumentException("Use .si spawncount <count>, between 1 and 10000.");
            return count;
        }

        [Command("spawncount", Permission, true)]
        static bool SpawnCount(CommandHandler h, StringArguments arguments)
        {
            if (!Enabled(h)) return false;
            return Run(h, () =>
            {
                int requested = SpawnCountArgument(arguments.GetString());
                var accounts = BotAccounts();
                if (accounts.Count == 0) { h.SendSysMessage("No known SI characters."); return; }
                using var rows = DB.Characters.Query("SELECT guid FROM characters WHERE deleteInfos_Name IS NULL AND account IN (" +
                    string.Join(',', accounts.Keys.Select(id => unchecked((uint)id))) + ")");
                var candidates = new List<ObjectGuid>();
                if (!rows.IsEmpty()) do
                {
                    var guid = ObjectGuid.Create(HighGuid.Player, rows.Read<long>(0));
                    var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                    if (character is { IsDeleted: false } && !Editing.Contains(guid) && Global.WorldMgr.FindSession(character.AccountId) == null)
                        candidates.Add(guid);
                } while (rows.NextRow());
                // Shuffle without replacement, then keep one character per account: sessions own accounts.
                RandomNumberGenerator.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates));
                var pending = new Queue<ObjectGuid>();
                var selectedAccounts = new HashSet<int>();
                foreach (var guid in candidates)
                {
                    if (selectedAccounts.Add(Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid).AccountId)) pending.Enqueue(guid);
                }
                long generation = SpawnQueue.Generation;
                int spawned = 0, failed = 0;
                Work.Enqueue(() =>
                {
                    if (!SpawnQueue.IsCurrent(generation)) return true;
                    if (spawned == requested || !pending.TryDequeue(out var guid))
                    {
                        h.SendSysMessage($"SI spawncount: {spawned}/{requested} random offline character login(s) queued; {failed} failed." +
                            (spawned < requested ? " No more eligible offline SI accounts." : ""));
                        return true;
                    }
                    var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                    if (character == null || character.IsDeleted || Editing.Contains(guid) || Global.WorldMgr.FindSession(character.AccountId) != null) return false;
                    try { SpawnCharacter(h, guid); ++spawned; }
                    catch (Exception error) { ++failed; h.SendSysMessage($"SI spawn {guid.GetCounter()}: {error.Message}"); }
                    return false;
                });
                h.SendSysMessage($"Queued up to {Math.Min(requested, pending.Count)} random offline SI logins; requested {requested}. Logins are paced.");
            });
        }

        static CharacterCacheEntry RequireSI(ObjectGuid guid)
        {
            var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
            if (character == null || character.IsDeleted ||
                Global.ObjAccessor.FindConnectedPlayer(guid) is not siPlayer && !IsBotAccount(character.AccountId))
                throw new InvalidOperationException("SI character not found. Select an SI player or supply its GUID/name.");
            return character;
        }

        [Command("delete", Permission, true)]
        static bool Delete(CommandHandler h, StringArguments arguments) => Run(h, () =>
        {
            ObjectGuid guid = ResolveTarget(h, TargetText(arguments));
            var character = RequireSI(guid);
            if (!Editing.Add(guid)) throw new InvalidOperationException("This SI character already has a pending rename/delete.");
            string name = character.Name;
            long deadline = Environment.TickCount64 + 60000;
            Work.Enqueue(() =>
            {
                try
                {
                    if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("Timed out waiting for the SI character to finish loading/transferring.");
                    if (Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid) is not { IsDeleted: false })
                        throw new InvalidOperationException("The SI character was already deleted.");
                    var session = Global.WorldMgr.FindSession(character.AccountId);
                    if (session != null)
                    {
                        if (!session.IsServerControlled) throw new InvalidOperationException("That account is connected to a real client.");
                        if (session.PlayerLoading() || session.GetPlayer()?.IsBeingTeleported() == true) return false;
                        if (session.GetPlayer()?.GetGUID() == guid)
                        {
                            session.KickPlayer("SI delete requested");
                            // Earlier queued saves precede native deletion on the DB worker.
                            session.LogoutPlayer(false);
                        }
                    }
                    Player.DeleteFromDB(guid, character.AccountId, true, true);
                    h.SendSysMessage($"SI character {name} ({guid.GetCounter()}) deletion queued.");
                }
                catch (Exception error) { h.SendSysMessage("SI delete: " + error.Message); }
                Editing.Remove(guid);
                return true;
            });
            h.SendSysMessage($"Queued deletion of SI character {name} ({guid.GetCounter()}).");
        });

        internal static (string Identifier, string NewName) RenameArguments(IReadOnlyList<string> words, bool selected)
        {
            if (selected && words.Count == 1) return ("", words[0]);
            if (words.Count == 2) return (words[0], words[1]);
            throw new ArgumentException("Use .si rename [GUID|\"name\"] <random|\"first surname\">. With an SI target, omit its identifier.");
        }

        [Command("rename", Permission, true)]
        static bool Rename(CommandHandler h, StringArguments arguments) => Run(h, () =>
        {
            var parsed = RenameArguments(ParseArguments(arguments.GetString()), !SelectedSI(h).IsEmpty());
            ObjectGuid guid = ResolveTarget(h, parsed.Identifier);
            var character = RequireSI(guid);
            var name = parsed.NewName.Equals("random", StringComparison.OrdinalIgnoreCase)
                ? RandomName(character.RaceId, character.Sex, guid) : ParseFullName(parsed.NewName);
            if (ObjectManager.CheckPlayerName(name.First, Locale.enUS, true) != ResponseCodes.CharNameSuccess || Global.ObjectMgr.IsReservedName(name.First))
                throw new ArgumentException("The new name does not pass character name validation.");
            string fullName=Player.FormatFullName(name.First,name.Surname);
            var existing = Global.CharacterCacheStorage.GetCharacterCacheByFullName(name.First,name.Surname);
            if (existing != null && existing.Guid != guid) throw new InvalidOperationException("That full name is already taken.");
            if (!Creating.TryAdd(fullName, 0)) throw new InvalidOperationException("That name is already being created or assigned.");
            if (!Editing.Add(guid)) { Creating.TryRemove(fullName, out _); throw new InvalidOperationException("This SI character already has a pending rename/delete."); }
            bool relog = false;
            long deadline = Environment.TickCount64 + 60000;
            Work.Enqueue(() =>
            {
                try
                {
                    if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("Timed out waiting for the SI character to finish loading/transferring.");
                    if (Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid) is not { IsDeleted: false })
                        throw new InvalidOperationException("The SI character was already deleted.");
                    var session = Global.WorldMgr.FindSession(character.AccountId);
                    if (session != null)
                    {
                        if (!session.IsServerControlled) throw new InvalidOperationException("That account is connected to a real client.");
                        if (session.PlayerLoading() || session.GetPlayer()?.IsBeingTeleported() == true) return false;
                        if (session.GetPlayer() == null || session.GetPlayer().GetGUID() == guid)
                        {
                            relog |= session.GetPlayer() != null;
                            session.KickPlayer("SI rename requested");
                            session.LogoutPlayer(true);
                            return false; // Wait for account removal before a possible relog.
                        }
                    }
                    var transaction = new SQLTransaction();
                    var update = new PreparedStatement("UPDATE characters SET name=@0,surname=@1 WHERE guid=@2");
                    update.SetString(0, name.First); update.SetString(1, name.Surname); update.SetInt64(2, guid.GetCounter());
                    transaction.Append(update);
                    var declined = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_CHAR_DECLINED_NAME);
                    declined.SetInt64(0, guid.GetCounter()); transaction.Append(declined);
                    Transactions.AddCallback(DB.Characters.AsyncCommitTransaction(transaction)).AfterComplete(success =>
                    {
                        try
                        {
                            if (!success) { h.SendSysMessage("SI rename failed; the stored name was not changed."); return; }
                            Global.CharacterCacheStorage.UpdateCharacterData(guid,name.First,surname:name.Surname);
                            h.SendSysMessage($"Renamed SI character {guid.GetCounter()} to {(name.First + " " + name.Surname).Trim()}.");
                            if (relog) Work.Enqueue(() => { Run(h, () => SpawnCharacter(h, guid)); return true; });
                        }
                        finally { Creating.TryRemove(fullName, out _); Editing.Remove(guid); }
                    });
                    return true;
                }
                catch (Exception error)
                {
                    h.SendSysMessage("SI rename: " + error.Message);
                    Creating.TryRemove(fullName, out _); Editing.Remove(guid);
                    return true;
                }
            });
            h.SendSysMessage($"Queued rename for SI character {guid.GetCounter()}; an online bot will save, relog and resume questing.");
        });
    }
}
