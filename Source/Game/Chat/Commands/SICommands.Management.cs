using Framework.Constants;
using Framework.Database;
using Framework.IO;
using Game.Cache;
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
        static int _spawnGeneration;

        // Called between session and map updates. Bulk requests advance one bounded
        // operation at a time; database callbacks also run on the world thread.
        internal static void Update()
        {
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
            _ => Array.Empty<Class>()
        };

        static (string First, string Surname) RandomName(Race race, Gender gender, ObjectGuid own = default)
        {
            for (int attempt = 0; attempt < 200; ++attempt)
            {
                string generated = Global.DB2Mgr.GetNameGenEntry(race, gender);
                if (!TryName(generated, 2, 12, out string first) || Creating.ContainsKey(first)) continue;
                var existing = Global.CharacterCacheStorage.GetCharacterCacheByFirstName(first);
                if (existing != null && existing.Guid != own) continue;
                if (ObjectManager.CheckPlayerName(first, Locale.enUS, true) != ResponseCodes.CharNameSuccess || Global.ObjectMgr.IsReservedName(first)) continue;
                string surname = Global.DB2Mgr.GetNameGenEntry(race, gender, 1);
                if (!string.IsNullOrEmpty(surname) && !TryName(surname, 1, 32, out surname)) continue;
                if (existing?.Guid == own && string.Equals(existing.Surname, surname ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                return (first, surname ?? "");
            }
            throw new InvalidOperationException("Could not find an unused DB2 name for this race/gender. No fabricated fallback name was used.");
        }

        static void QueueRandomCreation(CommandHandler h, IReadOnlyList<string> words)
        {
            var (team, total) = RandomCreationOptions(words);
            Race[] races = team == Team.Horde ? new[] { Race.Orc, Race.Undead, Race.Tauren, Race.Troll }
                : new[] { Race.Human, Race.Dwarf, Race.NightElf, Race.Gnome };
            races = races.Where(race => ClassicClasses(race).Any(cls => Global.ObjectMgr.GetPlayerInfo(race, cls) != null)).ToArray();
            if (races.Length == 0) throw new InvalidOperationException("No supported Vanilla race/class combinations for that faction.");
            int remaining = total, active = 0, created = 0, failed = 0;
            Work.Enqueue(() =>
            {
                if (remaining == 0)
                {
                    if (active != 0) return false;
                    h.SendSysMessage($"SI random {team}: created {created}/{total}, failed {failed}.");
                    return true;
                }
                if (active >= 2) return false;
                --remaining;
                try
                {
                    Race race = races[RandomNumberGenerator.GetInt32(races.Length)];
                    Class[] classes = ClassicClasses(race).Where(cls => Global.ObjectMgr.GetPlayerInfo(race, cls) != null).ToArray();
                    Class cls = classes[RandomNumberGenerator.GetInt32(classes.Length)];
                    Gender gender = RandomNumberGenerator.GetInt32(2) == 0 ? Gender.Male : Gender.Female;
                    var name = RandomName(race, gender);
                    ++active;
                    try
                    {
                        CreateNamed(h, (name.First + " " + name.Surname).Trim(), race.ToString(), cls.ToString(), gender,
                            success => { --active; if (success) ++created; else ++failed; });
                    }
                    catch { --active; throw; }
                }
                catch (Exception error) { ++failed; ReportCreationFailure(h, "SI creation failed: " + error.GetBaseException().Message); }
                return false;
            });
            h.SendSysMessage($"Queued {total} random {team} SI character(s). Creation is paced; characters remain offline until spawned.");
        }

        static void QueueSpawnAll(CommandHandler h)
        {
            int generation = ++_spawnGeneration;
            var accounts = BotAccounts();
            if (accounts.Count == 0) { h.SendSysMessage("No known SI characters."); return; }
            using SQLResult rows = DB.Characters.Query("SELECT guid FROM characters WHERE deleteInfos_Name IS NULL AND account IN (" +
                string.Join(',', accounts.Keys.Select(id => unchecked((uint)id))) + ") ORDER BY guid");
            var pending = new Queue<ObjectGuid>();
            if (!rows.IsEmpty()) do { pending.Enqueue(ObjectGuid.Create(HighGuid.Player, rows.Read<long>(0))); } while (rows.NextRow());
            int total = pending.Count, spawned = 0, skipped = 0, failed = 0;
            Work.Enqueue(() =>
            {
                if (generation != _spawnGeneration) { h.SendSysMessage("Pending SI spawn-all request cancelled."); return true; }
                if (!pending.TryDequeue(out var guid))
                { h.SendSysMessage($"SI spawn all: {spawned} login(s) queued, {skipped} already online/loading, {failed} failed."); return true; }
                var character = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                if (character == null || character.IsDeleted || Editing.Contains(guid)) { ++skipped; return false; }
                if (Global.WorldMgr.FindSession(character.AccountId) != null) { ++skipped; return false; }
                try { SpawnCharacter(h, guid); ++spawned; }
                catch (Exception error) { ++failed; h.SendSysMessage($"SI spawn {guid.GetCounter()}: {error.Message}"); }
                return false;
            });
            h.SendSysMessage($"Queued spawn all for {total} known SI characters; existing online/loading sessions are skipped.");
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
                int generation = _spawnGeneration, spawned = 0, failed = 0;
                Work.Enqueue(() =>
                {
                    if (generation != _spawnGeneration) { h.SendSysMessage("Pending SI spawncount request cancelled."); return true; }
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
            var existing = Global.CharacterCacheStorage.GetCharacterCacheByFirstName(name.First);
            if (existing != null && existing.Guid != guid) throw new InvalidOperationException("That first name is already taken.");
            if (!Creating.TryAdd(name.First, 0)) throw new InvalidOperationException("That name is already being created or assigned.");
            if (!Editing.Add(guid)) { Creating.TryRemove(name.First, out _); throw new InvalidOperationException("This SI character already has a pending rename/delete."); }
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
                            Global.CharacterCacheStorage.UpdateCharacterSurname(guid, name.Surname);
                            Global.CharacterCacheStorage.UpdateCharacterData(guid, name.First);
                            h.SendSysMessage($"Renamed SI character {guid.GetCounter()} to {(name.First + " " + name.Surname).Trim()}.");
                            if (relog) Work.Enqueue(() => { Run(h, () => SpawnCharacter(h, guid)); return true; });
                        }
                        finally { Creating.TryRemove(name.First, out _); Editing.Remove(guid); }
                    });
                    return true;
                }
                catch (Exception error)
                {
                    h.SendSysMessage("SI rename: " + error.Message);
                    Creating.TryRemove(name.First, out _); Editing.Remove(guid);
                    return true;
                }
            });
            h.SendSysMessage($"Queued rename for SI character {guid.GetCounter()}; an online bot will save, relog and resume questing.");
        });
    }
}
