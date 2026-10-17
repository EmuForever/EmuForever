# EmuForever

**EmuForever** is an open-source, clean-room **World of Warcraft: Forever** server emulator written entirely in **C# / .NET 8**.

The goal of the project is to recreate the server-side infrastructure required to run **World of Warcraft: Forever 1.60.1.xxxxx**, including Battle.net authentication, world services, intelligent AI systems, the game launcher, and the underlying database architecture.

> ⚠️ **Early Development**
>
> EmuForever is currently under active development. Large portions of the emulator are incomplete and should be considered experimental.

---

## 🎮 Project Goal

The long-term goal of EmuForever is to provide a complete, standalone server ecosystem capable of hosting **World of Warcraft: Forever** without requiring Blizzard's original server infrastructure.

The project is being developed as multiple independent applications that communicate with each other through defined interfaces and network protocols.

### Architecture

```text
                         ┌─────────────────────────┐
                         │    Battle.not Launcher  │
                         │      Game Launcher      │
                         └────────────┬────────────┘
                                      │
                                      │ Authentication /
                                      │ Game Launch
                                      ▼
                         ┌─────────────────────────┐
                         │       Battle.not        │
                         │    Battle.net Emulator  │
                         │                         │
                         │ Authentication          │
                         │ Sessions                │
                         │ Realm Services          │
                         │ Account Services        │
                         └────────────┬────────────┘
                                      │
                                      │ Game Session
                                      ▼
                         ┌─────────────────────────┐
                         │          World          │
                         │      World Server       │
                         │                         │
                         │ Players                 │
                         │ Creatures               │
                         │ Maps                    │
                         │ Spells                  │
                         │ Combat                  │
                         │ Quests                  │
                         │ Items                   │
                         │ etc.                    │
                         └────────────┬────────────┘
                                      │
                                      │ AI / Decisions
                                      ▼
                         ┌─────────────────────────┐
                         │           SI            │
                         │   Super Intelligence   │
                         │                         │
                         │ AI Players              │
                         │ NPC Intelligence        │
                         │ Decision Making         │
                         │ Behavior                │
                         │ Social Interaction      │
                         └────────────┬────────────┘
                                      │
                                      ▼
                         ┌─────────────────────────┐
                         │         Database        │
                         │                         │
                         │ Accounts                │
                         │ Characters              │
                         │ World Data              │
                         │ Items                   │
                         │ Spells                  │
                         │ Quests                  │
                         │ AI Data                 │
                         └─────────────────────────┘
```

---

# 🧩 Components

EmuForever consists of four primary applications/layers.

## 1. Battle.not

**Status: 99% — Working**

Battle.not is the Battle.net-compatible server layer responsible for handling the services required before the game connects to the World server.

### Current responsibilities

* Battle.net authentication
* Client connections
* Account authentication
* Session management
* Realm/server information
* Battle.net protocol handling
* TLS communication
* Game server handoff
* Client/server communication
* Authentication services

The name **Battle.not** intentionally references Battle.net while being a completely independent implementation.

```text
WoW Forever Client
        │
        ▼
    Battle.not
        │
        ├── Authentication
        ├── Session
        ├── Realm
        └── Game Server Assignment
                    │
                    ▼
                  World
```

### Current Status

**99%**

Battle.not is currently the most mature component of the project and is substantially functional.

---

# 🌎 2. World

**Status: 25% — In Development**

The World application is the primary game server.

Its purpose is to provide the actual persistent World of Warcraft game environment once the client has authenticated through Battle.not.

### Planned functionality

* Player sessions
* Character loading
* Character creation
* Character deletion
* Movement
* Maps
* Zones
* Phasing
* Creatures
* NPCs
* GameObjects
* Spells
* Combat
* Items
* Inventory
* Equipment
* Quests
* Achievements
* Skills
* Professions
* Classes
* Races
* Factions
* Groups
* Guilds
* Mail
* Auction House
* Vendors
* Trainers
* World states
* Weather
* Time
* Chat
* Social systems
* Instances
* Dungeons
* Raids
* Battlegrounds
* World events
* Scripts
* AI
* Persistence

