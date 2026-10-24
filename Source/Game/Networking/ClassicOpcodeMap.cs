using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Game.Networking
{
    // The core's named opcodes follow TrinityCore's forever header. The Classic
    // client uses a different wire numbering; keep that difference at the socket.
    internal static class ClassicOpcodeMap
    {
        static readonly Dictionary<uint, uint> ClientToCore = new();
        static readonly Dictionary<uint, uint> ServerToClient = new();

        static ClassicOpcodeMap()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "Game.Networking.opcode-provenance-70425.json")
                ?? throw new InvalidDataException("Classic opcode map resource is missing.");
            using JsonDocument document = JsonDocument.Parse(stream);

            foreach (JsonProperty entry in document.RootElement.GetProperty("ClientOpcodes").EnumerateObject())
            {
                if (!Enum.TryParse(entry.Name, out ClientOpcodes coreOpcode) || coreOpcode == ClientOpcodes.Unknown)
                    continue;

                string wireValue = entry.Value.GetProperty("opcode").GetString();
                uint wireOpcode = uint.Parse(wireValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ClientToCore.Add(wireOpcode, (uint)coreOpcode);
            }

            foreach (JsonProperty entry in document.RootElement.GetProperty("ServerOpcodes").EnumerateObject())
            {
                if (!Enum.TryParse(entry.Name, out ServerOpcodes coreOpcode) || coreOpcode == ServerOpcodes.Unknown)
                    continue;

                string wireValue = entry.Value.GetProperty("opcode").GetString();
                uint wireOpcode = uint.Parse(wireValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ServerToClient.Add((uint)coreOpcode, wireOpcode);
            }
        }

        public static uint ToCore(uint wireOpcode) => ClientToCore.GetValueOrDefault(wireOpcode, wireOpcode);
        public static uint ToClient(uint coreOpcode) => ServerToClient.GetValueOrDefault(coreOpcode, coreOpcode);
    }
}
