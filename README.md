<a href="https://buymeacoffee.com/caffeine204">
  <img src="https://www.owlstown.com/assets/icons/bmc-yellow-button-941f96a1.png" width="200" alt="Buy me a coffee">
</a>

# EmuForever

**EmuForever** is an independent, C# / **.NET 10** World of Warcraft: Forever server emulator built on **CypherCore** foundation. Its goal is a playable, persistent world enhanced by **SIPlayers (Super Intelligence Players)**: autonomous characters that behave, learn, and interact like real players.

> **Status:** Active development. Core gameplay and server infrastructure are functional; feature completeness and client compatibility continue to improve.

## Architecture

```text
WoW: Forever Client
        │
        ▼
Battle.not (BNot) ── Authentication / Accounts / Realms
        │
        ▼
CypherCore World Server (.NET 10)
        │  Combat • Movement • Quests • Spells • Social • Persistence
        │
        ├──────────────► SQL Databases (Auth / Characters / World / Hotfixes)
        │
        └──── gRPC / Protocol Buffers ────► SI Server (.NET 10)
                                          │
                                          ├─ SIPlayer goals and decisions
                                          ├─ Local LLM inference
                                          ├─ Memory and relationships
                                          └─ Dedicated SI database
```

The **World server** remains authoritative for gameplay, movement, combat, and game rules. The separate **SI server** handles higher-level reasoning, planning, personality, conversation, and long-term memory. **gRPC / Protobuf** provides a typed communication contract between them.

## Component Status


| Component                       | Status                                                 | Estimated Progress |
| ------------------------------- | ------------------------------------------------------ | ------------------ |
| **Battle.not (BNot)**           | Mostly working; protocol refinement                    | ~95%               |
| **CypherCore World (.NET 10)**  | Playable; ongoing compatibility and bug fixes          | ~80%               |
| **Databases / DB2**             | Core integration working; data fixes ongoing           | ~85%               |
| **Battle.not Launcher**         | Functional foundation; updates and polish pending      | ~70%               |
| **SIPlayers / SI Server**       | Primary development focus; integration in progress     | In development     |
| **gRPC / Protobuf + Local LLM** | Target architecture; implementation/validation ongoing | In development     |


*These are informal development estimates, not tested coverage metrics.*

## SIPlayers — Main Project Goal

SIPlayers are intended to be persistent, autonomous characters rather than scripted NPCs or simple combat bots. They should be able to:

- **Play:** quest, level, explore, grind, farm, fight, loot, equip gear, and learn professions.
- **Socialize:** chat, form friendships and rivalries, group, trade, duel, join guilds, and interact with human players.
- **Decide:** pursue individual goals using personalities, preferences, world knowledge, and context.
- **Remember:** maintain experiences, relationships, goals, and history in a dedicated **SI database**.
- **Learn and converse:** use a **fully local LLM** without requiring a cloud AI provider.

Time-sensitive gameplay actions stay inside the World server; the SI server supplies strategic decisions and conversational intelligence without blocking the game loop.

## Technology


| Area                  | Stack                                        |
| --------------------- | -------------------------------------------- |
| Language / Runtime    | C# / .NET 10                                 |
| Game server           | CypherCore-derived World server              |
| Authentication        | Battle.not (BNot)                            |
| Service communication | gRPC / Protocol Buffers (SI integration)     |
| Persistence           | SQL databases + dedicated SI memory store    |
| Intelligence          | Local LLM + custom SIPlayer behavior systems |
| Client target         | World of Warcraft: Forever 1.60.1.x          |




## Roadmap

- [x] Move from the original scratch-built core to CypherCore
- [x] Upgrade server projects to .NET 10
- [x] Establish Battle.not authentication and World connectivity
- [x] Establish core character and gameplay functionality
- [x] Continue Forever client, opcode, DB2, and gameplay compatibility fixes
- [ ] Finish and harden gRPC / Protobuf World ↔ SI communication
- [ ] Expand SIPlayer activities, planning, and human-like behavior
- [ ] Integrate and validate fully local LLM inference and persistent memory
- [x] Optimize World server networking, ticks, and scalability
- [ ] Complete launcher updating, deployment, and documentation



## Build

Requires the **.NET 10 SDK**, a compatible development environment, configured SQL databases, and a supported WoW: Forever client.

```bash
dotnet restore
dotnet build -c Release
```

Configure and start Battle.not, World, and any enabled SI services using their respective project settings. Database initialization and client configuration are installation-specific.

## Disclaimer

EmuForever is an independent, fan-made research and interoperability project. It is **not affiliated with or endorsed by Blizzard Entertainment**. World of Warcraft, Warcraft, and Battle.net are trademarks of their respective owners. No proprietary Blizzard server software is distributed by this project.

---

**EmuForever — One world. Real players. Intelligent players.**

<a href="https://buymeacoffee.com/caffeine204">
  <img src="https://www.owlstown.com/assets/icons/bmc-yellow-button-941f96a1.png" width="200" alt="Buy me a coffee">
</a>
