# Toni Breidinger Racing

A top-down racing game with a Windows arcade edition and a high-resolution browser edition for Chromebooks and PCs. In the Windows game, you play as NASCAR driver **Toni Breidinger**: race eight courses against three rivals, fire missiles, hit zippers, collect upgrades and spell **T-O-N-I**. Eligible Windows races earn **Arcade1870 (A1870)** tokens from the same reward treasury as [Crypto Hockey](https://github.com/jcampbell1870/Crypto-Hockey).

> Unofficial fan game. It is not affiliated with or endorsed by Toni Breidinger, NASCAR or Nintendo. The rival drivers are fictional.

## Features

- **NES-style graphics and sound.** Everything is drawn into a 256×240 frame with a 40-colour NES-style palette and scaled up with crisp pixels. The sound effects are square-wave and noise chiptunes.
- **R.C. Pro-Am racing.** Top-down tracks with curbs and barriers, plus:
  - **Zippers** give a speed boost.
  - **Oil slicks** spin you out.
  - **Puddles** slow you down.
  - **Missile crates** refill your ammo, and missiles spin out the rivals.
- **Eight courses:** Sunshine Speedway, Desert Dunes, Pine Ridge Raceway, Harbor Hairpins, Midnight Mile, Thunder Valley, Snowbird Summit and the 4-lap Breidinger Grand Prix.
- **Season rules like the original:**
  - Finish in the top 3 to advance. Otherwise you spend one of your 2 continues.
  - Points are 10/6/4/1 for 1st to 4th.
  - Engine, tire and top-speed upgrades stay on Toni's car (up to level 3 each).
  - Spell **T-O-N-I** with letters found on the tracks to unlock the **Toni Turbo**. Spelling it again earns a bonus continue.
  - Win the final course to become Season Champion. The season then loops with faster rivals.
- **Your profile is saved** in `%APPDATA%\ToniBreidingerRacing\player.json`: races, wins, best times per track, season titles and wallet.

## Controls

| Key | Action |
| --- | --- |
| Up arrow / Z | Gas |
| Down arrow | Brake / reverse |
| Left / Right arrows | Steer |
| Space / X | Fire missile |
| Enter | Confirm / start |
| Esc | Pause / back |
| W | Set A1870 wallet |
| C | Claim A1870 reward with MetaMask |
| R | Retry a failed reward request |
| F11 / Alt+Enter | Toggle full screen |
| F10 | Toggle pixel-perfect / smooth display (Windows) |
| F9 | Open the browser multiplayer lobby (Windows) |

## Play online and download

**GitHub Pages address:** <https://jcampbell1870.github.io/Toni-Breidinger-Racing/>

The address becomes available after the changes are merged to `main`, a repository administrator selects **Settings → Pages → Source → GitHub Actions**, and **Publish game and downloads to Pages** succeeds. This repository change does not itself enable Pages or deploy a multiplayer server.

The site includes two direct downloads, built from the same commit as the site:

- [Windows PC ZIP](https://jcampbell1870.github.io/Toni-Breidinger-Racing/downloads/ToniBreidingerRacing-win-x64.zip): extract the complete archive and run `ToniBreidingerRacing.exe`. Windows x64; the .NET runtime is bundled. This is the original arcade season game.
- [Chromebook ZIP](https://jcampbell1870.github.io/Toni-Breidinger-Racing/downloads/ToniBreidingerRacing-chromebook.zip): extract it and open `index.html` in Chrome for offline practice. This is the browser edition, not a Windows executable or a ChromeOS native binary. For online play, open the hosted Pages site (or serve the files from an explicitly allowed HTTP(S) origin); the server intentionally rejects opaque `file://` origins.

The browser edition runs on Windows too, so **both platforms use the same browser lobby for multiplayer**. The native Windows executable does not connect to online matches.

### Graphics and lobby upgrade

The browser edition uses a high-DPI canvas, quality presets, smooth animation, car shadows and racing effects rather than enlarging a fixed NES framebuffer. Choose a quality level suited to your Chromebook or PC; output resolution and frame rate depend on the device and browser. The Windows edition retains its retro artwork, with pixel-perfect or smooth scaling and full-screen, per-monitor DPI support. These upgrades do not claim photorealistic 3D graphics or unlimited hardware performance.

The original dark racing lobby uses red/gold accents, room listings, ready seats and tournament brackets inspired by poker-client layouts. It does not use GGPoker branding, proprietary assets, gambling or entry fees.

### Live multiplayer

- **1v1:** two humans join a room and ready up for a live race.
- **Eight-person tournament:** eight humans ready up; four quarterfinals lead to two semifinals and a final. Results and advancement are decided by the server, not submitted by clients.
- The server uses the Windows game's track library and physics, a fixed-step simulation and live WebSocket snapshots. Practice opponents are AI; online seats are never filled with fake players.
- Disconnects forfeit active races. Input expires when a client stops sending controls; race timeouts prevent abandoned matches from blocking the tournament.
- Browser/online races do **not** request A1870 rewards. The existing wallet/reward flow remains exclusive to the Windows season game.

**GitHub Pages only hosts static files.** For live online play, deploy `src/ToniBreidingerRacing.Server` to a host that supports long-lived WebSockets and .NET 10 (or its included Docker image). Configure TLS and the allowed browser origins, then enter the server's `wss://YOUR-HOST/ws` address in the lobby's connection settings. Until that server is deployed, offline practice and downloads work but live rooms are unavailable.

Local development:

```sh
dotnet run --project src/ToniBreidingerRacing.Server --urls http://localhost:5080
python3 -m http.server 8080 --directory web
```

Open `http://localhost:8080` and connect to `ws://localhost:5080/ws`. Use separate tabs/browser profiles for two racers, or eight for a tournament.

Production server example:

```sh
docker build -f src/ToniBreidingerRacing.Server/Dockerfile -t toni-racing-server .
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e AllowedOrigins__0=https://jcampbell1870.github.io \
  toni-racing-server
```

Put a TLS reverse proxy in front of the container and enable WebSocket upgrades on `/ws`. The origin is the **scheme and host**, not the Pages repository path. Configure additional origins explicitly for other sites. Do not expose development origin rules to the public internet. `/health` is available for health checks.

Rooms are in-memory and anonymous: a restart clears matches and there is no account identity, persistent ranking, cross-instance matchmaking or reconnect recovery. Rooms are listed to everyone connected to that server; “private” refers to using your own host, not password-protected tables. Run one server instance; apply host-level connection/IP limits and monitoring before operating a large public service. A lobby display name is not proof of identity.

## Arcade1870 rewards (just for playing)

You earn A1870 for every race that lasts at least `MinimumRaceSeconds`, **whatever place you finish**. The reward setup is the same as Crypto Hockey's:

| Setting | Value |
| --- | --- |
| Token (A1870) | `0x8eddD4edea39c5B5f77662453600F53A202EE47C` |
| Reward vault (treasury) | `0x1e4f6e4a382adbdb662733a19ae773d3ab8f497d` |
| Claim issuer | `https://www.cryptohockey.org/api/reward-claim` |
| Reward per race | 10 A1870 |

How it works:

1. Press **W** and enter your Ethereum wallet address. It is saved in your profile.
2. When a race ends, the game sends the race result to the Crypto Hockey claim issuer. The issuer returns a signed claim for the `Arcade1870RewardVault`.
3. Press **C**. A local claim page opens in your browser (served only on `127.0.0.1`). Confirm the transaction in MetaMask to receive your A1870.

The signing key stays on the Crypto Hockey server. The game only stores public addresses. All of these values are in `src/ToniBreidingerRacing/appsettings.json`, along with `SoundEnabled` and `StartFullScreen`.

## Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/ToniBreidingerRacing          # play (Windows)
dotnet test tests/ToniBreidingerRacing.Tests            # run the tests (any OS)
dotnet publish src/ToniBreidingerRacing -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The game logic, renderer and rewards are all in the cross-platform `ToniBreidingerRacing.Core` library, so the tests also run on Linux and macOS. Only the thin WinForms window needs Windows.

Pushing a `v*` tag runs `.github/workflows/release.yml`. It builds, tests and publishes Windows and Chromebook ZIPs as a GitHub release. Pushing `main` also builds both downloads and deploys the browser site through `.github/workflows/pages.yml` once Pages is enabled.

## Project layout

```
src/ToniBreidingerRacing.Core/   Game logic (cross-platform)
  Racing/        Tracks, car physics, AI rivals, race rules
  Game/          Season progression, input, game state machine and screens
  Rendering/     NES palette, frame buffer, pixel font, sprites, track renderer
  Audio/         Chiptune sound effect synthesizer
  Rewards/       Arcade1870 claim client, encoder, claim page (shared treasury)
  Configuration/ appsettings + player profile
src/ToniBreidingerRacing/        Windows (WinForms) shell
src/ToniBreidingerRacing.Server/ Authoritative WebSocket multiplayer server
web/                           GitHub Pages site, browser game and offline Chromebook edition
tests/ToniBreidingerRacing.Tests xUnit tests
```
