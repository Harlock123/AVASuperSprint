# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

An overhead racing game in the style of Atari's 1986 arcade game Super Sprint, in C# on .NET 10 and Avalonia 12.
One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is original:
the six tracks and the graphics are made in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASuperSprint.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASuperSprint.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVASuperSprint           # play it; --no-audio for silence
SUPERSPRINT_SHOTS=$PWD/Images dotnet test tests/AVASuperSprint.Tests --filter ScreenshotTests   # refresh the README's pictures
SUPERSPRINT_PROBE=1 dotnet test tests/AVASuperSprint.Tests --filter PilotProbe   # how the computer driver does, race by race ("lazy" too)
python3 tools/make_sounds.py                      # rebuild src/AVASuperSprint/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASuperSprint/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the phases of a race.
  - `GameEngine.Play.cs` steps a race: the four cars, walls and wrecks, the helicopter, oil, wrenches and
    whirlwinds, laps and placings.
  - `Entities.cs` has `Controls`, the screen measures (`Field`), the six tracks (`Track.All`) and the cars.
    `Geometry.cs` has the vector type (heading 0 is up the screen, turning clockwise).
  - `CarPilot.cs` is how a computer drives: the engine drives the three computer cars with it, and it is the
    player in the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASuperSprint/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, the whole
  track seen from above, and runs the 60 Hz timer.
- `src/AVASuperSprint/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASuperSprint.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PilotProbe` does nothing unless
  `SUPERSPRINT_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`, never from
  `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** `Controls` here is `Steer` (-1 left, 1 right), `Gas` and `Brake`: up
  keys are the gas and down keys the brake. Test new controls through `InputHandler`, not by setting `Controls`
  directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock (Shift brakes). `ShiftReleaseTests` covers this.
- **One driver for everyone.** A change to `CarPilot` changes the computer cars as well as the demonstration, so
  run the probe after one.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASuperSprint`); tests
  load the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
