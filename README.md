# gptntPlays: An HTTP Control Interface for *Keep Talking and Nobody Explodes*

`gptntPlays` is a Unity mod that exposes [*Keep Talking and Nobody Explodes*](https://keeptalkinggame.com/) (KTANE) as a programmatic environment for AI agents and research. It adds an HTTP server to the game so an external process (a model, a controller, a benchmark harness) can start missions, observe the bomb, send mouse/rotation actions, and step the simulation deterministically.

The mod is built on top of the [TwitchPlays mod](https://github.com/samfun123/KtaneTwitchPlays) by samfun123 and others; the original Twitch-chat command interface has been removed and replaced with the HTTP control surface described below.

This repository accompanies a research paper. If you are looking for the code, builds, or want to reproduce experiments, this README is the primary entry point. The bulk of new code lives under [TwitchPlaysAssembly/](TwitchPlaysAssembly).

---

## Table of contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Installation](#installation)
- [Running headless in Docker](#running-headless-in-docker)
- [Configuration](#configuration)
- [HTTP API](#http-api)
  - [Lifecycle](#lifecycle)
  - [Observation](#observation)
  - [Actions](#actions)
  - [Time control](#time-control)
  - [Utilities and debugging](#utilities-and-debugging)
- [Game state schema](#game-state-schema)
- [Supported modules](#supported-modules)
- [Coordinate system](#coordinate-system)
- [Game-state machine](#game-state-machine)
- [Tracing and logging](#tracing-and-logging)
- [Building the mod](#building-the-mod)
- [Repository layout](#repository-layout)
- [Citation](#citation)
- [Acknowledgements and license](#acknowledgements-and-license)

---

## Overview

KTANE is a real-time, partially observable, mouse-driven puzzle game in which a player must defuse a procedurally generated bomb made up of independent puzzle modules. The game is well suited as a benchmark for embodied / GUI-driven agents because:

- Each mission is **seeded and reproducible**.
- Each module is a small, **self-contained reasoning task** (sequences, mazes, ciphers, perception under noise) that can be evaluated independently.
- The action space is low-level: 2D screen-space clicks, holds, releases, and bomb rotations — the same affordances available to a human player.
- Difficulty scales naturally by adding modules, strikes, time pressure, and needy modules.

`gptntPlays` makes this environment scriptable. It boots a standard KTANE install, exposes a local HTTP server on port `8085`, and lets a controller:

1. Start a mission with a chosen seed and module list.
2. Read a structured JSON description of the current bomb (the **state**), including per-module ground-truth attributes.
3. Read pixel **observations** (RGB screenshots plus segmentation masks of interactable elements).
4. Issue **actions** (click, hold, release, rotate the bomb, zoom out).
5. **Pause, resume, and time-step** the simulation so agents that are slower than real time still have a fair evaluation.

The HTTP server runs on a worker thread; all requests that touch Unity objects are marshalled back to the main thread, so handlers are safe to call concurrently from an external runner.

## Architecture

The new mod code lives in [TwitchPlaysAssembly/Src/](TwitchPlaysAssembly/Src) and is organised around a small set of `MonoBehaviour` components attached to a single mod host object:

| Component | Responsibility |
|---|---|
| [`GptntHttpHandler`](TwitchPlaysAssembly/Src/GptntHttpHandler.cs) | Runs the `HttpListener` worker thread, parses W3C `traceparent` headers, and dispatches to route handlers. |
| [`RequestHandlers`](TwitchPlaysAssembly/RequestHandlers.cs) | Per-endpoint handler methods. Validates request state, marshals work to the main thread, and serialises responses. |
| [`GptntStates`](TwitchPlaysAssembly/Src/GptntStates.cs) | Tracks the high-level game state machine (`Setup`, `LightsOn`, `Transitioning`, …), maintains the current `BombState`, and fires `OnFirstLightsOn` / `OnReset` / `OnGameEnd` events. |
| [`GptntActions`](TwitchPlaysAssembly/Src/GptntActions.cs) | Implements the action primitives: ray-cast clicks in screen space, hold/release, 90° and 180° bomb rotations, zoom-out. Tracks which face of the bomb is currently active. |
| [`GptntBuffer`](TwitchPlaysAssembly/Src/GptntBuffer.cs) | Maintains a fixed-size ring buffer of recent rendered frames (PNG-encoded) so the controller can recover short observation histories. |
| [`Segmentation`](TwitchPlaysAssembly/Src/Segmentation.cs) | Renders a separate segmentation pass that colours each interactable selectable on a dedicated layer, used to produce instance masks aligned with the observation frame. |
| [`MagicSolver`](TwitchPlaysAssembly/Src/MagicSolver.cs) | Oracle controller that solves the next module in a randomised sequence. Used as an upper-bound baseline and for end-to-end smoke tests of the action pipeline. |
| [`GptntGameHost`](TwitchPlaysAssembly/Src/GptntGameHost.cs) | Bootstraps screen resolution, the frame buffer, segmentation, and the initial "pick up the bomb" coroutine on first lights-on. |
| [`StateClasses`](TwitchPlaysAssembly/Src/StateClasses.cs) | Plain-old-data state classes for every supported module and widget, with `UpdateAttributes` hooks that read the underlying KTANE component (sometimes via reflection on private fields) and emit JSON-friendly views. |
| [`OpenTelemetrySpan`](TwitchPlaysAssembly/Src/OpenTelemetrySpan.cs) | Lightweight OTLP/HTTP exporter. Every request creates a server span; long-running handlers create child spans. See [Tracing and logging](#tracing-and-logging). |

The HTTP server only binds to `localhost`. The mod is designed to run alongside a controller on the same machine (typically inside a single container) — not to be exposed on a network.

## Installation

You need a copy of KTANE. The game loads mods from a `mods/` directory that sits next to the game executable.

We have only tested against the **Humble Store** build of KTANE; the Steam build *probably* works but has not been validated by us. Any storefront that ships the standard Unity build of the game should be fine.

1. Install KTANE, then create an empty `mods/` directory next to the game executable. The install directory should look like:

   ```
   <KTANE install>/
   ├── Keep Talking and Nobody Explodes.app   # or the .exe / Linux binary
   └── mods/
   ```

2. Download the latest prebuilt assembly from the [`build/` directory of the upstream repo](https://github.com/GPTNT/gptntPlays/tree/main/build).
3. Drop the built `GptntPlays` folder into the game's `mods/` directory, so the path looks like `<KTANE install>/mods/GptntPlays/`.
4. Launch KTANE. The HTTP server should bind to `http://localhost:8085/` on startup. Confirm with:

   ```sh
   curl http://localhost:8085/health
   # → Setup
   ```

If you prefer to build from source, see [Building the mod](#building-the-mod).

## Running headless in Docker

For batch experiments we recommend the Dockerfiles in [docker-ktane/](docker-ktane), which wrap the Linux build of KTANE in a virtual X display so it can run without a GPU monitor attached. See [docker-ktane/README.md](docker-ktane/README.md) for build / run commands; the short version is:

```sh
docker build . -t docker-ktane -f Dockerfile-ubuntu
docker run --rm -p 8085:8085 docker-ktane
```

You will need to drop a Linux KTANE build (and the built mod) into the directory before building; the Dockerfiles do not (and cannot) ship the game.

## Configuration

The mod reads the following environment variables at startup:

| Variable | Default | Meaning |
|---|---|---|
| `port` | `8085` | Port the HTTP server binds to (localhost only). |
| `GAME_WIDTH` | `512` | Render width, in pixels, for the screenshot/segmentation cameras. |
| `GAME_HEIGHT` | `384` | Render height, in pixels. |

Observation frames in `/buffer` are PNG-encoded at this resolution; the ring buffer holds the most recent 16 frames captured at one frame every 0.25 in-game seconds (see [`GptntGameHost`](TwitchPlaysAssembly/Src/GptntGameHost.cs)).

## HTTP API

All endpoints accept `GET` with query-string parameters and return either `text/plain` or `application/json`. CORS is enabled (`Access-Control-Allow-Origin: *`) and `traceparent` / `tracestate` headers are accepted for distributed tracing.

Responses use standard status codes:

- `200 OK` — handled successfully.
- `400 Bad Request` — request issued in a game state that does not permit it, or invalid parameters.
- `408 Request Timeout` — segmentation render did not complete within 500 ms.
- `500 Internal Server Error` — handler threw.

### Lifecycle

#### `GET /startmission`

Starts a new mission. Only valid from `Setup`.

| Param | Type | Description |
|---|---|---|
| `seed` | string | Mission seed (string; passed verbatim to KTANE). |
| `timeLimit` | int | Total bomb time in seconds. |
| `numStrikes` | int | Maximum strikes before detonation. |
| `needyTime` | int | Delay (seconds) before needy modules activate; must satisfy `0 ≤ needyTime ≤ timeLimit`. |
| `isFront` | bool | If `true`, all modules placed on the front face. |
| `optWidgets` | int | Number of optional widgets (batteries, indicators, ports, serial). |
| `components` | csv | Comma-separated list of `KMComponentPool.ComponentTypeEnum` values, 1–11 entries. |
| `timeScale` | float | Initial `Time.timeScale`. |
| `timeStepSize` | int | Step size for `/timestep`, in in-game milliseconds. |
| `sessionId` | string (optional) | Tag attached to all subsequent log lines for this episode, used to correlate Unity logs with an external run id. |

Example:

```
/startmission?seed=1&timeLimit=300&numStrikes=3&needyTime=90&isFront=true&optWidgets=5&components=Venn&timeScale=1.0&timeStepSize=250
```

Returns the seed on success.

#### `GET /reset`

Returns the game to the setup screen from any non-setup state. Used between episodes.

#### `GET /health`

Returns the current game state as a single token: one of `Gameplay`, `Setup`, `LightsOn`, `LightsOff`, `Transitioning`, `PostGame`. Excluded from request logs so it is safe to poll.

#### `GET /detonate`

Force-detonates the bomb. Only valid in `LightsOn`. Intended for terminating failed episodes.

#### `GET /solve`

Force-solves every remaining module. Only valid in `LightsOn`. Intended as an upper-bound oracle and for tooling tests.

### Observation

#### `GET /state`

Returns the full bomb state as JSON. Only available once the bomb has reached its first lights-on and not after reset. See [Game state schema](#game-state-schema).

#### `GET /buffer`

Returns the most recent buffered RGB frames plus a single fresh segmentation mask as a packed **binary** payload (`Content-Type: application/octet-stream`). The wire format avoids the cost of PNG-encoding + base64 + JSON parsing on the hot observation path.

Layout (little-endian, written via `System.IO.BinaryWriter`):

| Offset | Type | Field |
|---|---|---|
| 0 | `bool` (1 byte) | `segmentationIncluded` — `1` if a segmentation mask is appended after the frames, `0` otherwise. |
| 1 | `int32` | `frameCount` — number of buffered RGB frames that follow. |
| 5 | `int32` | `frameHeight` (pixels). |
| 9 | `int32` | `frameWidth` (pixels). |
| 13 | `frameCount × (frameWidth · frameHeight · 3)` bytes | Raw RGB24 pixels, frames concatenated in chronological order (oldest first). |
| … | `frameWidth · frameHeight · 3` bytes | Raw RGB24 pixels of the segmentation mask. Present iff `segmentationIncluded == 1`. |

Each frame is exactly `frameWidth * frameHeight * 3` bytes; GPU row padding from `Texture2D.GetRawTextureData()` is stripped before serialisation. The ring buffer holds up to 16 frames captured at one frame every 0.25 in-game seconds.
If the segmentation render does not complete within 500 ms the response returns `408 Request Timeout` and no body is written.

The segmentation mask colours all currently active interactable `Selectable`s on a dedicated render layer; pixel colour identifies which selectable a pixel belongs to.

#### `GET /old-buffer`

Legacy JSON form of the same payload, kept for backwards compatibility with older controllers and notebooks:

```json
{
  "screenshot": ["<base64 PNG>", "<base64 PNG>", ...],
  "segmentation": "<base64 PNG>"
}
```

Prefer `/buffer` for new code — `/old-buffer` is materially slower because it PNG-encodes and base64-wraps every frame.

### Actions

#### `GET /action`

The single action endpoint. Behaviour is controlled by the `action` query parameter. Only valid during `LightsOn` / `LightsOff` once the mission has actually started; otherwise `400`.

| `action` | Extra params | Effect |
|---|---|---|
| `click` | `x_pos`, `y_pos` | Press and release at the given screen-space position. |
| `hold`  | `x_pos`, `y_pos` | Press at the given position; the selectable remains held until `release`. (For modules that can't be held, the effect is the same as `click` ) |
| `release` | — | Release the currently held selectable. |
| `out` | — | Zoom out of the currently focused module. |
| `left` / `right` / `up` / `down` | — | Rotate the bomb 90° around the corresponding axis. |
| `flip` | — | Rotate the bomb 180° (front ↔ back). |
| `magic` | — | Solve one step of the next module via the oracle solver (see [`MagicSolver`](TwitchPlaysAssembly/Src/MagicSolver.cs)). |
| `lottery` | — | Randomly do any one action (see [`LotterySolver`](TwitchPlaysAssembly/Src/LotterySolver.cs)). |

`x_pos` and `y_pos` are floats in `[0, 1]`. See [Coordinate system](#coordinate-system).

### Time control

These endpoints let an external controller decouple game time from wall-clock time so that slow agents can be evaluated under the same effective time pressure as fast ones.

#### `GET /settimescale?value=<float>`

Sets `Time.timeScale`. `0` pauses the game. If a pause is requested during a `Transitioning` state, the mod waits for the transition to end before applying the pause (transitions never play correctly under `timeScale=0`).

#### `GET /setstepunit?value=<int>`

Sets the duration of one step, in in-game milliseconds.

#### `GET /timestep`

Synchronously advances the game by exactly one `setstepunit` and pauses it again before returning. The handler:

1. Sets `Time.timeScale = 1` and waits `setstepunit` ms.
2. Waits until the game is no longer in a `Transitioning` state.
3. Waits until no module is mid-emerge (`bombState.isEmerging == false`), so that the next observation reads a quiescent bomb.
4. Sets `Time.timeScale = 0` and returns.

Each of the three waits has a 10 s wall-clock timeout; if any times out the response is `408 Request Timeout` with a message indicating which phase stalled. The typical control loop is: `/settimescale?value=0` once at episode start, then alternate `/state` (and/or `/buffer`) ↔ `/action` ↔ `/timestep`.

### Utilities and debugging

| Route | Purpose |
|---|---|
| `GET /random?value=<n>` | Force-solves `n` random unsolved modules. Useful for staged evaluations and for testing late-bomb behaviour without playing the whole bomb. |
| `GET /debug` | Logs and returns the active selectable face. Diagnostic only. |

## Game state schema

`GET /state` serialises a [`BombState`](TwitchPlaysAssembly/Src/StateClasses.cs):

```jsonc
{
  "seed": 1,
  "maxStrikes": 3,
  "isDetonated": false,
  "isSolved": false,
  "isLightOn": true,
  "bombSide": "front",      // "front" or "back"
  "isEmerging": false,      // true while any module's interactables are mid-emerge animation
  "timerModule": { "secondsRemaining": 287.4, "onFront": true, "index": 0, "name": "Timer" },
  "widgets":  [ /* BatteryWidgetState | IndicatorWidgetState | PortWidgetState | SerialNumberWidgetState */ ],
  "modules":  [ /* one entry per solvable module, see below */ ],
  "strikes":  [ "ButtonModuleState: wrong release", ... ]
}
```

Every module entry inherits from [`SolvableModuleState`](TwitchPlaysAssembly/Src/StateClasses.cs) and includes:

- `name` — module identifier (e.g. `BigButton`, `Keypad`, `Wires`).
- `onFront`, `index` — anchor position on the bomb. `index` is the closest anchor slot on the chosen face.
- `isSolved`, `inFocus` — solve state and whether this module is currently zoomed in on.
- Module-specific fields (the wire array for `Wires`/`Venn`/`WireSequence`, the maze coordinates for `Maze`, etc.).

Widgets carry their bomb-face position (`front` / `back` / `top` / `bottom` / `left` / `right`) and per-widget attributes such as battery type and count, indicator label and lit state, port set, or serial-number string.

The schema is intentionally exposed as ground truth: it bypasses what a human or vision-only agent could read from the screen. For vision-only experiments, use `/buffer`.

## Supported modules

[`StateClasses.cs`](TwitchPlaysAssembly/Src/StateClasses.cs) defines structured state for the following stock vanilla modules:

- **The Button** (`BigButton`)
- **Keypad**
- **Simon Says** (`Simon`)
- **Wires** (`WireSet`)
- **Complicated Wires** (`Venn`)
- **Wire Sequence**
- **Maze** (Invisible Walls)
- **Memory**
- **Morse Code**
- **Password**
- **Who's On First**

The timer and the four widget types (battery, indicator, port, serial number) are also fully serialised. Modded modules will still appear physically on the bomb but need to be manually added to the state schema.

## Coordinate system

`x_pos` and `y_pos` are normalised screen coordinates in `[0, 1]`:

- `x = 0` is the left edge of the rendered frame, `x = 1` the right.
- `y = 0` is the **top** of the frame, `y = 1` the bottom. (Internally the mod converts to Unity's bottom-origin convention, so this matches how images returned by `/buffer` are oriented.)

A click ray is cast from the main camera through that point against the interactable layer. If the ray misses, the click is a no-op; otherwise it triggers the corresponding `Selectable`. See [`GptntActions.Click`](TwitchPlaysAssembly/Src/GptntActions.cs).

## Game-state machine

Reported by `/health` and used to gate other endpoints:

| State | Meaning | What is allowed |
|---|---|---|
| `Setup` | Main / mission-select screen. | `/startmission`, `/reset`. |
| `Transitioning` | Loading, lights coming up, scene change. | Polls only (`/health`). Action / state requests will fail or block. |
| `LightsOff` | Bomb on table, lights about to come up. | Most actions queue but `/state` is gated until first lights-on. |
| `LightsOn` | Bomb active, timer running. | All actions, `/state`, `/buffer`, `/solve`, `/detonate`. |
| `PostGame` | Win/lose screen. | `/reset`. |
| `Gameplay` | Internal KTANE gameplay state during transitions. | Polls only. |

`OnFirstLightsOn` (the moment the bomb is first picked up and the lights come up) enables `/state`. `OnReset` disables it again.

## Tracing and logging

Every HTTP request creates an OpenTelemetry span via [`OpenTelemetrySpan`](TwitchPlaysAssembly/Src/OpenTelemetrySpan.cs), exporting OTLP/JSON to `http://localhost:4318/v1/traces` (the default OTLP HTTP collector endpoint). Long-running handlers create child spans (`game.action.mainthread`, `game.action.clickstart`, `observation.buffer`, `buffer.start`, …) so action latency and main-thread queueing can be measured.

The mod honours W3C `traceparent` headers on incoming requests, so a controller that is already inside a trace can attach the game-side spans to the same trace.

Logs are written through `log4net` and tagged with `trace_id` / `span_id` properties during a request, via [`GptntDebug.FormatMessage`](TwitchPlaysAssembly/Src/GptntDebug.cs).

If you do not run a collector, requests still succeed; the OTLP export call simply fails silently per span.

## Building the mod

The mod is a Unity-side C# assembly that targets the same KTANE managed runtime as TwitchPlays — `net35`, Unity's Mono profile for this version of the game. The general flow follows the upstream [TwitchPlays build instructions](https://github.com/samfun123/KtaneTwitchPlays/wiki/How-to-build):

1. Build [`TwitchPlaysAssembly/TwitchPlaysAssembly.csproj`](TwitchPlaysAssembly/TwitchPlaysAssembly.csproj) from your IDE.
2. In Unity, wait for the editor to pick up the new scripts, then build the mod.
3. Drop the built mod into the game's `mods/` directory and restart KTANE.

### Game install

You do **not** need the Steam version of KTANE — the Humble Store build works fine and, in our experience, was less awkward to point the build at than the Steam install (your mileage with Steam may vary).

### Windows build

The committed [`TwitchPlaysAssembly.csproj`](TwitchPlaysAssembly/TwitchPlaysAssembly.csproj) targets a Windows install by default; override `GameFolder` (in the csproj) if your install lives elsewhere.

### Mac build

Two macOS-specific gotchas:

**1. IDE.** Visual Studio for Mac has been discontinued by Microsoft, so the build requires an older installer. We are currently building with **Visual Studio for Mac 17.6.14 (build 413)**; newer "Visual Studio Code + C# Dev Kit" setups have not been validated.

**2. Paths.** The Mac KTANE bundle has a different managed-DLL layout than the Windows install. On macOS the managed directory is:

```
<wherever KTANE lives>/Keep Talking and Nobody Explodes.app/Contents/Resources/Data/Managed
```

— there is no `ktane_Data\Managed\` segment. So Mac users need a one-time patch to the csproj before building:

- Set `GameFolder` to the full `.../Keep Talking and Nobody Explodes.app/Contents/Resources/Data/Managed` directory.
- Strip the `\ktane_Data\Managed\` segment from every `<HintPath>` that references it, so each path becomes `$(GameFolder)/<Dll>.dll`. The affected references are `Assembly-CSharp`, every `UnityEngine.*Module`, `UnityEngine.UI`, and `log4net`.
- Update the post-build `Exec` command's destination from `$(GameFolder)mods\Twitch Plays\…` to `$(GameFolder)/../../../../mods/Twitch Plays/…` (or copy the DLL manually after each build) — the `mods/` directory lives next to the `.app`, not inside `Managed/`.

A small `sed` does most of the work on a fresh checkout:

```sh
sed -i '' 's|\\ktane_Data\\Managed\\|/|g' TwitchPlaysAssembly/TwitchPlaysAssembly.csproj
```

We chose not to commit the Mac-friendly variant as the default because the upstream TwitchPlays csproj — and the majority of likely contributors — target Windows. Keep these edits in your working tree (or behind an MSBuild `Condition`); do not push them as the project default.

### Typical iteration loop

1. Edit C# sources under [`TwitchPlaysAssembly/`](TwitchPlaysAssembly).
2. Build the project in Visual Studio — the DLL is copied into the game's `mods/Twitch Plays/` folder by the post-build step.
3. Open the Unity project once and wait for the editor to recompile / refresh.
4. Build the Unity mod, move the output into the game's `mods/` directory.
5. Restart KTANE to pick up the new mod.

## Repository layout

```
.
├── Assets/                  # Unity project assets (untouched fork content)
├── Packages/                # Unity package manifest
├── ProjectSettings/         # Unity project settings
├── TwitchPlaysAssembly/     # The mod assembly (all gptntPlays code lives here)
│   ├── RequestHandlers.cs   # HTTP route handlers
│   ├── Src/
│   │   ├── Gptnt*.cs        # HTTP server, state, actions, buffer, host, debug
│   │   ├── StateClasses.cs  # JSON-serialisable bomb/module/widget state
│   │   ├── Segmentation.cs  # Per-selectable instance segmentation pass
│   │   ├── MagicSolver.cs   # Oracle baseline controller
│   │   ├── OpenTelemetrySpan.cs
│   │   └── ...              # Inherited TwitchPlays sources kept for compatibility
│   └── TwitchPlaysAssembly.{csproj,sln}
├── docker-ktane/            # Headless-Linux Docker images for batch experiments
├── docs/                    # Generated docs (Doxygen output)
└── README.md                # This file
```

The `TwitchPlaysAssembly` directory contains both the new code added for this project and the original TwitchPlays sources it inherits from. The new control surface is contained in the `Gptnt*` files, `RequestHandlers.cs`, `StateClasses.cs`, `Segmentation.cs`, `MagicSolver.cs`, and `OpenTelemetrySpan.cs`.

## Citation

If you use `gptntPlays` in academic work, please cite the accompanying paper. A BibTeX entry will be added here once the paper has a public preprint / DOI.

## Acknowledgements and license

This project is a fork of and extension to [KtaneTwitchPlays](https://github.com/samfun123/KtaneTwitchPlays) by samfun123 and contributors. We are grateful for their work, which made the input plumbing, module abstractions, and mission scaffolding used here possible. The original repository's license terms (see [LICENSE](LICENSE)) carry over to this fork.

*Keep Talking and Nobody Explodes* is © Steel Crate Games. This mod is an unofficial third-party modification and is not affiliated with or endorsed by Steel Crate Games.
