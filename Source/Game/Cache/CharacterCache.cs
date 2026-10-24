using System.Collections.Generic;
using Game.Entities;
using System;
using System.Linq;
using Framework.Database;
using Framework.Constants;
using Game.Arenas;
using Game.Networking.Packets;

namespace Game.Cache
{
    public partial class CharacterCache : Singleton<CharacterCache>
    {
        Dictionary<ObjectGuid, CharacterCacheEntry> _characterCacheStore = new();
        Dictionary<string, CharacterCacheEntry> _characterCacheByNameStore = new();

        CharacterCache() { }

        public void LoadCharacterCacheStorage()
        {
            _characterCacheStore.Clear();
            _characterCacheByNameStore.Clear();
            _mailRecipientsByName.Clear();
            RelativeTime oldMSTime = Time.NowRelative;

            SQLResult result = DB.Characters.Query("SELECT guid, name, account, race, gender, class, level, deleteDate, surname FROM characters");
            if (result.IsEmpty())
            {
                Log.outInfo(LogFilter.ServerLoading, "No character name data loaded, empty query");
                return;
            }

            do
            {
                ObjectGuid guid = ObjectGuid.Create(HighGuid.Player, result.Read<long>(0));
                AddCharacterCacheEntry(guid, result.Read<int>(2), result.Read<string>(1), result.Read<byte>(4), result.Read<byte>(3), result.Read<byte>(5), result.Read<byte>(6), result.Read<uint>(7) != 0);
                UpdateCharacterSurname(guid, result.Read<string>(8));
            } while (result.NextRow());

            Log.outInfo(LogFilter.ServerLoading, 
                $"Loaded character infos for {_characterCacheStore.Count} characters in {Time.Diff(oldMSTime)} ms.");
        }

        public void AddCharacterCacheEntry(ObjectGuid guid, int accountId, string name, byte gender, byte race, byte playerClass, byte level, bool isDeleted)
        {
            string surname = _characterCacheStore.TryGetValue(guid, out CharacterCacheEntry existing) ? existing.Surname : string.Empty;
            if (existing != null) UpdateMailRecipientIndex(existing, add: false);
            var data = new CharacterCacheEntry();
            data.Guid = guid;
            data.Name = name;
            data.Surname = surname;
            data.AccountId = accountId;
            data.RaceId = (Race)race;
            data.Sex = (Gender)gender;
            data.ClassId = (Class)playerClass;
            data.Level = level;
            data.GuildId = 0;                           // Will be set in guild loading or guild setting
            for (byte i = 0; i < SharedConst.MaxArenaSlot; ++i)
                data.ArenaTeamId[i] = 0;                // Will be set in arena teams loading
            data.IsDeleted = isDeleted;

            // Fill Name to Guid Store
            if (!isDeleted)
                _characterCacheByNameStore[name] = data;
            _characterCacheStore[guid] = data;
            UpdateMailRecipientIndex(data, add: true);
        }

        public void DeleteCharacterCacheEntry(ObjectGuid guid, string name)
        {
            if (_characterCacheStore.TryGetValue(guid, out var entry))
                UpdateMailRecipientIndex(entry, add: false);
            _characterCacheStore.Remove(guid);
            _characterCacheByNameStore.Remove(name);
        }

        public void UpdateCharacterData(ObjectGuid guid, string name, byte? gender = null, byte? race = null)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return;

            string oldName = characterCacheEntry.Name;
            UpdateMailRecipientIndex(characterCacheEntry, add: false);
            characterCacheEntry.Name = name;

            if (gender.HasValue)
                characterCacheEntry.Sex = (Gender)gender.Value;

            if (race.HasValue)
                characterCacheEntry.RaceId = (Race)race.Value;

            InvalidatePlayer invalidatePlayer = new();
            invalidatePlayer.Guid = guid;
            Global.WorldMgr.SendGlobalMessage(invalidatePlayer);

            // Correct name -> pointer storage
            _characterCacheByNameStore.Remove(oldName);
            if (!characterCacheEntry.IsDeleted)
                _characterCacheByNameStore[name] = characterCacheEntry;
            UpdateMailRecipientIndex(characterCacheEntry, add: true);
        }

        public void UpdateCharacterGender(ObjectGuid guid, byte gender)
        {
            if (!_characterCacheStore.ContainsKey(guid))
                return;

            _characterCacheStore[guid].Sex = (Gender)gender;
        }

        public void UpdateCharacterLevel(ObjectGuid guid, byte level)
        {
            if (!_characterCacheStore.ContainsKey(guid))
                return;

            _characterCacheStore[guid].Level = level;
        }

        // Resolve characters whose old SI name contains the separately stored surname, before login
        // has corrected the name index. Exact first-name matches retain priority.
        internal CharacterCacheEntry GetCharacterCacheByFirstName(string first)
        {
            return GetCharacterCacheByName(first) ?? _characterCacheStore.Values.FirstOrDefault(entry => !entry.IsDeleted &&
                string.Equals(siPlayer.NormalizeStoredName(entry.Name, entry.Surname), first, StringComparison.OrdinalIgnoreCase));
        }

