# C3Studio

A WPF + MonoGame asset browser and viewer for **Conquer Online** `.c3` files.

## Requirements
- .NET 8.0 SDK (Windows)
- Visual Studio 2022 or `dotnet build`
- A local Conquer Online installation

## Build
```
dotnet build C3Studio.csproj
dotnet run --project C3Studio.csproj
```

## First Run
1. The **Setup** screen appears — click **Browse…** and select your Conquer Online folder
   (the one containing `c3.wdf`, `data.wdf`, and the `ini/` folder).
2. The validation checklist confirms all required folders, archives, and INI files.
3. Click **Load Game Data →** — the app parses all INI files and opens the **Workspace**.

## Workspace
| Area | Description |
|---|---|
| Left panel | Asset tree: **NPCs** and **Simple Objects** from the INI files |
| Centre | MonoGame viewport — orbit with left-drag, zoom with scroll / W S |
| Right panel | Playback controls, FPS slider, file path |
| Toolbar | Open a `.c3` directly, Play/Pause, step frame, camera reset |

Click any NPC or part node in the tree to load its mesh into the viewport.

## Project Structure
```
C3Studio/
├── Core/
│   ├── Models/       AssetNode, NpcTypeInfo, C3DSimpleObjInfo, ValidationItem
│   └── Services/     Settings, AssetFile, GameData, Navigation
├── Infrastructure/
│   ├── C3Format/     C3Model, C3Phy, C3Motion, C3Texture, loaders …
│   ├── FileSystem/   TqPackageReader, WdfPackageReader
│   ├── Ini/          NpcIniParser, ResIniParser, SimpleObjIniParser
│   └── Rendering/    C3Renderer
├── MonoGame/         C3StudioGame (WpfInterop host + orbit camera)
├── ViewModels/       SetupViewModel, WorkspaceViewModel
├── Views/            SetupView.xaml, WorkspacePage.xaml
├── Converters/       6 value converters
└── Resources/        Styles.xaml (dark Conquer theme)
```
