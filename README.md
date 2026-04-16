# BocikPG 🎵

A feature-rich Discord bot built with **C# / .NET**, **DSharpPlus**, and **Lavalink4NET** — born from skepticism, finished with a changed mind.

> *This is my first complete application built end-to-end with AI assistance (Claude, a bit of DeepSeek — Gemini didn't survive contact). Going in as an AI skeptic, I came out a convert. The code you're reading is the proof.*

---

## Features

### 🎵 Soundboard
The heart of the bot. Post an interactive button panel into any channel — members join a voice channel and click to play sounds instantly.

- Buttons auto-generated from a JSON sound definition file
- Supports custom **emoji**, **volume per sound**, and **name editing** via slash commands
- Paginated across multiple Discord messages when the board grows large
- `/soundboard create` · `update` · `destroy` · `edit` · `emotes`

### 🔊 Per-User Join / Leave Sounds
Every user can have a dedicated sound that plays when they join or leave a voice channel. Falls back to a **random sound** from the board when no assignment exists.

- `/usersound set-join` · `set-leave` · `clear-join` · `clear-leave` · `clear-all` · `info`
- Autocomplete on sound names

### 📊 Sound Stats
Tracks every play — by guild, user, sound, and trigger source (Soundboard, JoinSound, LeaveSound, Random).

- `/soundstats` with optional filters for user, sound name, and source
- Configurable top-N leaderboard (up to 25)
- **Share publicly** button to post results to the channel
- Periodic flush to disk + Git; manual `/soundstatsflush` available

### ☁️ Git Sync
All bot data (sounds, stats, keyword responses, ping configs…) lives in a `Resources/` folder that is **automatically committed and pushed** to a GitHub repository.

- Pull on demand with `/sync pull` — conflict detection with resolution buttons (keep local / keep remote / force pull)
- All services implement `IReloadable` and hot-reload after a pull — **no restart needed**
- PAT-based auth, configurable branch and author identity

### 🤖 Smart Voice Channel Management
The bot follows the humans. It evaluates every voice channel on voice-state changes, scores each by configurable **user weights**, and moves itself to the highest-scoring channel automatically.

- `/voice setweight` · `removeweight` · `listweights`
- Reconnects automatically if the Lavalink player is destroyed

### 💬 Keyword & Random Responses
- Keyword → weighted random response mapping, fully manageable via slash commands
- Per-user random response chance and response pool (`/random chance` · `add` · `remove` · `list`)

### 🏓 Ping Handling
Responds when mentioned. Supports per-user custom responses, ping-count throttling with configurable timeout, optional server timeout (mute), and gradual count decay.

### ⚡ Dynamic Commands
Define simple slash commands (name + description + response) in a JSON file — no recompilation needed. Loaded at startup.

---

## Stack

| Layer | Technology |
|---|---|
| Language | C# / .NET 10 |
| Discord | DSharpPlus 5.x |
| Audio | Lavalink4NET + Lavalink 4 |
| Persistence | JSON files + Git (via LibGit2Sharp) |
| Container | Docker + Docker Compose |

---

## Quick Start

### Prerequisites
- Docker & Docker Compose
- A Discord bot token
- A GitHub PAT (optional, for Git sync)

### 1. Clone & configure

```bash
git clone https://github.com/yourname/BocikPG.git
cd BocikPG
cp _env.example .env
```

Edit `.env`:

```env
DISCORD_TOKEN=your_token_here
DISCORD_OWNER_ID=your_discord_id
LAVALINK_PASSWORD=youshallnotpass
SYNC__REMOTE=https://<PAT>@github.com/yourname/your-resources-repo.git
```

### 2. Run

```bash
docker compose up -d
```

The bot will initialise the `Resources/` Git repository on first start, pull from remote if configured, then connect to Discord.

### 3. Set up the soundboard

1. Add sound files to `Resources/Soundboard/Sounds/`
2. Define them in `Resources/Soundboard/soundboard.json`
3. Run `/soundboard create` in the channel you want the board to live in

---

## Configuration

All settings are driven by environment variables (Docker Compose) or `appsettings.json`. Key sections:

| Section | Purpose |
|---|---|
| `Discord` | Token, owner ID |
| `Lavalink` | Host, port, password |
| `Soundboard` | Paths to sound files and JSON definitions |
| `SoundStats` | Stats file path, flush interval |
| `Voice` | Auto-join toggle, user weights file |
| `GitSync` | Remote URL, branch, PAT, author identity |
| `Ping` | Max pings, timeout, decay interval |
| `DynamicCommand` | Path to dynamic commands JSON |

---

## License

MIT — do whatever you want with it.