The World server will eventually provide the majority of the actual gameplay functionality required by the Forever client.

### Current Status

**25%**

Core architecture and foundational systems are under active development.

---

# 🚀 4. Battle.not Game Launcher

**Status: 50% — In Development**

The Battle.not Game Launcher is the client-side launcher for EmuForever.

Its purpose is to provide a simple interface for installing, configuring, updating, and launching the World of Warcraft: Forever client against an EmuForever server.

### Planned functionality

* Server selection
* Account login
* Client detection
* Client version detection
* Installation management
* File verification
* Patch management
* Server configuration
* Realm selection
* Game launching
* Launcher updates
* Configuration management
* Download/update system
* Server status
* News
* Server information

The launcher is designed to provide an experience similar to modern game launchers while remaining independent from Blizzard's infrastructure.

### Current Status

**50%**

The launcher UI and core architecture are under development.

---

# 🗄️ Database

**Status: 50% — In Development**

The database layer provides persistent storage for EmuForever.

The database is designed to support both the traditional MMORPG data model and future AI-related data.

### Planned data

```text
Accounts
Characters
Character Progression
Inventory
Items
Mail
Guilds
Groups
Quests
Achievements
Spells
Skills
Professions
NPCs
Creatures
GameObjects
World States
Maps
Instances
Battlegrounds
AI Profiles
AI Memories
AI Goals
AI Relationships
AI Statistics
Server Configuration
```

The database architecture is intended to remain modular so additional systems can be introduced without requiring major changes to the core server.

### Current Status

**50%**

Database structure and integration are actively being developed.

---

# 🏗️ Technology

EmuForever is built using modern Microsoft .NET technologies.

| Component      | Technology                         |
| -------------- | ---------------------------------- |
| Language       | C#                                 |
| Runtime        | .NET 8                             |
| Platform       | Windows / Cross-platform capable   |
| Architecture   | Client / Server                    |
| Networking     | Native .NET networking             |
| Database       | SQL-based                          |
| Authentication | Battle.net-compatible services     |
| World Server   | Custom C# implementation           |
| AI             | Custom SI architecture             |
| Launcher       | C# / .NET                          |
| Development    | Visual Studio / JetBrains / Cursor |

The project is intentionally written in **native C#/.NET**.

No C++ emulator core is required by the project.

---

# 📊 Development Status

| Component                |         Status | Progress |
| ------------------------ | -------------: | -------: |
| Battle.not               |     🟢 Working |  **99%** |
| World                    | 🟡 Development |  **25%** |
| SI                       |     🔴 Planned |   **0%** |
| Database                 | 🟡 Development |  **50%** |
| Battle.not Game Launcher | 🟡 Development |  **50%** |

### Overall Project

**Active Development**

The project is currently focused on establishing a stable Battle.net → World authentication and gameplay pipeline before expanding into the more advanced systems.

---

# 🎯 Roadmap

## Phase 1 — Battle.net

* [x] Battle.net server architecture
* [x] Client connection
* [x] TLS infrastructure
* [x] Authentication architecture
* [x] Session handling
* [x] Realm information
* [x] Game server handoff
* [ ] Final protocol cleanup
* [ ] Complete compatibility testing

**Progress: ~99%**

---

## Phase 2 — World Server

* [x] World server architecture
* [x] Client connection
* [x] World authentication
* [ ] Player sessions
* [ ] Character system
* [ ] World state
* [x] Movement
* [ ] Maps
* [ ] Creatures
* [x] NPCs
* [x] Items
* [ ] Spells
* [ ] Combat
* [ ] Quests
* [x] Inventory
* [x] Chat
* [ ] Guilds
* [ ] Groups
* [ ] Instances
* [ ] Battlegrounds
* [ ] Persistence

**Progress: ~25%**

---

## Phase 3 — Database

* [x] Database architecture
* [x] Initial schema
* [x] Account storage
* [x] Character storage
* [ ] World data
* [x] Item persistence
* [x] Quest persistence
* [x] Guild persistence
* [ ] AI persistence
* [ ] Database optimization

**Progress: ~50%**

---

## Phase 4 — Game Launcher

