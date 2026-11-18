# Cyber Fish Tank

Cyber Fish Tank is a Windows-first 3D aquarium game prototype about designing a tank, watching fish behave, and gradually managing a small freshwater ecosystem. “Cyber” refers to a virtual aquarium; it does not prescribe cyberpunk visuals. The intended mood is calm and natural, with the tank and its inhabitants as the focus.

## Current demo

The playable vertical slice is a single aquarium with guppies, neon tetras, and panda corydoras. It currently includes:

- Schooling and individual swimming, turning, short bursts, hovering, and collision avoidance.
- Food scattering, chasing, eating, and a short leave-and-search-again feeding loop.
- A planted aquascape with sand, rocks, a moss carpet, pebbles, shells, and a moving hermit crab.
- A simple day/night cycle, water temperature and pH display, demo water-quality indicators, and a 30% water-change action.
- Single-fish inspection, close-up camera controls, and a small individual status card.

This is a visual and interaction prototype, not a complete aquarium simulator. Water chemistry, health, equipment effects, breeding, growth, and long-term ecology are not fully connected yet. The README and design documents distinguish implemented demo behavior from planned systems.

## Run the demo

1. Install Unity **6000.3.28f1** (Unity 6.3.28f1) with Unity Hub.
2. In Unity Hub, choose **Add project from disk** and select `game/fish-tank`.
3. Open `Assets/Scenes/FishTankVerticalSlice.unity`.
4. Press **Play** in the Unity Editor.

The first project open may take a while while Unity imports assets and resolves packages. The vertical-slice scene creates the aquarium at runtime. `Assets/Scenes/SampleScene.unity` is the default Unity template scene and is not the demo entry point.

### Controls

- **Left-click a fish:** inspect it in close-up. Other fish are hidden temporarily.
- **Left-click empty water:** scatter fish food.
- **Right-drag:** orbit the camera.
- **Scroll:** zoom; while inspecting a fish, adjust close-up distance.
- **Middle-drag:** pan the camera.
- **Esc:** exit fish inspection.
- **Home:** reset the aquarium camera.
- **Water Change 30% button:** apply the demo water-change action.

You can also start the Editor with `game/fish-tank/Start-UnityEditor.ps1`. It defaults to a machine-specific Unity executable path; pass `-UnityExe` if Unity is installed elsewhere. `game/fish-tank/启动Unity.bat` runs that script with its default path.

## Project structure

| Path | Purpose |
|---|---|
| `game/fish-tank/` | Unity project, scenes, C# gameplay code, and imported assets |
| `game/fish-tank/Assets/Scripts/Demo/` | Fish movement, feeding, tank presentation, and hermit-crab behavior |
| `game/fish-tank/Assets/Resources/` | Fish, plant, and aquascape models used by the demo |
| `game/fish-tank/Assets/Models/` | Editable Blender source files and archived model iterations |
| `docs/` | Design, implementation, behavior, progress, and technical-route documentation |
| `chat/` | Daily reports, weekly reports, and the current project memory |
| `fish-picture/` | User-provided fish reference images |

## Documentation

- [Development handbook](docs/development-handbook.md): current demo scope, implementation notes, and confirmed behavior.
- [Fish behavior design](docs/fish-behavior-design.md): movement, schooling, feeding, and planned behavior layers.
- [Game design framework](docs/game-design-framework.md): product direction and accumulated design decisions.
- [Technical roadmap](docs/technical-roadmap.md): Unity/Blender stack and technical options.
- [Project progress](docs/project-progress.md): current milestone and outstanding work.
- [Project memory](chat/记忆.md): concise, current project state.
- [Daily reports](chat/): dated implementation and research notes.

## Technology

- Unity 6.3.28f1, Universal Render Pipeline, C#, and the Unity Input System.
- Blender 5.3 for editable 3D source models; FBX and texture assets are imported into Unity.
- Git tracks the project and its source assets. Unity-generated folders such as `Library/` and `Temp/` are ignored.

## Current focus

The guppy is being rebuilt as the first close-up model sample using user-provided references. It has a fuller, shorter trunk, a broader curved fan tail, embedded fin rays, flank color patches, and a subtle scale normal map. Its appearance is still stylized; the neon tetra and panda cory have not yet been rebuilt to this standard.
