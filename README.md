# Toni Breidinger Racing

A 2D Windows PC racing game in the style of the 1980s NES classic **R.C. Pro-Am**. You play as NASCAR driver **Toni Breidinger**, the best racer on the circuit. Race top-down around eight courses against three rivals, fire missiles, hit zippers, collect upgrades and spell **T-O-N-I**. Every race you finish earns **Arcade1870 (A1870)** tokens from the same reward treasury as [Crypto Hockey](https://github.com/jcampbell1870/Crypto-Hockey).

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

Pushing a `v*` tag runs `.github/workflows/release.yml`. It builds, tests and publishes `ToniBreidingerRacing-win-x64.zip` as a GitHub release.

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
tests/ToniBreidingerRacing.Tests xUnit tests
```