* [x] Launcher architecture
* [x] Basic UI
* [x] Client detection
* [x] Server configuration
* [x] Login
* [x] Client validation
* [x] File verification
* [ ] Updating
* [x] Game launching
* [x] Server status
* [ ] News/update system

**Progress: ~50%**

---

# 🔐 Clean-Room Development

EmuForever is developed as an independent implementation.

The project does not aim to redistribute Blizzard server software or proprietary server-side code.

The goal is to implement compatible functionality through:

* Protocol analysis
* Client behavior analysis
* Publicly available information
* Server/client interaction
* Open-source research
* Original C# implementations
* Independent database structures
* Testing against the client

---

# 🧪 Development Philosophy

EmuForever follows several principles.

### Native C#

The emulator is written in C# using modern .NET rather than wrapping an existing C++ emulator.

### Modular

Major services should be independently executable and replaceable.

### Observable

Networking and server behavior should be easy to debug and inspect.

### Data Driven

Game systems should rely heavily on database/configuration data rather than hard-coded values.

### Extensible

New systems should be possible without redesigning the entire server.

### AI Ready

The World server is being designed with future integration with SI in mind.

---

# 🛠️ Building

### Requirements

* Windows 10/11
* .NET 8 SDK
* Visual Studio 2022 or newer
* Git
* SQL database server
* World of Warcraft: Forever client

Clone the repository:

```bash
git clone https://github.com/YOUR_USERNAME/EmuForever.git
cd EmuForever
```

Build the solution:

```bash
dotnet build
```

Run the individual services from their respective projects.

> Configuration and database setup instructions will be added as the emulator becomes ready for public testing.

---

# 📌 Current Focus

Current development is focused on completing the fundamental client connection pipeline:

```text
Launcher
    ↓
Battle.not
    ↓
Authentication
    ↓
Realm
    ↓
World
    ↓
Character
    ↓
Playable Game
```

Once this foundation is stable, development can move toward implementing the larger gameplay systems.

---

# 🗺️ Long-Term Goal

The ultimate objective is a complete standalone **World of Warcraft: Forever server ecosystem**.

That means eventually supporting:

* Account creation
* Authentication
* Character creation
* Character progression
* Persistent worlds
* NPCs
* Creatures
* Combat
* Classes
* Races
* Spells
* Items
* Quests
* Dungeons
* Raids
* PvP
* Battlegrounds
* Guilds
* Economy
* Professions
* Social systems
* World events
* Scripts
* AI players
* Autonomous characters
* Human players
* AI + human interaction
* Complete server administration

The end goal is simple:

> **Run World of Warcraft: Forever on infrastructure controlled entirely by EmuForever.**

---

# 🚧 Disclaimer

EmuForever is an independent fan-made server emulator project.

**EmuForever is not affiliated with, endorsed by, sponsored by, or otherwise associated with Blizzard Entertainment.**

World of Warcraft, Warcraft, Battle.net, and related names and trademarks are property of their respective owners.

This project is intended for research, educational, interoperability, and private server development purposes.

Users are responsible for ensuring that their use of the project and game client complies with applicable laws, licenses, and terms of service.

---

# 📜 License

License information will be added as the project matures.

---

# ⭐ Project Status

**EmuForever is actively being developed.**

Current estimated progress:

```text
Battle.not              ███████████████████▊ 99%
World                   █████░░░░░░░░░░░░░░░ 25%
SI                      ░░░░░░░░░░░░░░░░░░░░  0%
Database                ██████████░░░░░░░░░░ 50%
Game Launcher           ██████████░░░░░░░░░░ 50%
```

### The destination

```text
                    EMUFOREVER

             ┌─────────────────────┐
             │    World of         │
             │   Warcraft: Forever │
             └──────────┬──────────┘
                        │
                 ┌──────▼──────┐
                 │ Battle.not  │
                 └──────┬──────┘
                        │
                 ┌──────▼──────┐
                 │   World/SI  │
                 └──────┬──────┘
                        │              
                        ▼ 
                  ┌───────────┐       
                  │ Database  │       
                  └───────────┘      
                               
                      
```

**One client. One world. One emulator.**

**EmuForever.**
