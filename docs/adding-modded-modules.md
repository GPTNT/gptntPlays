# Adding a Modded Module

This guide explains how to add support for an arbitrary modded (non-vanilla) KTANE module so that it can be:

1. Spawned via `/startmission`.
2. Observed via `/state` with structured, module-specific attributes.
3. Interacted with via `/action` (click, zoom, release).

---

## Table of contents

- [How module support works](#how-module-support-works)
- [Installing a mod](#installing-a-mod)
- [Step 1 — Find the module ID](#step-1--find-the-module-id)
- [Step 2 — Spawn the module in a mission](#step-2--spawn-the-module-in-a-mission)
- [Step 3 — Add a state class](#step-3--add-a-state-class)
- [Step 4 — Register the state class](#step-4--register-the-state-class)
- [Step 5 — Test](#step-5--test)
- [Reference: special module behaviours](#reference-special-module-behaviours)

---

## How module support works

Each supported module has two integration points in the codebase:

| File | Purpose |
|---|---|
| [`StateClasses.cs`](../TwitchPlaysAssembly/Src/StateClasses.cs) | A subclass of `SolvableModuleState` that reads internal module state and exposes it as JSON-serialisable properties. |
| [`GptntStates.cs`](../TwitchPlaysAssembly/Src/GptntStates.cs) | `CreateModuleState(BombComponent)` — a factory switch that maps a `BombComponent` to its state class so the module is included in `bombState.modules`. |

When the bomb lights on, `GptntStates.ModuleStates` iterates every `BombComponent` and calls `CreateModuleState`. Any component not matched by the switch is dropped; it will never appear in `/state` and clicking on it will silently do nothing after zoom.

For vanilla modules `ComponentTypeEnum` is the discriminator. For modded modules the type is always `ComponentTypeEnum.Mod` — so a single `case ComponentTypeEnum.Mod` fallback (already present in the codebase) catches all mods generically and exposes `isSolved` / `inFocus`. If you want module-specific fields in `/state`, follow the steps below to promote the module from the generic fallback to a dedicated state class.

---

## Installing a mod

KTANE mods are distributed as Steam Workshop items. Each workshop item has a numeric **Workshop ID** visible in the URL of its Steam Workshop page:

```
https://steamcommunity.com/sharedfiles/filedetails/?id=<WORKSHOP_ID>
```

You do **not** need the Steam version of KTANE to download Workshop items. We use SteamCMD to download the mod files. 

### Install SteamCMD

| Platform | Method |
|---|---|
| Ubuntu / Debian | `apt-get install steamcmd` |
| macOS | `brew install steamcmd` |
| Windows | Download the [SteamCMD zip](https://developer.valvesoftware.com/wiki/SteamCMD) and extract it. |


### Download a mod

KTANE's Steam App ID is **341800**. Run the following, substituting `<WORKSHOP_ID>` for the numeric ID from the workshop page:

```sh
# Enter the SteamCMD prompt
steamcmd

# Inside the SteamCMD prompt:
login <STEAM_USER>
workshop_download_item 341800 <WORKSHOP_ID>
quit

```

SteamCMD downloads the mod to:

| Platform | Path |
|---|---|
| Linux | `~/.local/share/Steam/steamapps/workshop/content/341800/<WORKSHOP_ID>/` |
| macOS | `~/Library/Application Support/Steam/steamapps/workshop/content/341800/<WORKSHOP_ID>/` |
| Windows | `C:\Program Files (x86)\Steam\steamapps\workshop\content\341800\<WORKSHOP_ID>\` |

The workshop content directory for a KTANE mod is already in the correct format for the game to load — it contains a `modInfo.json` and the compiled Unity asset bundle.

### Enable the mod in KTANE

Copy the downloaded workshop directory into the game's `mods/` directory:

Restart KTANE. After accepting mods, the module will then appear in the output of `GET /modules`.

---

## Step 1 — Find the module ID

Every modded module has a `ModuleType` string (also called the *module ID*) defined inside the mod itself via `KMBombModule.ModuleType`. This is the string you pass to `/startmission` and the key you use in `CreateModuleState`.

Retrieve it from the running game:

```sh
curl http://localhost:8085/modules
```

Response (excerpt):

```json
[
  { "id": "BigButton",       "displayName": "The Button",       "isMod": false },
  { "id": "PianoKeys",       "displayName": "Piano Keys",       "isMod": true  }
]
```

The `id` field for entries where `isMod` is `true` is the string you need. Note it down — it is used in every step that follows.

---

## Step 2 — Spawn the module in a mission

Pass the module ID directly in the `components` list of `/startmission`. Vanilla and modded IDs can be mixed freely:

```sh
curl "http://localhost:8085/startmission?\
seed=1&timeLimit=300&numStrikes=3&needyTime=90&\
isFront=true&optWidgets=3&timeScale=1.0&timeStepSize=250&\
components=Wires,PianoKeys"
```

Vanilla component names (e.g. `Wires`, `BigButton`) are parsed as `KMComponentPool.ComponentTypeEnum`. Anything that does not match the enum is treated as a mod module ID and placed into `KMComponentPool.ModTypes` automatically — no code change is required.

Verify the module appears on the bomb by calling `/state` once the game reaches `LightsOn`:

```sh
curl http://localhost:8085/state
```

You should see an entry in `modules` with the module's name. At this point the generic `ModdedModuleState` is in use, so only `isSolved`, `inFocus`, `onFront`, and `index` are populated. The following steps add module-specific attributes.

---

## Step 3 — Add a state class

Open [`TwitchPlaysAssembly/Src/StateClasses.cs`](../TwitchPlaysAssembly/Src/StateClasses.cs) and add a new class that extends `SolvableModuleState`.

### Minimal template

```csharp
public class MyModuleState : SolvableModuleState
{
    // Add one property per piece of observable state you need.
    public string someAttribute { get; set; }

    public MyModuleState(BombComponent comp) : base(comp)
    {
        component = comp;
        SetAttributes();
    }

    public override void UpdateAttributes()
    {
        base.UpdateAttributes();
        SetAttributes();
    }

    private void SetAttributes()
    {
        // Cast to the concrete Unity component type from the mod.
        // The type name comes from the mod's assembly — use reflection
        // if the type is not directly accessible (see note below).
        var myComp = component.GetComponent<MyModComponent>();
        someAttribute = myComp.SomePublicField.ToString();
        name = "MyModule"; // Should match the module ID from /modules.
    }
}
```

### Accessing private fields via reflection

Mod component types often live in a separate assembly and may keep their state in non-public fields. Use `System.Reflection` the same way the vanilla state classes do:

```csharp
FieldInfo field = typeof(MyModComponent).GetField(
    "privateField",
    BindingFlags.NonPublic | BindingFlags.Instance
);
someAttribute = field.GetValue(myComp).ToString();
```

### Modules with emerge animations

If the module has UI elements that animate into position when zoomed in (like vanilla Memory or Who's On First), implement `IEmergingModule` so that `/timestep` waits for them before returning:

```csharp
public class MyModuleState : SolvableModuleState, IEmergingModule
{
    public bool isEmerged { get; set; }

    private void SetAttributes()
    {
        var myComp = component.GetComponent<MyModComponent>();
        isEmerged = myComp.IsReady; // whatever the module exposes
        if (!isEmerged) return;     // skip expensive reads until emerged

        // ... read attributes normally
    }
}
```

---

## Step 4 — Register the state class

Open [`TwitchPlaysAssembly/Src/GptntStates.cs`](../TwitchPlaysAssembly/Src/GptntStates.cs) and find `CreateModuleState`. Add a check for the module's `KMBombModule.ModuleType` string before the generic `ComponentTypeEnum.Mod` fallback:

```csharp
case ComponentTypeEnum.Mod:
    KMBombModule kmModule = comp.GetComponent<KMBombModule>();
    if (kmModule != null && kmModule.ModuleType == "PianoKeys")
        return new MyModuleState(comp);
    return new ModdedModuleState(comp); // generic fallback for all other mods
```

If you are adding several modded modules, a nested switch or dictionary lookup keeps this tidy:

```csharp
case ComponentTypeEnum.Mod:
    KMBombModule kmModule = comp.GetComponent<KMBombModule>();
    switch (kmModule?.ModuleType)
    {
        case "PianoKeys":       return new PianoKeysModuleState(comp);
        case "TurnTheKey":      return new TurnTheKeyModuleState(comp);
        default:                return new ModdedModuleState(comp);
    }
```

The `ModdedModuleState` fallback at the end ensures every other mod still appears in `/state` with basic solve and focus tracking, rather than being silently dropped.

---

## Step 5 — Test

1. **Build and deploy** the mod (see [Building the mod](../README.md#building-the-mod)).
2. Start a mission containing the module:
   ```sh
   curl "http://localhost:8085/startmission?seed=1&timeLimit=300&numStrikes=3\
   &needyTime=90&isFront=true&optWidgets=3&timeScale=1.0&timeStepSize=250\
   &components=ColoredSwitches"
   ```
3. Once `LightsOn`, poll `/state` and confirm the new module-specific fields are present in the `modules` array.
4. Click into the module (`/action?action=click&x_pos=0.5&y_pos=0.5`), then check that `inFocus` flips to `true` in the next `/state` response.
5. Zoom out (`/action?action=out`) and confirm `inFocus` returns to `false`.