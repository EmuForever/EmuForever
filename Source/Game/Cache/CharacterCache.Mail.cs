using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Cache
{
    public partial class CharacterCache
    {
        // At most two aliases per active character: first name and full name.
        // Buckets make legacy duplicate first names explicitly ambiguous instead
        // of letting cache load order decide who receives valuable attachments.
        readonly Dictionary<string, HashSet<ObjectGuid>> _mailRecipientsByName = new(StringComparer.OrdinalIgnoreCase);

        static string NormalizeMailRecipientName(string name) => string.IsNullOrWhiteSpace(name)
            ? string.Empty : string.Join(" ", name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        void UpdateMailRecipientIndex(CharacterCacheEntry entry, bool add)
        {
            if (entry.IsDeleted) return;
            // Older SI records stored the surname inside Name as well. Resolve
            // them before login without appending that surname a second time.
            string first = NormalizeMailRecipientName(siPlayer.NormalizeStoredName(entry.Name, entry.Surname));
            if (first.Length == 0) return;
            UpdateMailRecipientAlias(first, entry.Guid, add);
            string surname = NormalizeMailRecipientName(entry.Surname);
            if (surname.Length != 0) UpdateMailRecipientAlias(first + " " + surname, entry.Guid, add);
        }

        void UpdateMailRecipientAlias(string name, ObjectGuid guid, bool add)
        {
            if (add)
            {
                if (!_mailRecipientsByName.TryGetValue(name, out var recipients))
                    _mailRecipientsByName.Add(name, recipients = new HashSet<ObjectGuid>());
                recipients.Add(guid);
            }
            else if (_mailRecipientsByName.TryGetValue(name, out var existingRecipients))
            {
                existingRecipients.Remove(guid);
                if (existingRecipients.Count == 0) _mailRecipientsByName.Remove(name);
            }
        }

        public ObjectGuid GetMailRecipientGuidByName(string name)
        {
            string key = NormalizeMailRecipientName(name);
            if (key.Length == 0 || !_mailRecipientsByName.TryGetValue(key, out var recipients) || recipients.Count != 1)
                return ObjectGuid.Empty;
            // Only enumerate a singleton bucket. No scan of online players or
            // the character database is needed, including for offline mail.
            foreach (ObjectGuid guid in recipients)
                if (_characterCacheStore.TryGetValue(guid, out var entry) && !entry.IsDeleted)
                    return guid;
            return ObjectGuid.Empty;
        }
    }
}
