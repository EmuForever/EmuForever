// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Cryptography.Ed25519;
using Framework.IO;
using Game.DataStorage;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Game.Networking.Packets
{
    class Ping : ClientPacket
    {
        public Ping(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Serial = _worldPacket.ReadUInt32();
            Latency = _worldPacket.ReadUInt32();
        }
        
        public uint Serial;
        public uint Latency;
    }

    class Pong : ServerPacket
    {
        public Pong(uint serial) : base(ServerOpcodes.Pong)
        {
            Serial = serial;
        }

        public override void Write()
        {
            _worldPacket.WriteUInt32(Serial);
        }

        uint Serial;
    }

    class AuthChallenge : ServerPacket
    {
        public AuthChallenge() : base(ServerOpcodes.AuthChallenge) { }

        public override void Write()
        {
            _worldPacket.WriteBytes(DosChallenge);
            _worldPacket.WriteBytes(Challenge);
            _worldPacket.WriteUInt8(DosZeroBits);
        }

        public byte[] Challenge = new byte[32];
        public byte[] DosChallenge = new byte[32]; // Encryption seeds
        public byte DosZeroBits;
    }

    class AuthSession : ClientPacket
    {
        const int DigestLength = 24;

        public AuthSession(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            DosResponse = _worldPacket.ReadUInt64();
            RegionID = _worldPacket.ReadUInt32();
            BattlegroupID = _worldPacket.ReadUInt32();
            RealmID = _worldPacket.ReadUInt32();

            for (var i = 0; i < LocalChallenge.GetLimit(); ++i)
                LocalChallenge[i] = _worldPacket.ReadUInt8();

            Digest = _worldPacket.ReadBytes(DigestLength);

            UseIPv6 = _worldPacket.HasBit();
            int realmJoinTicketSize = _worldPacket.ReadInt32();
            if (realmJoinTicketSize != 0)
                RealmJoinTicket = _worldPacket.ReadString(realmJoinTicketSize);

            // Forever 1.60 sends a JSON ticket ({"gameAccount":"..."}); older clients send the account name.
            RealmJoinTicket = GameAccountFromTicket(RealmJoinTicket);
        }

        static string GameAccountFromTicket(string ticket)
        {
            if (string.IsNullOrEmpty(ticket) || ticket[0] != '{')
                return ticket ?? "";

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(ticket);
                if (doc.RootElement.TryGetProperty("gameAccount", out var account))
                    return account.GetString() ?? "";
            }
            catch (System.Text.Json.JsonException) { }

            return ticket;
        }

        public uint RegionID;
        public uint BattlegroupID;
        public uint RealmID;
        public Array<byte> LocalChallenge = new(32);
        public byte[] Digest = new byte[DigestLength];
        public ulong DosResponse;
        public string RealmJoinTicket;
        public bool UseIPv6;
    }

    class AuthResponse : ServerPacket
    {
        public AuthResponse() : base(ServerOpcodes.AuthResponse) { }

        public override void Write()
        {
            // Forever layout: TC AuthResponse::Write
            _worldPacket.WriteUInt32((uint)Result);
            _worldPacket.WriteBit(SuccessInfo != null);
            _worldPacket.WriteBit(WaitInfo.HasValue);
            _worldPacket.FlushBits();

            if (SuccessInfo != null)
            {
                _worldPacket.WriteUInt32(SuccessInfo.VirtualRealmAddress);
                _worldPacket.WriteInt32(SuccessInfo.VirtualRealms.Count);
                _worldPacket.WriteUInt32(SuccessInfo.TimeRested);
                _worldPacket.WriteUInt8(SuccessInfo.ActiveExpansionLevel);
                _worldPacket.WriteUInt8(SuccessInfo.AccountExpansionLevel);
                _worldPacket.WriteUInt32(SuccessInfo.TimeSecondsUntilPCKick);
                _worldPacket.WriteInt32(SuccessInfo.AvailableClasses?.Count ?? 0);
                _worldPacket.WriteInt32(SuccessInfo.Templates.Count);
                _worldPacket.WriteUInt32(SuccessInfo.CurrencyID);

                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.BillingType);
                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.MinutesRemaining);
                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.RealBillingType);
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.IsInIGR);
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.IsPaidForByIGR);
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.IsCAISEnabled);
                _worldPacket.FlushBits();

                _worldPacket.WriteInt64(SuccessInfo.Time);

                foreach (VirtualRealmInfo virtualRealm in SuccessInfo.VirtualRealms)
                    virtualRealm.Write(_worldPacket);

                if (SuccessInfo.AvailableClasses != null)
                {
                    foreach (RaceClassAvailability raceAvailability in SuccessInfo.AvailableClasses)
                    {
                        _worldPacket.WriteUInt8((byte)raceAvailability.RaceID);
                        _worldPacket.WriteInt32(raceAvailability.Classes.Count);
                        foreach (ClassAvailability classAvailability in raceAvailability.Classes)
                        {
                            _worldPacket.WriteUInt8((byte)classAvailability.ClassID);
                            _worldPacket.WriteUInt8((byte)classAvailability.ActiveExpansionLevel);
                            _worldPacket.WriteUInt8((byte)classAvailability.AccountExpansionLevel);
                            _worldPacket.WriteUInt8((byte)classAvailability.MinActiveExpansionLevel);
                        }
                    }
                }

                foreach (CharacterTemplate characterTemplate in SuccessInfo.Templates)
                {
                    _worldPacket.WriteInt32(characterTemplate.TemplateSetId);
                    _worldPacket.WriteInt32(characterTemplate.Classes?.Count ?? 0);
                    if (characterTemplate.Classes != null)
                        foreach (CharacterTemplateClass templateClass in characterTemplate.Classes)
                        {
                            _worldPacket.WriteUInt8((byte)templateClass.ClassID);
                            _worldPacket.WriteUInt8((byte)templateClass.FactionGroup);
                        }

                    string name = characterTemplate.Name ?? string.Empty;
                    string description = characterTemplate.Description ?? string.Empty;
                    _worldPacket.WriteBits(name.GetByteCount(), 7);
                    _worldPacket.WriteBits(description.GetByteCount(), 10);
                    _worldPacket.FlushBits();
                    _worldPacket.WriteString(name);
                    _worldPacket.WriteString(description);
                }

                _worldPacket.WriteBit(SuccessInfo.IsExpansionTrial);
                _worldPacket.WriteBit(SuccessInfo.ForceCharacterTemplate);
                _worldPacket.WriteBit(SuccessInfo.NumPlayersHorde.HasValue);
                _worldPacket.WriteBit(SuccessInfo.NumPlayersAlliance.HasValue);
                _worldPacket.WriteBit(SuccessInfo.ExpansionTrialExpiration.HasValue);
                _worldPacket.WriteBit(SuccessInfo.CurrentBuild != null);
                _worldPacket.FlushBits();

                if (SuccessInfo.NumPlayersHorde.HasValue)
                    _worldPacket.WriteUInt16(SuccessInfo.NumPlayersHorde.Value);

                if (SuccessInfo.NumPlayersAlliance.HasValue)
                    _worldPacket.WriteUInt16(SuccessInfo.NumPlayersAlliance.Value);

                if(SuccessInfo.ExpansionTrialExpiration.HasValue)
                    _worldPacket.WriteInt64(SuccessInfo.ExpansionTrialExpiration.Value);

                if (SuccessInfo.CurrentBuild != null)
                {
                    for (int i = 0; i < 16; ++i)
                    {
                        _worldPacket.WriteUInt8(SuccessInfo.CurrentBuild.BuildKey[i]);
                        _worldPacket.WriteUInt8(SuccessInfo.CurrentBuild.ConfigKey[i]);
                    }
                }
            }

            if (WaitInfo.HasValue)
                WaitInfo.Value.Write(_worldPacket);            
        }

        public AuthSuccessInfo SuccessInfo; // contains the packet data in case that it has account information (It is never set when WaitInfo is set), otherwise its contents are undefined.
        public AuthWaitInfo? WaitInfo; // contains the queue wait information in case the account is in the login queue.
        public BattlenetRpcErrorCode Result; // the result of the authentication process, possible values are @ref BattlenetRpcErrorCode

        public class AuthSuccessInfo
        {
            public byte ActiveExpansionLevel; // the current server expansion, the possible values are in @ref Expansions
            public byte AccountExpansionLevel; // the current expansion of this account, the possible values are in @ref Expansions
            public uint TimeRested; // affects the return value of the GetBillingTimeRested() client API call, it is the number of seconds you have left until the experience points and loot you receive from creatures and quests is reduced. It is only used in the Asia region in retail, it's not implemented in TC and will probably never be.

            public uint VirtualRealmAddress; // a special identifier made from the Index, BattleGroup and Region. @todo implement
            public uint TimeSecondsUntilPCKick; // @todo research
            public uint CurrencyID; // this is probably used for the ingame shop. @todo implement
            public UnixTime64 Time;

            public GameTime GameTimeInfo;

            public List<VirtualRealmInfo> VirtualRealms = new();     // list of realms connected to this one (inclusive) @todo implement
            public List<CharacterTemplate> Templates = new(); // list of pre-made character templates. @todo implement

            public List<RaceClassAvailability> AvailableClasses; // the minimum AccountExpansion required to select the classes

            public bool IsExpansionTrial;
            public bool ForceCharacterTemplate; // forces the client to always use a character template when creating a new character. @see Templates. @todo implement
            public ushort? NumPlayersHorde; // number of horde players in this realm. @todo implement
            public ushort? NumPlayersAlliance; // number of alliance players in this realm. @todo implement
            public long? ExpansionTrialExpiration; // expansion trial expiration unix timestamp
            public NewBuild CurrentBuild;

            public struct GameTime
            {
                public uint BillingType;
                public uint MinutesRemaining;
                public uint RealBillingType;
                public bool IsInIGR;
                public bool IsPaidForByIGR;
                public bool IsCAISEnabled;
            }

            public class NewBuild
            {
                public byte[] BuildKey = new byte[16];
                public byte[] ConfigKey = new byte[16];
            }
        }
    }

    class WaitQueueUpdate : ServerPacket
    {
        public WaitQueueUpdate() : base(ServerOpcodes.WaitQueueUpdate) { }

        public override void Write()
        {
            WaitInfo.Write(_worldPacket);
        }

        public AuthWaitInfo WaitInfo;
    }

    class WaitQueueFinish : ServerPacket
    {
        public WaitQueueFinish() : base(ServerOpcodes.WaitQueueFinish) { }

        public override void Write() { }
    }

    class ConnectTo : ServerPacket
    {
        public ConnectTo() : base(ServerOpcodes.ConnectTo)
        {
            Payload = new ConnectPayload();
        }

        public override void Write()
        {
            // Forever layout: TC ConnectTo::Write (single ConnectPayload with empty BleepToken)
            // Classic 1.60.1 JAM ConnectTo layout. The older RSA-signed payload
            // makes the client interpret the signature as payload metadata.
            _worldPacket.WriteUInt32(1); // Payload count
            _worldPacket.WriteUInt32((uint)Serial);
            _worldPacket.WriteUInt8(Con);
            _worldPacket.WriteUInt64(unchecked((ulong)Key));
            _worldPacket.WriteUInt32(NativeRealmAddress);
            _worldPacket.WriteUInt32(Key3);

            _worldPacket.WriteUInt8((byte)Payload.Where.Type);

            switch (Payload.Where.Type)
            {
                case AddressType.IPv4:
                    _worldPacket.WriteBytes(Payload.Where.IPv4);
                    break;
                case AddressType.IPv6:
                    _worldPacket.WriteBytes(Payload.Where.IPv6);
                    break;
                case AddressType.NamedSocket:
                    _worldPacket.WriteCString(Payload.Where.NameSocket); // TC: data << char const* (NUL terminated)
                    break;
                default:
                    break;
            }

            _worldPacket.WriteUInt16(Payload.Port);

            // Empty BleepToken: empty token, empty proxy ID (one NUL byte),
            // empty address, and zero lifespan.
            _worldPacket.WriteBits(0, 5);
            _worldPacket.WriteBits(1, 24);
            _worldPacket.WriteBits(0, 6);
            _worldPacket.FlushBits();
            _worldPacket.WriteInt64(0);
            _worldPacket.WriteCString(string.Empty);
        }

        public long Key;
        public uint NativeRealmAddress;
        public uint Key3;
        public ConnectToSerial Serial;
        public ConnectPayload Payload;
        public byte Con;

        public class ConnectPayload
        {
            public SocketAddress Where;
            public ushort Port;
        }

        public struct SocketAddress
        {
            public AddressType Type;

            public byte[] IPv4;
            public byte[] IPv6;
            public string NameSocket;
        }

        public enum AddressType
        {
            IPv4 = 1,
            IPv6 = 2,
            NamedSocket = 3 // not supported by windows client
        }
    }

    class AuthContinuedSession : ClientPacket
    {
        const int DigestLength = 24;
        public AuthContinuedSession(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            // Forever 1.60 order: DosResponse, LocalChallenge[32], Digest[24], Key
            DosResponse = _worldPacket.ReadInt64();
            LocalChallenge = _worldPacket.ReadBytes(32);
            Digest = _worldPacket.ReadBytes(DigestLength);
            Key = _worldPacket.ReadInt64();
            NativeRealmAddress = _worldPacket.ReadUInt32();
            Key3 = _worldPacket.ReadUInt32();
        }

        public long DosResponse;
        public long Key;
        public uint NativeRealmAddress;
        public uint Key3;
        public byte[] LocalChallenge = new byte[32];
        public byte[] Digest = new byte[DigestLength];
    }

    class ResumeComms : ServerPacket
    {
        public ResumeComms(ConnectionType connection) : base(ServerOpcodes.ResumeComms, connection) { }

        public override void Write() { }
    }

    class ConnectToFailed : ClientPacket
    {
        public ConnectToFailed(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            // Forever layout: TC ConnectToFailed::Read
            Con = _worldPacket.ReadUInt8();
            Serial = (ConnectToSerial)_worldPacket.ReadUInt32();
        }

        public ConnectToSerial Serial;
        byte Con;
    }

    class EnterEncryptedMode : ServerPacket
    {
        byte[] EncryptionKey;
        bool Enabled;
        static byte[] expandedPrivateKey;

        static byte[] EnableEncryptionSeed =
        [
            0x66, 0xBE, 0x29, 0x79, 0xEF, 0xF2, 0xD5, 0xB5, 0x61, 0x53, 0xF6, 0x5F, 0x45, 0xAE, 0x81, 0xCB,
            0x32, 0xEC, 0x94, 0xEC, 0x75, 0xB3, 0x5F, 0x44, 0x6A, 0x63, 0x43, 0x67, 0x17, 0x20, 0x44, 0x34
        ];
        static byte[] EnableEncryptionContext = [0xA7, 0x1F, 0xB6, 0x9B, 0xC9, 0x7C, 0xDD, 0x96, 0xE9, 0xBB, 0xB8, 0x21, 0x39, 0x8D, 0x5A, 0xD4];

        static byte[] EnterEncryptedModePrivateKey =
        [
            0x08, 0xBD, 0xC7, 0xA3, 0xCC, 0xC3, 0x4F, 0x3F,
            0x6A, 0x0B, 0xFF, 0xCF, 0x31, 0xC1, 0xB6, 0x97,
            0x69, 0x1E, 0x72, 0x9A, 0x0A, 0xAB, 0x2C, 0x77,
            0xC3, 0x6F, 0x8A, 0xE7, 0x5A, 0x9A, 0xA7, 0xC9
        ];

        static EnterEncryptedMode()
        {
            expandedPrivateKey = Ed25519.ExpandedPrivateKeyFromSeed(EnterEncryptedModePrivateKey);
        }

        public int RegionGroup = 8; // WoW Forever's working 1.60.1 client expects region group 8.

        public EnterEncryptedMode(byte[] encryptionKey, bool enabled) : base(ServerOpcodes.EnterEncryptedMode)
        {
            EncryptionKey = encryptionKey;
            Enabled = enabled;
        }

        public override void Write()
        {
            byte[] toSign;
            using (HMACSHA512 hmac = new(EncryptionKey))
            {
                hmac.TransformBlock([(byte)(Enabled ? 1 : 0)], 0, 1, null, 0);
                hmac.TransformFinalBlock(EnableEncryptionSeed, 0, EnableEncryptionSeed.Length);
                toSign = hmac.Hash;
            }

            _worldPacket.WriteInt32(RegionGroup);
            _worldPacket.WriteBytes(Ed25519.Sign(toSign, expandedPrivateKey, 0, EnableEncryptionContext));
            _worldPacket.WriteBit(Enabled);
            _worldPacket.FlushBits();
        }
    }

    //Structs
    public struct AuthWaitInfo
    {
        public void Write(WorldPacket data)
        {
            data.WriteUInt32(WaitCount);
            data.WriteUInt32(WaitTime);
            data.WriteUInt8(AllowedFactionGroupForCharacterCreate);
            data.WriteBit(HasFCM);
            data.WriteBit(CanCreateOnlyIfExisting);
            data.FlushBits();
        }

        public uint WaitCount; // position of the account in the login queue
        public uint WaitTime; // Wait time in login queue in minutes, if sent queued and this value is 0 client displays "unknown time"
        public byte AllowedFactionGroupForCharacterCreate;
        public bool HasFCM; // true if the account has a forced character migration pending. @todo implement
        public bool CanCreateOnlyIfExisting;
    }

    struct VirtualRealmNameInfo
    {
        public VirtualRealmNameInfo(bool isHomeRealm, bool isInternalRealm, string realmNameActual, string realmNameNormalized)
        {
            IsLocal = isHomeRealm;
            IsInternalRealm = isInternalRealm;
            RealmNameActual = realmNameActual;
            RealmNameNormalized = realmNameNormalized;
        }

        public void Write(WorldPacket data)
        {
            data.WriteBit(IsLocal);
            data.WriteBit(IsInternalRealm);
            data.WriteBits(RealmNameActual.GetByteCount(), 8);
            data.WriteBits(RealmNameNormalized.GetByteCount(), 8);
            data.FlushBits();

            data.WriteString(RealmNameActual);
            data.WriteString(RealmNameNormalized);
        }

        public bool IsLocal;                    // true if the realm is the same as the account's home realm
        public bool IsInternalRealm;            // @todo research
        public string RealmNameActual;     // the name of the realm
        public string RealmNameNormalized; // the name of the realm without spaces
    }

    struct VirtualRealmInfo
    {
        public VirtualRealmInfo(uint realmAddress, bool isHomeRealm, bool isInternalRealm, string realmNameActual, string realmNameNormalized)
        {

            RealmAddress = realmAddress;
            RealmNameInfo = new VirtualRealmNameInfo(isHomeRealm, isInternalRealm, realmNameActual, realmNameNormalized);
        }

        public void Write(WorldPacket data)
        {
            data.WriteUInt32(RealmAddress);
            RealmNameInfo.Write(data);
        }

        public uint RealmAddress;             // the virtual address of this realm, constructed as RealmHandle::Region << 24 | RealmHandle::Battlegroup << 16 | RealmHandle::Index
        public VirtualRealmNameInfo RealmNameInfo;
    }
}