        public void UpdateCharacterSurname(ObjectGuid guid, string surname)
        {
            if (_characterCacheStore.TryGetValue(guid, out CharacterCacheEntry entry))
            {
                UpdateMailRecipientIndex(entry, add: false);
                entry.Surname = surname ?? string.Empty;
                UpdateMailRecipientIndex(entry, add: true);
            }
        }

        public string GetCharacterSurnameByGuid(ObjectGuid guid)
        {
            return _characterCacheStore.TryGetValue(guid, out CharacterCacheEntry entry) ? entry.Surname : string.Empty;
        }

        public void UpdateCharacterAccountId(ObjectGuid guid, int accountId)
        {
            if (!_characterCacheStore.ContainsKey(guid))
                return;

            _characterCacheStore[guid].AccountId = accountId;
        }

        public void UpdateCharacterGuildId(ObjectGuid guid, long guildId)
        {
            if (!_characterCacheStore.ContainsKey(guid))
                return;

            _characterCacheStore[guid].GuildId = guildId;
        }

        public void UpdateCharacterArenaTeamId(ObjectGuid guid, byte slot, int arenaTeamId)
        {
            if (!_characterCacheStore.ContainsKey(guid))
                return;

            _characterCacheStore[guid].ArenaTeamId[slot] = arenaTeamId;
        }

        public void UpdateCharacterInfoDeleted(ObjectGuid guid, bool deleted, string name)
        {
            if (!_characterCacheStore.TryGetValue(guid, out var cacheEntry))
                return;

            UpdateMailRecipientIndex(cacheEntry, add: false);
            _characterCacheByNameStore.Remove(cacheEntry.Name);
            cacheEntry.Name = name;
            cacheEntry.IsDeleted = deleted;
            if (!deleted)
                _characterCacheByNameStore[name] = cacheEntry;
            UpdateMailRecipientIndex(cacheEntry, add: true);
        }

        public bool HasCharacterCacheEntry(ObjectGuid guid)
        {
            return _characterCacheStore.ContainsKey(guid);
        }

        public CharacterCacheEntry GetCharacterCacheByGuid(ObjectGuid guid)
        {
            return _characterCacheStore.LookupByKey(guid);
        }

        public CharacterCacheEntry GetCharacterCacheByName(string name)
        {
            return _characterCacheByNameStore.LookupByKey(name);
        }

        public ObjectGuid GetCharacterGuidByName(string name)
        {
            var characterCacheEntry = _characterCacheByNameStore.LookupByKey(name);
            if (characterCacheEntry != null)
                return characterCacheEntry.Guid;

            return ObjectGuid.Empty;
        }

        public bool GetCharacterNameByGuid(ObjectGuid guid, out string name)
        {
            name = "Unknown";
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return false;

            name = characterCacheEntry.Name;
            return true;
        }

        public Team GetCharacterTeamByGuid(ObjectGuid guid)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return Team.Other;

            return Player.TeamForRace(characterCacheEntry.RaceId);
        }

        public int GetCharacterAccountIdByGuid(ObjectGuid guid)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return 0;

            return characterCacheEntry.AccountId;
        }

        public int GetCharacterAccountIdByName(string name)
        {
            var characterCacheEntry = _characterCacheByNameStore.LookupByKey(name);
            if (characterCacheEntry != null)
                return characterCacheEntry.AccountId;

            return 0;
        }

        public byte GetCharacterLevelByGuid(ObjectGuid guid)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return 0;

            return characterCacheEntry.Level;
        }

        public long GetCharacterGuildIdByGuid(ObjectGuid guid)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return 0;

            return characterCacheEntry.GuildId;
        }

        public int GetCharacterArenaTeamIdByGuid(ObjectGuid guid, byte type)
        {
            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return 0;

            return characterCacheEntry.ArenaTeamId[ArenaTeam.GetSlotByType(type)];
        }

        public bool GetCharacterNameAndClassByGUID(ObjectGuid guid, out string name, out byte _class)
        {
            name = "Unknown";
            _class = 0;

            var characterCacheEntry = _characterCacheStore.LookupByKey(guid);
            if (characterCacheEntry == null)
                return false;

            name = characterCacheEntry.Name;
            _class = (byte)characterCacheEntry.ClassId;
            return true;
        }

    }

    public class CharacterCacheEntry
    {
        public ObjectGuid Guid;
        public string Name;
        public string Surname = string.Empty;
        public int AccountId;
        public Class ClassId;
        public Race RaceId;
        public Gender Sex;
        public byte Level;
        public long GuildId;
        public int[] ArenaTeamId = new int[SharedConst.MaxArenaSlot];
        public bool IsDeleted;
    }
}
