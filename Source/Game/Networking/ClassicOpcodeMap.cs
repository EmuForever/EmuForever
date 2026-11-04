using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Game.Networking
{
    // The core's named opcodes follow EmuForever's forever header. The Classic
    // client uses a different wire numbering; keep that difference at the socket.
    internal static class ClassicOpcodeMap
    {
        static readonly Dictionary<uint, uint> ClientToCore = new();
        static readonly Dictionary<uint, uint> ServerToClient = new();
        static readonly Dictionary<uint, uint> Client70755ToCore = new();
        static readonly Dictionary<uint, uint> ServerTo70755 = new();

        static ClassicOpcodeMap()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "opcode-map.json")
                ?? throw new InvalidDataException("Classic opcode map resource is missing.");
            using JsonDocument document = JsonDocument.Parse(stream);

            foreach (JsonProperty entry in document.RootElement.GetProperty("ClientOpcodes").EnumerateObject())
            {
                if (!Enum.TryParse(entry.Name, out ClientOpcodes coreOpcode) || coreOpcode == ClientOpcodes.Unknown)
                    continue;

                string wireValue = entry.Value.GetProperty("opcode").GetString();
                uint wireOpcode = uint.Parse(wireValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ClientToCore.Add(wireOpcode, (uint)coreOpcode);
                Client70755ToCore.Add(Get70755ClientWireOpcode(wireOpcode), (uint)coreOpcode);
            }

            foreach (JsonProperty entry in document.RootElement.GetProperty("ServerOpcodes").EnumerateObject())
            {
                if (!Enum.TryParse(entry.Name, out ServerOpcodes coreOpcode) || coreOpcode == ServerOpcodes.Unknown)
                    continue;

                string wireValue = entry.Value.GetProperty("opcode").GetString();
                uint wireOpcode = uint.Parse(wireValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ServerToClient.Add((uint)coreOpcode, wireOpcode);
                ServerTo70755.Add((uint)coreOpcode, Get70755ServerWireOpcode(wireOpcode));
            }
        }

        // Verified against 70755's message getters and serializers. The shifts
        // are inside two groups. Creation, enumeration, movement, chat and
        // transport retain their existing numbers. Older builds keep their map.
        internal static uint Get70755ClientWireOpcode(uint wireOpcode) =>
            wireOpcode >= 0x440071 && wireOpcode <= 0x44FFFF ? wireOpcode + 2 : wireOpcode;

        // AddonListRequest is the first affected named message in the base map.
        // Earlier named messages, including the mail responses, are unchanged.
        internal static uint Get70755ServerWireOpcode(uint wireOpcode) =>
            wireOpcode >= 0x4700EC && wireOpcode <= 0x47FFFF ? wireOpcode + 1 : wireOpcode;

        // Builds 70755 and newer use this wire profile; older builds retain their base map.
        internal static bool Uses70755WireProfile(uint clientBuild) => clientBuild >= 70755;

        public static uint ToCore(uint wireOpcode, uint clientBuild = 0) =>
            (Uses70755WireProfile(clientBuild) ? Client70755ToCore : ClientToCore).GetValueOrDefault(wireOpcode, wireOpcode);

        public static uint ToClient(uint coreOpcode, uint clientBuild = 0) =>
            (Uses70755WireProfile(clientBuild) ? ServerTo70755 : ServerToClient).GetValueOrDefault(coreOpcode, coreOpcode);
    }
}
