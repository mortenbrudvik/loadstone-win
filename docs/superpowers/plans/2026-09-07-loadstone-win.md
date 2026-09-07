# Loadstone for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Loadstone for Windows 0.1.0: a tray utility that tiles the focused window into halves, corners, thirds, and two-thirds from global hotkeys or by dragging a window to a screen edge, with one-shot Restore, next/previous display, editable shortcuts, launch at login, and Windows' own edge snapping switched off while it runs.

**Architecture:** Two source projects. `Loadstone.Core` is pure C# with no Win32 and no WPF: geometry, commands, the director with its restore ledger, the drag state machine, settings, and the seams the UI implements. `Loadstone.UI` is the WPF tray app: a `LibraryImport` interop layer, `Win32Window` placement with the place-twice DPI sequence, `RegisterHotKey` hotkeys on a message-only window, drag-to-snap driven by the OS move-loop WinEvents plus a 60 Hz cursor timer, a topmost click-through preview, H.NotifyIcon tray, and a WPF-UI settings window. Data flows one way: input resolves a window and display, the director decides the frame, `Win32Window` writes it.

**Tech Stack:** .NET 10 SDK, C# 14, WPF with WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.2, Autofac 9.3.2 with `IHost`, H.NotifyIcon.Wpf 2.4.1, NLog 6.2.0, xUnit v3 4.0.0 with Xunit.StaFact 4.0.23, NSubstitute 6.2.0, Shouldly 4.3.0.

**Spec:** `docs/superpowers/specs/2026-09-07-loadstone-win-design.md`. The plan argues from the spec; read the spec section a task names before starting it. The macOS reference implementation is at `C:\code\projects\loadstone` and is the authority on behavior; the Swift file a task names is worth reading when a rule seems odd.

## Global Constraints

- Target framework `net10.0-windows10.0.26100.0`, `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion` latest, `TreatWarningsAsErrors` true, all from `Directory.Build.props`. A build with any warning fails.
- `Loadstone.Core` references only `Microsoft.Extensions.Logging.Abstractions`. It contains no `DllImport`, no `LibraryImport`, no WPF, no registry access. Every Win32 declaration lives in `src/Loadstone.UI/Interop/`.
- Coordinates are physical pixels, top-left origin, y down, everywhere. There is no coordinate flip anywhere.
- Package versions are pinned exactly: WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.2, Autofac 9.3.2, Autofac.Extensions.DependencyInjection 11.0.2, Microsoft.Extensions.Hosting 10.0.11, Microsoft.Extensions.Logging.Abstractions 10.0.11, H.NotifyIcon.Wpf 2.4.1, NLog 6.2.0, NLog.Extensions.Logging 6.2.0, xunit.v3 4.0.0, xunit.runner.visualstudio 4.0.0, Microsoft.NET.Test.Sdk 18.9.0, NSubstitute 6.2.0, Shouldly 4.3.0, coverlet.collector 10.0.1, Xunit.StaFact 4.0.23.
- The exe is `Loadstone.exe`, the product name is `Loadstone`, settings live at `%LOCALAPPDATA%\Loadstone\settings.json`, logs under `%LOCALAPPDATA%\Loadstone\Logs`, the help URL is `https://github.com/mortenbrudvik/loadstone-win#readme`.
- Command ids are exactly `leftHalf`, `rightHalf`, `topHalf`, `bottomHalf`, `topLeft`, `topRight`, `bottomLeft`, `bottomRight`, `leftThird`, `centerThird`, `rightThird`, `leftTwoThirds`, `rightTwoThirds`, `maximize`, `center`, `restore`, `nextDisplay`, `previousDisplay`, and never change.
- Tests: xUnit v3, NSubstitute, Shouldly. Names follow `MethodName_Scenario_ExpectedResult`. Fixtures use dimensions that do not divide by 2 or 3 (1201 by 901) and displays with negative origins, on purpose. Tests that create windows run under `[WpfFact]` (Xunit.StaFact). Tests never touch the user's real Run key, real settings file, or real snap flags except to write back the value they read.
- Every commit message ends with these two trailer lines, exactly:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
  ```
  The commit steps below use a bash heredoc (`git commit -F - <<'EOF'`). From PowerShell, put the same text in a single-quoted here-string and pipe it to `git commit -F -`.
- Commands run from the repository root `C:\code\projects\loadstone-win`. `dotnet build Loadstone.sln` must be warning-free after every task, and `dotnet test Loadstone.sln` must pass.
- The machine this is developed on has the 32-bit `pwsh` first on PATH; scripts that touch Win32 (the icon generator) must be run with `C:\Program Files\PowerShell\7\pwsh.exe`.

## File Structure

```
Loadstone.sln
Directory.Build.props                          shared build properties
.editorconfig                                  formatting and naming rules
.gitignore
.github/workflows/ci.yml                       build + test on windows-latest
docs/superpowers/specs/2026-09-07-loadstone-win-design.md
docs/layouts.svg, docs/snap-zones.svg          README diagrams (Task 22)
tools/icons/generate.ps1                       regenerates the .ico assets (Task 18)

src/Loadstone.Core/
  Loadstone.Core.csproj
  Geometry/Point.cs, Rect.cs                   integer value records (Task 2)
  Geometry/Tile.cs                             Tile enum, Title(), Frame() (Task 2)
  Geometry/Display.cs, Layout.cs, ScreenGeometry.cs   (Task 3)
  Geometry/SnapZones.cs                        (Task 4)
  Commands/KeyModifiers.cs, VirtualKeys.cs, Shortcut.cs   (Task 5)
  Commands/CommandSection.cs, WindowCommand.cs, WindowCommands.cs   (Task 6)
  Commands/CommandOutcome.cs                   (Task 7)
  Windows/WindowIdentity.cs, IMovableWindow.cs, WindowDirector.cs   (Task 7)
  Drag/SnapTarget.cs, DragSnapTracker.cs       (Task 8)
  Settings/AppSettings.cs, ISettingsService.cs, SettingsService.cs   (Task 9)
  Platform/INativeSnapSettings.cs, NativeSnap.cs, StartupStatus.cs, IStartupRegistration.cs, StartupApproval.cs   (Task 10)

src/Loadstone.UI/
  Loadstone.UI.csproj, app.manifest, AssemblyInfo.cs, App.xaml, App.xaml.cs
  Interop/NativeConstants.cs, NativeStructs.cs, NativeMethods.User32.cs, NativeMethods.Dwmapi.cs, NativeMethods.Shcore.cs   (Task 11)
  Windows/Displays.cs, WindowStyles.cs         (Task 11)
  Windows/Win32Window.cs, WindowLiveness.cs, FocusedWindow.cs   (Task 12)
  Windows/ICommandDispatcher.cs, CommandDispatcher.cs   (Task 13)
  Input/HotkeyStatus.cs, IHotkeyRegistrar.cs, HotkeyRegistrar.cs, IKeyboardLayoutProbe.cs, KeyboardLayoutProbe.cs   (Task 14)
  Input/HotkeyCenter.cs                        (Task 15)
  Input/IMoveSizeEvents.cs, WinEventMoveSizeEvents.cs, ISnapPreview.cs, SnapPreview.cs, DragSnapMonitor.cs   (Task 16)
  Services/SystemParametersNativeSnapSettings.cs, RegistryStartupRegistration.cs, SingleInstance.cs   (Task 17)
  Assets/loadstone.ico, tray-dark.ico, tray-light.ico   (Task 18)
  Tray/TrayMenuEntry.cs, TrayMenuBuilder.cs, IShellActions.cs, TrayController.cs   (Task 18)
  Controls/RecorderKeyHandler.cs, ShortcutRecorder.xaml(.cs)   (Task 19)
  ViewModels/ShortcutRowViewModel.cs, ShortcutGroupViewModel.cs, ShortcutsViewModel.cs, SettingsViewModel.cs   (Task 20)
  Views/SettingsWindow.xaml(.cs)               (Task 20)
  DependencyInjection/AppModule.cs, Services/LoggingSetup.cs, Services/ShellActions.cs   (Task 21)

tests/Loadstone.Core.Tests/                    one test class per Core type, mirrored folders
tests/Loadstone.UI.Tests/                      real-window tests under [WpfFact], pure tests as [Fact]
```

---

### Task 1: Repository scaffold and CI skeleton

Spec sections 3.1, 3.2, 11. Creates the solution, both source projects, both test projects, shared build properties, the CI workflow, and one smoke test per test project so the pipeline is proven before any product code exists.

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `.editorconfig`, `Loadstone.sln`
- Create: `src/Loadstone.Core/Loadstone.Core.csproj`
- Create: `src/Loadstone.UI/Loadstone.UI.csproj`, `src/Loadstone.UI/app.manifest`, `src/Loadstone.UI/AssemblyInfo.cs`, `src/Loadstone.UI/App.xaml`, `src/Loadstone.UI/App.xaml.cs`
- Create: `tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj`, `tests/Loadstone.Core.Tests/SmokeTests.cs`
- Create: `tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj`, `tests/Loadstone.UI.Tests/SmokeTests.cs`
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: the project references every later task assumes: `Loadstone.UI → Loadstone.Core`, `Loadstone.Core.Tests → Loadstone.Core`, `Loadstone.UI.Tests → Loadstone.UI`. `InternalsVisibleTo` from Core to `Loadstone.Core.Tests` and from UI to `Loadstone.UI.Tests`, so tests may use `internal` types.

- [ ] **Step 1: Write `.gitignore`, `Directory.Build.props`, `.editorconfig`**

`.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
*.suo
TestResults/
publish/
installer-output/
.claude/worktrees/
```

`Directory.Build.props`:

```xml
<Project>
  <!-- Shared by every project in the tree. Project-specific settings (UseWPF, OutputType,
       AssemblyName, version) stay in each .csproj. -->
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{xaml,xml,csproj,props,targets,json,yml,yaml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.cs]
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_prefer_braces = true:suggestion
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
```

Naming conventions (private instance fields `_camelCase`, everything public PascalCase, test methods `MethodName_Scenario_ExpectedResult`) are followed by hand; they are not enforced by the build.

- [ ] **Step 2: Create the Core project**

`src/Loadstone.Core/Loadstone.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Loadstone.Core</RootNamespace>
    <!-- CS8524 warns that a switch expression over an enum does not handle unnamed values. Core
         relies on switch expressions over closed enums (Tile, WindowCommand) having NO discard arm,
         so that adding an enum member fails the build (CS8509) until every switch covers it. -->
    <NoWarn>$(NoWarn);CS8524</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Loadstone.Core.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the UI project**

`src/Loadstone.UI/Loadstone.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <AssemblyName>Loadstone</AssemblyName>
    <RootNamespace>Loadstone.UI</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Version>0.1.0</Version>
    <Product>Loadstone</Product>
    <Company>Morten Brudvik</Company>
    <Copyright>Copyright © 2026 Morten Brudvik</Copyright>
    <Description>Snap any window into place: halves, corners, and thirds from the keyboard or by dragging to an edge.</Description>
    <NoWarn>$(NoWarn);CS8524</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WPF-UI" Version="4.3.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Autofac" Version="9.3.2" />
    <PackageReference Include="Autofac.Extensions.DependencyInjection" Version="11.0.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.11" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.4.1" />
    <PackageReference Include="NLog" Version="6.2.0" />
    <PackageReference Include="NLog.Extensions.Logging" Version="6.2.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Loadstone.Core\Loadstone.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Loadstone.UI.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>

</Project>
```

`src/Loadstone.UI/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Loadstone" />

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <!-- Per-Monitor V2: every Win32 rectangle Loadstone reads or writes is in physical pixels. -->
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

`src/Loadstone.UI/AssemblyInfo.cs`:

```csharp
using System.Windows;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
```

`src/Loadstone.UI/App.xaml`:

```xml
<Application
    x:Class="Loadstone.UI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    ShutdownMode="OnExplicitShutdown">

  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ui:ThemesDictionary Theme="Dark" />
        <ui:ControlsDictionary />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>

</Application>
```

`src/Loadstone.UI/App.xaml.cs` (placeholder; Task 21 replaces the body):

```csharp
using System.Windows;

namespace Loadstone.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Task 21 wires the host, tray, hotkeys, and drag monitor here. Until then the app exits.
        Shutdown();
    }
}
```

- [ ] **Step 4: Create the test projects with one smoke test each**

`tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS8524</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="xunit.v3" Version="4.0.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageReference Include="NSubstitute" Version="6.2.0" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="coverlet.collector" Version="10.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Loadstone.Core\Loadstone.Core.csproj" />
  </ItemGroup>

</Project>
```

`tests/Loadstone.Core.Tests/SmokeTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProject_Builds_AndRuns()
    {
        (1 + 1).ShouldBe(2);
    }
}
```

`tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS8524</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="xunit.v3" Version="4.0.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageReference Include="Xunit.StaFact" Version="4.0.23" />
    <PackageReference Include="NSubstitute" Version="6.2.0" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="coverlet.collector" Version="10.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Loadstone.UI\Loadstone.UI.csproj" />
  </ItemGroup>

</Project>
```

`tests/Loadstone.UI.Tests/SmokeTests.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

public class SmokeTests
{
    [WpfFact]
    public void WpfFact_RunsOnStaThread_WithDispatcher()
    {
        Thread.CurrentThread.GetApartmentState().ShouldBe(ApartmentState.STA);
        Dispatcher.CurrentDispatcher.ShouldNotBeNull();
        var window = new Window { Width = 100, Height = 100, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.IsVisible.ShouldBeTrue();
        window.Close();
    }
}
```

- [ ] **Step 5: Create the solution and add the projects**

Run:

```bash
dotnet new sln -n Loadstone
dotnet sln Loadstone.sln add src/Loadstone.Core/Loadstone.Core.csproj src/Loadstone.UI/Loadstone.UI.csproj tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj
```

If `dotnet new sln` produces a `.slnx` file, keep it but name it `Loadstone.slnx` and use that name in every later command and in `ci.yml`.

- [ ] **Step 6: Build and run both smoke tests**

Run: `dotnet build Loadstone.sln` then `dotnet test Loadstone.sln`
Expected: build succeeds with 0 warnings; 2 tests pass. If the UI smoke test fails to find `WpfFact`, the `Xunit.StaFact` package did not resolve against xunit.v3 4.0.0: check `dotnet list package --include-transitive` for `xunit.v3.extensibility.core` and, if it is absent, replace `[WpfFact]` with `[Fact]` plus a manual STA thread helper (see spec 10.2) before continuing.

- [ ] **Step 7: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
  workflow_dispatch:

# A new push to the same branch cancels the run still going for the old one.
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  test:
    name: Build and test
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore Loadstone.sln

      - name: Build
        run: dotnet build Loadstone.sln -c Release --no-restore

      # Real-window tests run here too: the hosted Windows runner has an interactive desktop.
      - name: Test
        run: dotnet test Loadstone.sln -c Release --no-build --logger "trx" --results-directory TestResults

      - name: Upload test results
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: TestResults
          retention-days: 14
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F - <<'EOF'
Scaffold the solution, test projects, and CI workflow

Two source projects (Core without Win32 or WPF, UI as the WPF tray app),
two xUnit v3 test projects with one smoke test each, shared build
properties with warnings as errors, and a windows-latest workflow.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 2: Geometry primitives and tile frames

Spec section 4.1 (Tile). Mac reference: `Loadstone/Core/Tile.swift`, `Tile.frame(in:)`. The Mac floors the left and bottom pieces; in y-down coordinates the same rule floors the left and top pieces and gives the remainder to the right and bottom ones.

**Files:**
- Create: `src/Loadstone.Core/Geometry/Point.cs`, `src/Loadstone.Core/Geometry/Rect.cs`, `src/Loadstone.Core/Geometry/Tile.cs`
- Test: `tests/Loadstone.Core.Tests/Geometry/RectTests.cs`, `tests/Loadstone.Core.Tests/Geometry/TileFrameTests.cs`

**Interfaces:**
- Produces: `Loadstone.Core.Geometry.Point(int X, int Y)`; `Rect(int X, int Y, int Width, int Height)` with `Left`, `Top`, `Right`, `Bottom` (exclusive), `Origin`, `Center`, `FromEdges`, `Contains(Point)`, `Inflate(int dx, int dy)`, `HasSameSize(Rect)`; `enum Tile` with fourteen members; `TileExtensions.Title(this Tile)` and `TileExtensions.Frame(this Tile, Rect workArea)`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Geometry/RectTests.cs`:

```csharp
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class RectTests
{
    [Fact]
    public void Edges_AreExclusiveOnRightAndBottom()
    {
        var r = new Rect(10, 20, 30, 40);
        r.Left.ShouldBe(10);
        r.Top.ShouldBe(20);
        r.Right.ShouldBe(40);
        r.Bottom.ShouldBe(60);
        r.Contains(new Point(39, 59)).ShouldBeTrue();
        r.Contains(new Point(40, 59)).ShouldBeFalse();
        r.Contains(new Point(39, 60)).ShouldBeFalse();
        r.Contains(new Point(10, 20)).ShouldBeTrue();
    }

    [Fact]
    public void FromEdges_RoundTrips()
    {
        Rect.FromEdges(-5, 7, 10, 9).ShouldBe(new Rect(-5, 7, 15, 2));
    }

    [Fact]
    public void Center_UsesFloorDivision()
    {
        new Rect(0, 0, 1201, 901).Center.ShouldBe(new Point(600, 450));
    }

    [Fact]
    public void Inflate_GrowsAndShrinks()
    {
        new Rect(0, 0, 10, 10).Inflate(2, 3).ShouldBe(new Rect(-2, -3, 14, 16));
        new Rect(0, 0, 10, 10).Inflate(-1, -1).ShouldBe(new Rect(1, 1, 8, 8));
    }

    [Fact]
    public void HasSameSize_IgnoresPosition()
    {
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(100, 200, 5, 6)).ShouldBeTrue();
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(0, 0, 5, 7)).ShouldBeFalse();
    }
}
```

`tests/Loadstone.Core.Tests/Geometry/TileFrameTests.cs`:

```csharp
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class TileFrameTests
{
    // 1201 by 901 divides by neither 2 nor 3, and the origin is negative and odd on purpose.
    private static readonly Rect WorkArea = new(-1920, 37, 1201, 901);

    [Theory]
    [InlineData(Tile.LeftHalf, -1920, 37, 600, 901)]
    [InlineData(Tile.RightHalf, -1320, 37, 601, 901)]
    [InlineData(Tile.TopHalf, -1920, 37, 1201, 450)]
    [InlineData(Tile.BottomHalf, -1920, 487, 1201, 451)]
    [InlineData(Tile.TopLeft, -1920, 37, 600, 450)]
    [InlineData(Tile.TopRight, -1320, 37, 601, 450)]
    [InlineData(Tile.BottomLeft, -1920, 487, 600, 451)]
    [InlineData(Tile.BottomRight, -1320, 487, 601, 451)]
    [InlineData(Tile.LeftThird, -1920, 37, 400, 901)]
    [InlineData(Tile.CenterThird, -1520, 37, 400, 901)]
    [InlineData(Tile.RightThird, -1120, 37, 401, 901)]
    [InlineData(Tile.LeftTwoThirds, -1920, 37, 800, 901)]
    [InlineData(Tile.RightTwoThirds, -1520, 37, 801, 901)]
    [InlineData(Tile.Maximize, -1920, 37, 1201, 901)]
    public void Frame_LaysTileIntoWorkArea(Tile tile, int x, int y, int width, int height)
    {
        tile.Frame(WorkArea).ShouldBe(new Rect(x, y, width, height));
    }

    [Theory]
    [InlineData(Tile.LeftHalf, Tile.RightHalf)]
    [InlineData(Tile.TopHalf, Tile.BottomHalf)]
    [InlineData(Tile.LeftTwoThirds, Tile.RightThird)]
    [InlineData(Tile.LeftThird, Tile.RightTwoThirds)]
    public void Frame_ComplementaryTilesMeetExactly(Tile first, Tile second)
    {
        var a = first.Frame(WorkArea);
        var b = second.Frame(WorkArea);
        var horizontal = a.Top == b.Top;
        if (horizontal)
        {
            a.Right.ShouldBe(b.Left);
            (a.Width + b.Width).ShouldBe(WorkArea.Width);
        }
        else
        {
            a.Bottom.ShouldBe(b.Top);
            (a.Height + b.Height).ShouldBe(WorkArea.Height);
        }
    }

    [Fact]
    public void Frame_ThirdsTileTheWholeWidth()
    {
        var left = Tile.LeftThird.Frame(WorkArea);
        var center = Tile.CenterThird.Frame(WorkArea);
        var right = Tile.RightThird.Frame(WorkArea);
        left.Right.ShouldBe(center.Left);
        center.Right.ShouldBe(right.Left);
        right.Right.ShouldBe(WorkArea.Right);
    }

    [Fact]
    public void Title_CoversEveryTile()
    {
        foreach (var tile in Enum.GetValues<Tile>())
        {
            tile.Title().ShouldNotBeNullOrWhiteSpace();
        }

        Tile.TopLeft.Title().ShouldBe("Top Left Corner");
        Tile.LeftTwoThirds.Title().ShouldBe("Left Two Thirds");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'Rect' could not be found" (and `Point`, `Tile`).

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Geometry/Point.cs`:

```csharp
namespace Loadstone.Core.Geometry;

/// <summary>A point in physical pixels, top-left origin, y down.</summary>
public readonly record struct Point(int X, int Y);
```

`src/Loadstone.Core/Geometry/Rect.cs`:

```csharp
namespace Loadstone.Core.Geometry;

/// <summary>
/// An axis-aligned rectangle in physical pixels, top-left origin, y down.
/// <see cref="Right"/> and <see cref="Bottom"/> are exclusive, like Win32's RECT.
/// </summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public Point Origin => new(X, Y);
    public Point Center => new(X + Width / 2, Y + Height / 2);

    public static Rect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>Grows by <paramref name="dx"/> on the left and right and <paramref name="dy"/> on the top and bottom. Negative values shrink.</summary>
    public Rect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public bool HasSameSize(Rect other) => Width == other.Width && Height == other.Height;

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
```

`src/Loadstone.Core/Geometry/Tile.cs`:

```csharp
namespace Loadstone.Core.Geometry;

/// <summary>A named screen tile, laid into a work area in y-down coordinates.</summary>
public enum Tile
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    LeftTwoThirds,
    RightTwoThirds,
    Maximize,
}

public static class TileExtensions
{
    // No discard arm on purpose: a new Tile member fails the build here until it is covered.
    public static string Title(this Tile tile) => tile switch
    {
        Tile.LeftHalf => "Left Half",
        Tile.RightHalf => "Right Half",
        Tile.TopHalf => "Top Half",
        Tile.BottomHalf => "Bottom Half",
        Tile.TopLeft => "Top Left Corner",
        Tile.TopRight => "Top Right Corner",
        Tile.BottomLeft => "Bottom Left Corner",
        Tile.BottomRight => "Bottom Right Corner",
        Tile.LeftThird => "Left Third",
        Tile.CenterThird => "Center Third",
        Tile.RightThird => "Right Third",
        Tile.LeftTwoThirds => "Left Two Thirds",
        Tile.RightTwoThirds => "Right Two Thirds",
        Tile.Maximize => "Maximize",
    };

    /// <summary>
    /// Lays the tile into <paramref name="workArea"/>. The left and top pieces are floored and the
    /// right and bottom pieces get the remainder, so complementary tiles meet exactly on odd sizes.
    /// LeftTwoThirds is 2 * thirdW wide so it ends exactly where RightThird starts.
    /// </summary>
    public static Rect Frame(this Tile tile, Rect workArea)
    {
        int x = workArea.X, y = workArea.Y, w = workArea.Width, h = workArea.Height;
        int halfW = w / 2, halfH = h / 2, thirdW = w / 3, twoThirdsW = 2 * thirdW;

        return tile switch
        {
            Tile.LeftHalf => new Rect(x, y, halfW, h),
            Tile.RightHalf => new Rect(x + halfW, y, w - halfW, h),
            Tile.TopHalf => new Rect(x, y, w, halfH),
            Tile.BottomHalf => new Rect(x, y + halfH, w, h - halfH),
            Tile.TopLeft => new Rect(x, y, halfW, halfH),
            Tile.TopRight => new Rect(x + halfW, y, w - halfW, halfH),
            Tile.BottomLeft => new Rect(x, y + halfH, halfW, h - halfH),
            Tile.BottomRight => new Rect(x + halfW, y + halfH, w - halfW, h - halfH),
            Tile.LeftThird => new Rect(x, y, thirdW, h),
            Tile.CenterThird => new Rect(x + thirdW, y, thirdW, h),
            Tile.RightThird => new Rect(x + 2 * thirdW, y, w - 2 * thirdW, h),
            Tile.LeftTwoThirds => new Rect(x, y, twoThirdsW, h),
            Tile.RightTwoThirds => new Rect(x + thirdW, y, w - thirdW, h),
            Tile.Maximize => workArea,
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all tests pass (5 in RectTests, 14 + 4 + 1 + 1 in TileFrameTests, plus the smoke test).

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Geometry tests/Loadstone.Core.Tests/Geometry
git commit -F - <<'EOF'
Add Rect, Point, and the fourteen tile frames

Ported from the Mac's Tile.frame(in:) to y-down coordinates: the left and
top pieces are floored and the right and bottom pieces take the remainder,
so complementary tiles meet exactly on odd work areas.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 3: Display, screen geometry, and layout helpers

Spec section 4.1 (Display, ScreenGeometry, Layout). Mac reference: `ScreenGeometry.swift`, `Display.swift`, and `Layout` in `Tile.swift`. Reading order sorts by x ascending then y ascending; the Mac sorted by maxY descending because its y axis pointed up.

**Files:**
- Create: `src/Loadstone.Core/Geometry/Display.cs`, `src/Loadstone.Core/Geometry/Layout.cs`, `src/Loadstone.Core/Geometry/ScreenGeometry.cs`
- Test: `tests/Loadstone.Core.Tests/Geometry/LayoutTests.cs`, `tests/Loadstone.Core.Tests/Geometry/ScreenGeometryTests.cs`

**Interfaces:**
- Consumes: `Rect`, `Point` from Task 2.
- Produces: `sealed record Display(Rect Frame, Rect WorkArea, int Dpi)` with `double Scale`; `Layout.Centered(Rect current, Rect workArea)`, `Layout.Mapped(Rect current, Rect source, Rect destination)`; `ScreenGeometry.DisplayContaining(Point, IReadOnlyList<Display>)`, `ScreenGeometry.DisplayContaining(Rect, IReadOnlyList<Display>)`, `ScreenGeometry.Neighbor(Display, int delta, IReadOnlyList<Display>)`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Geometry/LayoutTests.cs`:

```csharp
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class LayoutTests
{
    private static readonly Rect WorkArea = new(0, 0, 1201, 901);

    [Fact]
    public void Centered_KeepsSizeAndCenters()
    {
        Layout.Centered(new Rect(10, 10, 401, 301), WorkArea).ShouldBe(new Rect(400, 300, 401, 301));
    }

    [Fact]
    public void Centered_ClampsToWorkArea()
    {
        Layout.Centered(new Rect(0, 0, 5000, 200), WorkArea).ShouldBe(new Rect(0, 350, 1201, 200));
    }

    [Fact]
    public void Mapped_KeepsAHalfAHalf()
    {
        var source = new Rect(0, 0, 1201, 901);
        var destination = new Rect(2000, -100, 1000, 700);
        var leftHalf = Tile.LeftHalf.Frame(source);
        Layout.Mapped(leftHalf, source, destination).ShouldBe(new Rect(2000, -100, 500, 700));
    }

    [Fact]
    public void Mapped_KeepsRelativeOrigin()
    {
        var source = new Rect(0, 0, 1000, 1000);
        var destination = new Rect(0, 2000, 500, 500);
        Layout.Mapped(new Rect(100, 200, 300, 400), source, destination).ShouldBe(new Rect(50, 2100, 150, 200));
    }

    [Fact]
    public void Mapped_DegenerateSource_YieldsDestination()
    {
        var destination = new Rect(1, 2, 3, 4);
        Layout.Mapped(new Rect(0, 0, 10, 10), new Rect(0, 0, 0, 10), destination).ShouldBe(destination);
    }
}
```

`tests/Loadstone.Core.Tests/Geometry/ScreenGeometryTests.cs`:

```csharp
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class ScreenGeometryTests
{
    // Primary 4K at 150%, a 100% display to its left with a negative origin, a third stacked under the primary.
    private static readonly Display Primary = new(new Rect(0, 0, 3840, 2160), new Rect(0, 0, 3840, 2088), 144);
    private static readonly Display LeftOf = new(new Rect(-2560, 0, 2560, 1440), new Rect(-2560, 0, 2560, 1392), 96);
    private static readonly Display Below = new(new Rect(0, 2160, 1920, 1080), new Rect(0, 2160, 1920, 1040), 96);
    private static readonly Display[] Displays = [Primary, LeftOf, Below];

    [Fact]
    public void DisplayContaining_Point_ExactContainmentWins()
    {
        ScreenGeometry.DisplayContaining(new Point(-1, 10), Displays).ShouldBe(LeftOf);
        ScreenGeometry.DisplayContaining(new Point(0, 10), Displays).ShouldBe(Primary);
        ScreenGeometry.DisplayContaining(new Point(100, 2160), Displays).ShouldBe(Below);
    }

    [Fact]
    public void DisplayContaining_Point_OnOuterEdge_UsesOnePixelTolerance()
    {
        ScreenGeometry.DisplayContaining(new Point(3840, 100), Displays).ShouldBe(Primary);
        ScreenGeometry.DisplayContaining(new Point(100, -1), Displays).ShouldBe(Primary);
    }

    [Fact]
    public void DisplayContaining_Point_OnNoDisplay_IsNull()
    {
        ScreenGeometry.DisplayContaining(new Point(9000, 9000), Displays).ShouldBeNull();
        ScreenGeometry.DisplayContaining(new Point(0, 0), []).ShouldBeNull();
    }

    [Fact]
    public void DisplayContaining_Rect_UsesTheCenter()
    {
        // Straddles the shared edge; the center is on the left display.
        ScreenGeometry.DisplayContaining(new Rect(-700, 100, 1000, 500), Displays).ShouldBe(LeftOf);
        ScreenGeometry.DisplayContaining(new Rect(-300, 100, 1000, 500), Displays).ShouldBe(Primary);
    }

    [Fact]
    public void Neighbor_WalksReadingOrderAndWraps()
    {
        // Reading order: LeftOf (x=-2560), Primary (x=0, y=0), Below (x=0, y=2160).
        ScreenGeometry.Neighbor(LeftOf, +1, Displays).ShouldBe(Primary);
        ScreenGeometry.Neighbor(Primary, +1, Displays).ShouldBe(Below);
        ScreenGeometry.Neighbor(Below, +1, Displays).ShouldBe(LeftOf);
        ScreenGeometry.Neighbor(LeftOf, -1, Displays).ShouldBe(Below);
        ScreenGeometry.Neighbor(Primary, -1, Displays).ShouldBe(LeftOf);
    }

    [Fact]
    public void Neighbor_SingleDisplay_IsItself()
    {
        ScreenGeometry.Neighbor(Primary, +1, [Primary]).ShouldBe(Primary);
        ScreenGeometry.Neighbor(Primary, -1, [Primary]).ShouldBe(Primary);
    }

    [Fact]
    public void Neighbor_UnknownDisplay_IsNull()
    {
        ScreenGeometry.Neighbor(Below, +1, [Primary, LeftOf]).ShouldBeNull();
    }

    [Fact]
    public void Scale_IsDpiOver96()
    {
        Primary.Scale.ShouldBe(1.5);
        LeftOf.Scale.ShouldBe(1.0);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'Display' could not be found" (and `Layout`, `ScreenGeometry`).

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Geometry/Display.cs`:

```csharp
namespace Loadstone.Core.Geometry;

/// <summary>
/// A snapshot of one attached display in physical pixels: the full monitor rectangle, the work area
/// (the monitor minus the taskbar), and its DPI. A plain value so layout and multi-display logic can
/// be tested without a real monitor.
/// </summary>
public sealed record Display(Rect Frame, Rect WorkArea, int Dpi)
{
    /// <summary>1.0 at 100 percent scaling, 1.5 at 150 percent, and so on.</summary>
    public double Scale => Dpi / 96.0;
}
```

`src/Loadstone.Core/Geometry/Layout.cs`:

```csharp
namespace Loadstone.Core.Geometry;

public static class Layout
{
    /// <summary>Keeps the window's size (clamped to the work area) and centres it.</summary>
    public static Rect Centered(Rect current, Rect workArea)
    {
        var width = Math.Min(current.Width, workArea.Width);
        var height = Math.Min(current.Height, workArea.Height);
        return new Rect(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height);
    }

    /// <summary>
    /// Places <paramref name="current"/> in <paramref name="destination"/> at the same relative
    /// position and size it had in <paramref name="source"/>, so a half stays a half on the new
    /// display. A degenerate source yields the whole destination.
    /// </summary>
    public static Rect Mapped(Rect current, Rect source, Rect destination)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return destination;
        }

        var rx = (current.X - source.X) / (double)source.Width;
        var ry = (current.Y - source.Y) / (double)source.Height;
        var rw = current.Width / (double)source.Width;
        var rh = current.Height / (double)source.Height;

        return new Rect(
            destination.X + (int)Math.Round(rx * destination.Width),
            destination.Y + (int)Math.Round(ry * destination.Height),
            Math.Max(1, (int)Math.Round(rw * destination.Width)),
            Math.Max(1, (int)Math.Round(rh * destination.Height)));
    }
}
```

`src/Loadstone.Core/Geometry/ScreenGeometry.cs`:

```csharp
namespace Loadstone.Core.Geometry;

public static class ScreenGeometry
{
    /// <summary>
    /// The display whose frame contains <paramref name="point"/>. Exact containment wins, so a point
    /// on the edge shared by two displays goes to the one whose frame includes it. A 1 px tolerance
    /// then catches a cursor resting exactly on the outer top or right edge, which the exclusive
    /// rectangle edges would otherwise miss. Null when the point is on no display.
    /// </summary>
    public static Display? DisplayContaining(Point point, IReadOnlyList<Display> displays)
    {
        foreach (var display in displays)
        {
            if (display.Frame.Contains(point))
            {
                return display;
            }
        }

        foreach (var display in displays)
        {
            if (display.Frame.Inflate(1, 1).Contains(point))
            {
                return display;
            }
        }

        return null;
    }

    /// <summary>The display under the centre of <paramref name="rect"/>; a straddling window belongs to the one holding its centre.</summary>
    public static Display? DisplayContaining(Rect rect, IReadOnlyList<Display> displays) =>
        DisplayContaining(rect.Center, displays);

    /// <summary>
    /// The display <paramref name="delta"/> steps away in reading order (left to right by x, then top
    /// to bottom by y for displays at the same x), wrapping at both ends. With one display this is the
    /// display itself. Null if <paramref name="display"/> is not in the list.
    /// </summary>
    public static Display? Neighbor(Display display, int delta, IReadOnlyList<Display> displays)
    {
        var ordered = displays.OrderBy(d => d.Frame.X).ThenBy(d => d.Frame.Y).ToList();
        var index = ordered.IndexOf(display);
        if (index < 0)
        {
            return null;
        }

        return ordered[Modulo(index + delta, ordered.Count)];
    }

    /// <summary>Euclidean modulo: always in 0..modulus-1, unlike % which keeps the sign of the dividend.</summary>
    private static int Modulo(int value, int modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Geometry tests/Loadstone.Core.Tests/Geometry
git commit -F - <<'EOF'
Add Display, ScreenGeometry, and Layout

Display is a value snapshot of a monitor. ScreenGeometry resolves the
display under a point or a rect's centre with a 1 px edge tolerance and
walks displays in reading order for Next/Previous Display. Layout keeps a
window centred or maps it between displays at the same relative frame.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 4: Snap zones

Spec section 4.1 (SnapZones). Mac reference: `SnapZones` in `Tile.swift`. Zones are measured against the full monitor frame, not the work area, and scale with the display's DPI.

**Files:**
- Create: `src/Loadstone.Core/Geometry/SnapZones.cs`
- Test: `tests/Loadstone.Core.Tests/Geometry/SnapZonesTests.cs`

**Interfaces:**
- Consumes: `Point`, `Rect`, `Tile` from Task 2.
- Produces: `SnapZones.EdgeThickness` (16), `SnapZones.CornerSize` (140), `SnapZones.TileAt(Point point, Rect screenFrame, double scale) → Tile?`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Geometry/SnapZonesTests.cs`:

```csharp
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class SnapZonesTests
{
    private static readonly Rect Landscape = new(0, 0, 1201, 901);
    private static readonly Rect Portrait = new(3000, -50, 901, 1201);

    [Theory]
    [InlineData(10, 10, Tile.TopLeft)]
    [InlineData(140, 140, Tile.TopLeft)]
    [InlineData(1190, 10, Tile.TopRight)]
    [InlineData(10, 890, Tile.BottomLeft)]
    [InlineData(1190, 890, Tile.BottomRight)]
    [InlineData(141, 10, Tile.Maximize)]
    [InlineData(600, 0, Tile.Maximize)]
    [InlineData(5, 500, Tile.LeftHalf)]
    [InlineData(0, 500, Tile.LeftHalf)]
    [InlineData(1195, 500, Tile.RightHalf)]
    [InlineData(1200, 500, Tile.RightHalf)]
    [InlineData(300, 895, Tile.LeftThird)]
    [InlineData(600, 895, Tile.CenterThird)]
    [InlineData(1000, 895, Tile.RightThird)]
    public void TileAt_Landscape_MapsBandsAndCorners(int x, int y, Tile expected)
    {
        SnapZones.TileAt(new Point(x, y), Landscape, 1.0).ShouldBe(expected);
    }

    [Theory]
    [InlineData(600, 450)]
    [InlineData(17, 500)]
    [InlineData(600, 883)]
    public void TileAt_Landscape_InsideTheFrameAwayFromEdges_IsNull(int x, int y)
    {
        SnapZones.TileAt(new Point(x, y), Landscape, 1.0).ShouldBeNull();
    }

    [Fact]
    public void TileAt_OvershootPastAnOuterEdge_StillCounts()
    {
        SnapZones.TileAt(new Point(-16, 500), Landscape, 1.0).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(new Point(-17, 500), Landscape, 1.0).ShouldBeNull();
        SnapZones.TileAt(new Point(600, -16), Landscape, 1.0).ShouldBe(Tile.Maximize);
        SnapZones.TileAt(new Point(1216, 500), Landscape, 1.0).ShouldBe(Tile.RightHalf);
    }

    [Fact]
    public void TileAt_CornerBeatsTopAndSideBands()
    {
        SnapZones.TileAt(new Point(0, 0), Landscape, 1.0).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(new Point(1200, 900), Landscape, 1.0).ShouldBe(Tile.BottomRight);
    }

    [Fact]
    public void TileAt_ScalesBandsWithDpi()
    {
        // At 200 percent the edge band is 32 px and the corner square 280 px.
        SnapZones.TileAt(new Point(30, 500), Landscape, 2.0).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(new Point(33, 500), Landscape, 2.0).ShouldBeNull();
        SnapZones.TileAt(new Point(279, 279), Landscape, 2.0).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(new Point(281, 10), Landscape, 2.0).ShouldBe(Tile.Maximize);
    }

    [Theory]
    [InlineData(3000, 100, Tile.TopLeft)]
    [InlineData(3000, 550, Tile.LeftThird)]
    [InlineData(3000, 1100, Tile.BottomLeft)]
    [InlineData(3900, 100, Tile.TopRight)]
    [InlineData(3900, 550, Tile.RightThird)]
    [InlineData(3900, 1100, Tile.BottomRight)]
    [InlineData(3300, 1150, Tile.LeftHalf)]
    [InlineData(3600, 1150, Tile.RightHalf)]
    [InlineData(3450, -50, Tile.Maximize)]
    public void TileAt_Portrait_SplitsSidesAlongTheHeightAndBottomIntoHalves(int x, int y, Tile expected)
    {
        SnapZones.TileAt(new Point(x, y), Portrait, 1.0).ShouldBe(expected);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The name 'SnapZones' does not exist".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Geometry/SnapZones.cs`:

```csharp
namespace Loadstone.Core.Geometry;

public static class SnapZones
{
    /// <summary>Depth of the edge bands at 100 percent, and how far past an outer edge the cursor may overshoot.</summary>
    public const int EdgeThickness = 16;

    /// <summary>Side of the square corner zones at 100 percent. Tuned by feel, like the bands.</summary>
    public const int CornerSize = 140;

    /// <summary>
    /// Detects a snap tile from a cursor against the *full* monitor frame (the tile itself is later
    /// laid into the work area). Priority: the corner squares beat everything, then the top band
    /// (maximize), then the side bands, then the bottom band. Bands are <see cref="EdgeThickness"/>
    /// deep and extend the same distance outside the frame so an overshoot past an outer edge still
    /// counts. Portrait (height > width): the side bands split along their length into top corner,
    /// side third, bottom corner, and the bottom band gives halves instead of thirds.
    /// <paramref name="scale"/> is the display's DPI over 96.
    /// </summary>
    public static Tile? TileAt(Point point, Rect screenFrame, double scale)
    {
        var f = screenFrame;
        var edge = (int)Math.Round(EdgeThickness * scale);
        var corner = (int)Math.Round(CornerSize * scale);

        if (!f.Inflate(edge, edge).Contains(point))
        {
            return null;
        }

        var x = Math.Clamp(point.X, f.Left, f.Right - 1);
        var y = Math.Clamp(point.Y, f.Top, f.Bottom - 1);
        var fromLeft = x - f.Left;
        var fromRight = f.Right - 1 - x;
        var fromTop = y - f.Top;
        var fromBottom = f.Bottom - 1 - y;

        if (fromLeft <= corner && fromTop <= corner) return Tile.TopLeft;
        if (fromRight <= corner && fromTop <= corner) return Tile.TopRight;
        if (fromLeft <= corner && fromBottom <= corner) return Tile.BottomLeft;
        if (fromRight <= corner && fromBottom <= corner) return Tile.BottomRight;

        var nearLeft = fromLeft <= edge;
        var nearRight = fromRight <= edge;
        var nearTop = fromTop <= edge;
        var nearBottom = fromBottom <= edge;

        if (nearTop) return Tile.Maximize;

        var portrait = f.Height > f.Width;
        if (portrait)
        {
            if (nearLeft || nearRight)
            {
                return ThirdAlongHeight(fromTop, f.Height, left: nearLeft);
            }

            if (nearBottom)
            {
                return fromLeft < f.Width / 2.0 ? Tile.LeftHalf : Tile.RightHalf;
            }

            return null;
        }

        if (nearLeft) return Tile.LeftHalf;
        if (nearRight) return Tile.RightHalf;
        if (nearBottom) return ThirdAlongWidth(fromLeft, f.Width);
        return null;
    }

    private static Tile ThirdAlongWidth(int fromLeft, int width)
    {
        var t = fromLeft / (double)Math.Max(width, 1);
        if (t < 1.0 / 3.0) return Tile.LeftThird;
        if (t < 2.0 / 3.0) return Tile.CenterThird;
        return Tile.RightThird;
    }

    private static Tile ThirdAlongHeight(int fromTop, int height, bool left)
    {
        var t = fromTop / (double)Math.Max(height, 1);
        if (left)
        {
            if (t < 1.0 / 3.0) return Tile.TopLeft;
            if (t < 2.0 / 3.0) return Tile.LeftThird;
            return Tile.BottomLeft;
        }

        if (t < 1.0 / 3.0) return Tile.TopRight;
        if (t < 2.0 / 3.0) return Tile.RightThird;
        return Tile.BottomRight;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass. If a portrait case fails, check the sign of `fromTop`: it measures from the top edge downward, so the first third along the height is the top corner.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Geometry/SnapZones.cs tests/Loadstone.Core.Tests/Geometry/SnapZonesTests.cs
git commit -F - <<'EOF'
Add the drag snap zones

Corner squares beat the top band, which beats the sides, which beat the
bottom band. Bands scale with the display's DPI and extend past outer
edges so an overshoot still counts. Portrait displays split the sides into
corner, third, corner and give halves along the bottom.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---
### Task 5: Shortcut value and virtual key names

Spec section 4.3. A shortcut is modifiers plus a virtual-key code, with a canonical text form used in settings.json and a display form with arrow glyphs. The modifier flag values equal Win32's `MOD_ALT` (1), `MOD_CONTROL` (2), `MOD_SHIFT` (4), `MOD_WIN` (8) so a cast gives `RegisterHotKey` its argument.

**Files:**
- Create: `src/Loadstone.Core/Commands/KeyModifiers.cs`, `src/Loadstone.Core/Commands/VirtualKeys.cs`, `src/Loadstone.Core/Commands/Shortcut.cs`
- Test: `tests/Loadstone.Core.Tests/Commands/ShortcutTests.cs`

**Interfaces:**
- Produces: `[Flags] enum KeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }`; `static class VirtualKeys` with named constants (`Backspace`, `Tab`, `Enter`, `Escape`, `Space`, `PageUp`, `PageDown`, `End`, `Home`, `Left`, `Up`, `Right`, `Down`, `Insert`, `Delete`, `F1`..`F24` via `F1 + n`, letters `A`..`Z` as `0x41..0x5A`, digits `D0`..`D9` as `0x30..0x39`, modifier codes) plus `NameOf(int vk)`, `TryParse(string name, out int vk)`, `DisplayName(int vk)`, `IsModifier(int vk)`; `sealed record Shortcut(KeyModifiers Modifiers, int VirtualKey)` with `TryParse`, `Parse`, `ToString()` (canonical), `DisplayText`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Commands/ShortcutTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Commands;

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Left", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)]
    [InlineData("ctrl+alt+left", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)]
    [InlineData("Control+Alt+U", KeyModifiers.Control | KeyModifiers.Alt, 0x55)]
    [InlineData("Ctrl+Alt+Win+Right", KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, VirtualKeys.Right)]
    [InlineData("Win+Ctrl+Right", KeyModifiers.Control | KeyModifiers.Win, VirtualKeys.Right)]
    [InlineData("Ctrl+Alt+Enter", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Enter)]
    [InlineData("Ctrl+Alt+Backspace", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Backspace)]
    [InlineData("Shift+F24", KeyModifiers.Shift, VirtualKeys.F1 + 23)]
    [InlineData("F5", KeyModifiers.None, VirtualKeys.F1 + 4)]
    [InlineData("Ctrl+Alt+3", KeyModifiers.Control | KeyModifiers.Alt, 0x33)]
    [InlineData("Ctrl+Alt+Vk0xBA", KeyModifiers.Control | KeyModifiers.Alt, 0xBA)]
    public void TryParse_AcceptsNamesCaseInsensitively(string text, KeyModifiers modifiers, int virtualKey)
    {
        Shortcut.TryParse(text, out var shortcut).ShouldBeTrue();
        shortcut!.Modifiers.ShouldBe(modifiers);
        shortcut.VirtualKey.ShouldBe(virtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Foo+A")]
    [InlineData("Ctrl+Alt+NoSuchKey")]
    [InlineData("Ctrl+Alt+Vk0xZZ")]
    public void TryParse_RejectsMalformedText(string text)
    {
        Shortcut.TryParse(text, out var shortcut).ShouldBeFalse();
        shortcut.ShouldBeNull();
    }

    [Fact]
    public void TryParse_NullText_IsFalse()
    {
        Shortcut.TryParse(null, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("win+ctrl+right", "Ctrl+Win+Right")]
    [InlineData("shift+alt+ctrl+win+a", "Ctrl+Alt+Shift+Win+A")]
    [InlineData("ctrl+alt+vk0xba", "Ctrl+Alt+Vk0xBA")]
    [InlineData("F12", "F12")]
    public void ToString_IsCanonical(string text, string canonical)
    {
        Shortcut.Parse(text).ToString().ShouldBe(canonical);
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse_ForEveryNamedKey()
    {
        var names = new[]
        {
            "Backspace", "Tab", "Enter", "Space", "PageUp", "PageDown", "End", "Home", "Left", "Up",
            "Right", "Down", "Insert", "Delete", "A", "Z", "0", "9", "F1", "F24",
        };
        foreach (var name in names)
        {
            var text = "Ctrl+Alt+" + name;
            Shortcut.TryParse(text, out var shortcut).ShouldBeTrue(text);
            shortcut!.ToString().ShouldBe(text);
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+Left", "Ctrl+Alt+←")]
    [InlineData("Ctrl+Alt+Right", "Ctrl+Alt+→")]
    [InlineData("Ctrl+Alt+Up", "Ctrl+Alt+↑")]
    [InlineData("Ctrl+Alt+Down", "Ctrl+Alt+↓")]
    [InlineData("Ctrl+Alt+Enter", "Ctrl+Alt+↵")]
    [InlineData("Ctrl+Alt+Backspace", "Ctrl+Alt+⌫")]
    [InlineData("Ctrl+Alt+U", "Ctrl+Alt+U")]
    public void DisplayText_UsesGlyphsForArrowsEnterAndBackspace(string text, string display)
    {
        Shortcut.Parse(text).DisplayText.ShouldBe(display);
    }

    [Fact]
    public void Parse_Malformed_Throws()
    {
        Should.Throw<FormatException>(() => Shortcut.Parse("Ctrl+"));
    }

    [Fact]
    public void Shortcuts_AreValues()
    {
        Shortcut.Parse("Ctrl+Alt+E").ShouldBe(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, 0x45));
    }

    [Fact]
    public void IsModifier_RecognisesEveryModifierCode()
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            VirtualKeys.IsModifier(vk).ShouldBeTrue($"0x{vk:X2}");
        }

        VirtualKeys.IsModifier(0x41).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'Shortcut' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Commands/KeyModifiers.cs`:

```csharp
namespace Loadstone.Core.Commands;

/// <summary>Modifier keys of a shortcut. The values equal Win32's MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN so a cast feeds RegisterHotKey.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}
```

`src/Loadstone.Core/Commands/VirtualKeys.cs`:

```csharp
namespace Loadstone.Core.Commands;

/// <summary>Win32 virtual-key codes Loadstone names, and the names it uses for them in settings and the UI.</summary>
public static class VirtualKeys
{
    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int ShiftKey = 0x10;
    public const int ControlKey = 0x11;
    public const int MenuKey = 0x12;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int F1 = 0x70;
    public const int F24 = 0x87;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    private static readonly Dictionary<int, string> NamesByCode = BuildNames();
    private static readonly Dictionary<string, int> CodesByName =
        NamesByCode.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [Backspace] = "Backspace",
            [Tab] = "Tab",
            [Enter] = "Enter",
            [Space] = "Space",
            [PageUp] = "PageUp",
            [PageDown] = "PageDown",
            [End] = "End",
            [Home] = "Home",
            [Left] = "Left",
            [Up] = "Up",
            [Right] = "Right",
            [Down] = "Down",
            [Insert] = "Insert",
            [Delete] = "Delete",
        };
        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var d = '0'; d <= '9'; d++) names[d] = d.ToString();
        for (var i = 0; i < 24; i++) names[F1 + i] = "F" + (i + 1);
        return names;
    }

    /// <summary>The canonical name, or "Vk0xNN" for a code without one.</summary>
    public static string NameOf(int vk) => NamesByCode.TryGetValue(vk, out var name) ? name : $"Vk0x{vk:X2}";

    /// <summary>Parses a canonical name or "Vk0xNN", case-insensitively.</summary>
    public static bool TryParse(string name, out int vk)
    {
        if (CodesByName.TryGetValue(name, out vk))
        {
            return true;
        }

        if (name.StartsWith("Vk0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out vk)
            && vk is > 0 and <= 0xFF)
        {
            return true;
        }

        vk = 0;
        return false;
    }

    /// <summary>The name shown in menus and the recorder: glyphs for arrows, Enter, and Backspace.</summary>
    public static string DisplayName(int vk) => vk switch
    {
        Left => "←",
        Right => "→",
        Up => "↑",
        Down => "↓",
        Enter => "↵",
        Backspace => "⌫",
        _ => NameOf(vk),
    };

    public static bool IsModifier(int vk) =>
        vk is ShiftKey or ControlKey or MenuKey or LWin or RWin or LShift or RShift or LControl or RControl or LMenu or RMenu;
}
```

`src/Loadstone.Core/Commands/Shortcut.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Loadstone.Core.Commands;

/// <summary>
/// A global hotkey chord. Text form: modifiers in the fixed order Ctrl, Alt, Shift, Win joined with
/// "+", then a key name from <see cref="VirtualKeys"/>, for example "Ctrl+Alt+Left".
/// </summary>
public sealed record Shortcut(KeyModifiers Modifiers, int VirtualKey)
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "win":
                    modifiers |= KeyModifiers.Win;
                    break;
                default:
                    return false;
            }
        }

        if (parts[^1].Length == 0 || !VirtualKeys.TryParse(parts[^1], out var vk))
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, vk);
        return true;
    }

    public static Shortcut Parse(string text) =>
        TryParse(text, out var shortcut) ? shortcut : throw new FormatException($"'{text}' is not a shortcut");

    /// <summary>The canonical text form, used as the settings value.</summary>
    public override string ToString() => Prefix() + VirtualKeys.NameOf(VirtualKey);

    /// <summary>The form shown to the user, with glyphs for arrows, Enter, and Backspace.</summary>
    public string DisplayText => Prefix() + VirtualKeys.DisplayName(VirtualKey);

    private string Prefix()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(KeyModifiers.Win)) sb.Append("Win+");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass. A failure on `"Ctrl+Alt"` (two parts, last part "Alt") is correct behavior only if it is rejected: "Alt" is not a key name, so `VirtualKeys.TryParse` returns false.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Commands tests/Loadstone.Core.Tests/Commands
git commit -F - <<'EOF'
Add the Shortcut value with its text and display forms

Modifiers use RegisterHotKey's flag values. Key names cover letters,
digits, F1-F24, arrows, and editing keys, with Vk0xNN for anything else;
parsing is case-insensitive and formatting is canonical.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 6: Window commands, sections, and defaults

Spec section 4.2. Mac reference: `WindowCommand` in `Tile.swift`. `WindowCommand` is an enum of eighteen members so that every switch over it is exhaustive by name; the extension methods carry id, title, section, tile, and default shortcut. Ids are settings keys and never change.

**Files:**
- Create: `src/Loadstone.Core/Commands/CommandSection.cs`, `src/Loadstone.Core/Commands/WindowCommand.cs`, `src/Loadstone.Core/Commands/WindowCommands.cs`
- Test: `tests/Loadstone.Core.Tests/Commands/WindowCommandTests.cs`

**Interfaces:**
- Consumes: `Tile` (Task 2), `Shortcut`, `KeyModifiers`, `VirtualKeys` (Task 5).
- Produces: `enum CommandSection { Halves, Corners, Thirds, Window, Displays }`; `enum WindowCommand` (18 members in menu order); `static class WindowCommands` with `IReadOnlyList<WindowCommand> All`, `IReadOnlyList<CommandSection> SectionOrder`, `IReadOnlyList<WindowCommand> In(CommandSection)`, `WindowCommand? FromId(string)`, and extension methods `Id()`, `Title()`, `Section()`, `ToTile()` (null for the four non-tile commands), `DefaultShortcut()`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Commands/WindowCommandTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Commands;

public class WindowCommandTests
{
    // Pinned: these are settings keys. Renaming one silently discards a user's custom shortcut.
    private static readonly string[] Ids =
    [
        "leftHalf", "rightHalf", "topHalf", "bottomHalf",
        "topLeft", "topRight", "bottomLeft", "bottomRight",
        "leftThird", "centerThird", "rightThird", "leftTwoThirds", "rightTwoThirds",
        "maximize", "center", "restore", "nextDisplay", "previousDisplay",
    ];

    [Fact]
    public void All_HasEveryCommandInMenuOrder_WithPinnedIds()
    {
        WindowCommands.All.Select(c => c.Id()).ShouldBe(Ids);
    }

    [Fact]
    public void FromId_RoundTrips_AndRejectsUnknown()
    {
        foreach (var command in WindowCommands.All)
        {
            WindowCommands.FromId(command.Id()).ShouldBe(command);
        }

        WindowCommands.FromId("LeftHalf").ShouldBeNull();
        WindowCommands.FromId("").ShouldBeNull();
    }

    [Fact]
    public void Sections_GroupCommandsInOrder()
    {
        WindowCommands.SectionOrder.ShouldBe([CommandSection.Halves, CommandSection.Corners, CommandSection.Thirds, CommandSection.Window, CommandSection.Displays]);
        WindowCommands.In(CommandSection.Halves).ShouldBe([WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf, WindowCommand.BottomHalf]);
        WindowCommands.In(CommandSection.Corners).Count.ShouldBe(4);
        WindowCommands.In(CommandSection.Thirds).Count.ShouldBe(5);
        WindowCommands.In(CommandSection.Window).ShouldBe([WindowCommand.Maximize, WindowCommand.Center, WindowCommand.Restore]);
        WindowCommands.In(CommandSection.Displays).ShouldBe([WindowCommand.NextDisplay, WindowCommand.PreviousDisplay]);
    }

    [Theory]
    [InlineData(WindowCommand.LeftHalf, "Ctrl+Alt+Left")]
    [InlineData(WindowCommand.RightHalf, "Ctrl+Alt+Right")]
    [InlineData(WindowCommand.TopHalf, "Ctrl+Alt+Up")]
    [InlineData(WindowCommand.BottomHalf, "Ctrl+Alt+Down")]
    [InlineData(WindowCommand.TopLeft, "Ctrl+Alt+U")]
    [InlineData(WindowCommand.TopRight, "Ctrl+Alt+I")]
    [InlineData(WindowCommand.BottomLeft, "Ctrl+Alt+J")]
    [InlineData(WindowCommand.BottomRight, "Ctrl+Alt+K")]
    [InlineData(WindowCommand.LeftThird, "Ctrl+Alt+D")]
    [InlineData(WindowCommand.CenterThird, "Ctrl+Alt+F")]
    [InlineData(WindowCommand.RightThird, "Ctrl+Alt+G")]
    [InlineData(WindowCommand.LeftTwoThirds, "Ctrl+Alt+E")]
    [InlineData(WindowCommand.RightTwoThirds, "Ctrl+Alt+T")]
    [InlineData(WindowCommand.Maximize, "Ctrl+Alt+Enter")]
    [InlineData(WindowCommand.Center, "Ctrl+Alt+C")]
    [InlineData(WindowCommand.Restore, "Ctrl+Alt+Backspace")]
    [InlineData(WindowCommand.NextDisplay, "Ctrl+Alt+Win+Right")]
    [InlineData(WindowCommand.PreviousDisplay, "Ctrl+Alt+Win+Left")]
    public void DefaultShortcut_MatchesTheMacDefaultsTranslatedToWindows(WindowCommand command, string expected)
    {
        command.DefaultShortcut().ToString().ShouldBe(expected);
    }

    [Fact]
    public void DefaultShortcuts_AreUnique()
    {
        WindowCommands.All.Select(c => c.DefaultShortcut()).Distinct().Count().ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void ToTile_MapsTileCommandsAndNothingElse()
    {
        WindowCommand.LeftHalf.ToTile().ShouldBe(Tile.LeftHalf);
        WindowCommand.Maximize.ToTile().ShouldBe(Tile.Maximize);
        WindowCommand.Center.ToTile().ShouldBeNull();
        WindowCommand.Restore.ToTile().ShouldBeNull();
        WindowCommand.NextDisplay.ToTile().ShouldBeNull();
        WindowCommand.PreviousDisplay.ToTile().ShouldBeNull();
        WindowCommands.All.Count(c => c.ToTile() is not null).ShouldBe(Enum.GetValues<Tile>().Length);
    }

    [Fact]
    public void Title_UsesTileTitlesAndNamesTheRest()
    {
        WindowCommand.TopLeft.Title().ShouldBe("Top Left Corner");
        WindowCommand.Center.Title().ShouldBe("Center");
        WindowCommand.Restore.Title().ShouldBe("Restore");
        WindowCommand.NextDisplay.Title().ShouldBe("Next Display");
        WindowCommand.PreviousDisplay.Title().ShouldBe("Previous Display");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'WindowCommand' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Commands/CommandSection.cs`:

```csharp
namespace Loadstone.Core.Commands;

/// <summary>Groups separated by a divider in the tray menu and by a header in the Shortcuts tab, in that order.</summary>
public enum CommandSection
{
    Halves,
    Corners,
    Thirds,
    Window,
    Displays,
}
```

`src/Loadstone.Core/Commands/WindowCommand.cs`:

```csharp
namespace Loadstone.Core.Commands;

/// <summary>
/// Everything a user can ask Loadstone to do to a window, in menu and settings order. This is the
/// single source of truth for the hotkey list, the tray menu, and the Shortcuts tab: a new member
/// fails the build until every switch in <see cref="WindowCommands"/> covers it, and then it appears
/// everywhere automatically.
/// </summary>
public enum WindowCommand
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    LeftTwoThirds,
    RightTwoThirds,
    Maximize,
    Center,
    Restore,
    NextDisplay,
    PreviousDisplay,
}
```

`src/Loadstone.Core/Commands/WindowCommands.cs`:

```csharp
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Commands;

public static class WindowCommands
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;
    private const KeyModifiers CtrlAltWin = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win;

    /// <summary>Every command, in menu and settings order.</summary>
    public static IReadOnlyList<WindowCommand> All { get; } = Enum.GetValues<WindowCommand>();

    public static IReadOnlyList<CommandSection> SectionOrder { get; } = Enum.GetValues<CommandSection>();

    public static IReadOnlyList<WindowCommand> In(CommandSection section) =>
        All.Where(c => c.Section() == section).ToList();

    public static WindowCommand? FromId(string id)
    {
        foreach (var command in All)
        {
            if (command.Id() == id)
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// Persistence key: a user's custom shortcut is stored under this id in settings.json, so an id
    /// must never change once shipped. They match the Mac app's ids exactly.
    /// </summary>
    public static string Id(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => "leftHalf",
        WindowCommand.RightHalf => "rightHalf",
        WindowCommand.TopHalf => "topHalf",
        WindowCommand.BottomHalf => "bottomHalf",
        WindowCommand.TopLeft => "topLeft",
        WindowCommand.TopRight => "topRight",
        WindowCommand.BottomLeft => "bottomLeft",
        WindowCommand.BottomRight => "bottomRight",
        WindowCommand.LeftThird => "leftThird",
        WindowCommand.CenterThird => "centerThird",
        WindowCommand.RightThird => "rightThird",
        WindowCommand.LeftTwoThirds => "leftTwoThirds",
        WindowCommand.RightTwoThirds => "rightTwoThirds",
        WindowCommand.Maximize => "maximize",
        WindowCommand.Center => "center",
        WindowCommand.Restore => "restore",
        WindowCommand.NextDisplay => "nextDisplay",
        WindowCommand.PreviousDisplay => "previousDisplay",
    };

    public static string Title(this WindowCommand command) => command.ToTile() is { } tile
        ? tile.Title()
        : command switch
        {
            WindowCommand.Center => "Center",
            WindowCommand.Restore => "Restore",
            WindowCommand.NextDisplay => "Next Display",
            WindowCommand.PreviousDisplay => "Previous Display",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands are titled above"),
        };

    public static CommandSection Section(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf or WindowCommand.RightHalf or WindowCommand.TopHalf or WindowCommand.BottomHalf => CommandSection.Halves,
        WindowCommand.TopLeft or WindowCommand.TopRight or WindowCommand.BottomLeft or WindowCommand.BottomRight => CommandSection.Corners,
        WindowCommand.LeftThird or WindowCommand.CenterThird or WindowCommand.RightThird or WindowCommand.LeftTwoThirds or WindowCommand.RightTwoThirds => CommandSection.Thirds,
        WindowCommand.Maximize or WindowCommand.Center or WindowCommand.Restore => CommandSection.Window,
        WindowCommand.NextDisplay or WindowCommand.PreviousDisplay => CommandSection.Displays,
    };

    /// <summary>The tile a command lays out, or null for Center, Restore, and the display moves.</summary>
    public static Tile? ToTile(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => Tile.LeftHalf,
        WindowCommand.RightHalf => Tile.RightHalf,
        WindowCommand.TopHalf => Tile.TopHalf,
        WindowCommand.BottomHalf => Tile.BottomHalf,
        WindowCommand.TopLeft => Tile.TopLeft,
        WindowCommand.TopRight => Tile.TopRight,
        WindowCommand.BottomLeft => Tile.BottomLeft,
        WindowCommand.BottomRight => Tile.BottomRight,
        WindowCommand.LeftThird => Tile.LeftThird,
        WindowCommand.CenterThird => Tile.CenterThird,
        WindowCommand.RightThird => Tile.RightThird,
        WindowCommand.LeftTwoThirds => Tile.LeftTwoThirds,
        WindowCommand.RightTwoThirds => Tile.RightTwoThirds,
        WindowCommand.Maximize => Tile.Maximize,
        WindowCommand.Center or WindowCommand.Restore or WindowCommand.NextDisplay or WindowCommand.PreviousDisplay => null,
    };

    /// <summary>
    /// The Mac defaults translated to Windows: Control-Option becomes Ctrl+Alt, Command becomes Win.
    /// The corner keys U I / J K form a square on the keyboard.
    /// </summary>
    public static Shortcut DefaultShortcut(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => new Shortcut(CtrlAlt, VirtualKeys.Left),
        WindowCommand.RightHalf => new Shortcut(CtrlAlt, VirtualKeys.Right),
        WindowCommand.TopHalf => new Shortcut(CtrlAlt, VirtualKeys.Up),
        WindowCommand.BottomHalf => new Shortcut(CtrlAlt, VirtualKeys.Down),
        WindowCommand.TopLeft => new Shortcut(CtrlAlt, 'U'),
        WindowCommand.TopRight => new Shortcut(CtrlAlt, 'I'),
        WindowCommand.BottomLeft => new Shortcut(CtrlAlt, 'J'),
        WindowCommand.BottomRight => new Shortcut(CtrlAlt, 'K'),
        WindowCommand.LeftThird => new Shortcut(CtrlAlt, 'D'),
        WindowCommand.CenterThird => new Shortcut(CtrlAlt, 'F'),
        WindowCommand.RightThird => new Shortcut(CtrlAlt, 'G'),
        WindowCommand.LeftTwoThirds => new Shortcut(CtrlAlt, 'E'),
        WindowCommand.RightTwoThirds => new Shortcut(CtrlAlt, 'T'),
        WindowCommand.Maximize => new Shortcut(CtrlAlt, VirtualKeys.Enter),
        WindowCommand.Center => new Shortcut(CtrlAlt, 'C'),
        WindowCommand.Restore => new Shortcut(CtrlAlt, VirtualKeys.Backspace),
        WindowCommand.NextDisplay => new Shortcut(CtrlAltWin, VirtualKeys.Right),
        WindowCommand.PreviousDisplay => new Shortcut(CtrlAltWin, VirtualKeys.Left),
    };
}
```

The letter virtual-key codes equal their uppercase ASCII codes, which is why `'U'` is a valid `int` argument.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Commands tests/Loadstone.Core.Tests/Commands
git commit -F - <<'EOF'
Add WindowCommand with pinned ids, sections, and default chords

The enum is the single source of truth for hotkeys, the tray menu, and
the Shortcuts tab. Ids match the Mac app and are settings keys, so a test
pins the full list. Defaults translate Control-Option to Ctrl+Alt and
Command to Win.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 7: Command outcomes, movable windows, and the director

Spec section 4.4. Mac reference: `WindowDirector.swift`, `MovableWindow.swift`. The ledger records a window's pre-Loadstone frame only after the window accepts a write, Restore is one-shot and drops its entry only once the old frame is accepted, and dead identities are pruned before every command.

**Files:**
- Create: `src/Loadstone.Core/Commands/CommandOutcome.cs`, `src/Loadstone.Core/Windows/WindowIdentity.cs`, `src/Loadstone.Core/Windows/IMovableWindow.cs`, `src/Loadstone.Core/Windows/WindowDirector.cs`
- Test: `tests/Loadstone.Core.Tests/Windows/FakeWindow.cs`, `tests/Loadstone.Core.Tests/Windows/WindowDirectorTests.cs`

**Interfaces:**
- Consumes: `Rect`, `Display`, `Tile`, `TileExtensions.Frame`, `Layout`, `ScreenGeometry` (Tasks 2 and 3); `WindowCommand`, `WindowCommands.ToTile` (Task 6).
- Produces: `enum OutcomeKind { Moved, FrameUnreadable, NoDisplay, NoOtherDisplay, NothingToRestore, Rejected }`; `readonly record struct CommandOutcome(OutcomeKind Kind, int Win32Error = 0)` with static `Moved`, `FrameUnreadable`, `NoDisplay`, `NoOtherDisplay`, `NothingToRestore`, `Rejected(int)` and `bool IsMoved`; `readonly record struct WindowIdentity(nint Hwnd, int Pid)`; `interface IMovableWindow { WindowIdentity Identity { get; } Rect? Frame { get; } CommandOutcome SetFrame(Rect frame); }`; `sealed class WindowDirector(Func<IReadOnlyList<Display>> displays, Func<WindowIdentity, bool> isAlive)` with `CommandOutcome Perform(WindowCommand, IMovableWindow)`, `CommandOutcome Snap(Tile, IMovableWindow, Display)`, `int RememberedWindowCount`.

- [ ] **Step 1: Write the fake window and the failing tests**

`tests/Loadstone.Core.Tests/Windows/FakeWindow.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;

namespace Loadstone.Core.Tests.Windows;

/// <summary>A window that accepts every frame unless told otherwise, and records what it was asked to do.</summary>
public sealed class FakeWindow : IMovableWindow
{
    public FakeWindow(Rect frame, nint hwnd = 1, int pid = 100)
    {
        Frame = frame;
        Identity = new WindowIdentity(hwnd, pid);
    }

    public WindowIdentity Identity { get; }
    public Rect? Frame { get; set; }
    public CommandOutcome NextOutcome { get; set; } = CommandOutcome.Moved;
    public List<Rect> Writes { get; } = [];

    public CommandOutcome SetFrame(Rect frame)
    {
        Writes.Add(frame);
        if (NextOutcome.IsMoved)
        {
            Frame = frame;
        }

        return NextOutcome;
    }
}
```

`tests/Loadstone.Core.Tests/Windows/WindowDirectorTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Windows;

public class WindowDirectorTests
{
    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Display Secondary = new(new Rect(1201, -100, 1000, 700), new Rect(1201, -100, 1000, 700), 96);
    private static readonly Rect Start = new(100, 100, 501, 401);

    private readonly HashSet<WindowIdentity> _dead = [];
    private List<Display> _displays = [Primary, Secondary];

    private WindowDirector NewDirector() => new(() => _displays, id => !_dead.Contains(id));

    [Fact]
    public void Perform_Tile_LaysTileIntoTheWorkAreaOfTheWindowsDisplay()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Tile.LeftHalf.Frame(Primary.WorkArea));
    }

    [Fact]
    public void Perform_Center_KeepsSizeAndCenters()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.Center, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Layout.Centered(Start, Primary.WorkArea));
    }

    [Fact]
    public void Perform_UnreadableFrame_DoesNothing()
    {
        var window = new FakeWindow(Start) { Frame = null };
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.FrameUnreadable);
        window.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void Perform_NoDisplays_ReportsNoDisplay()
    {
        _displays = [];
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.NoDisplay);
    }

    [Fact]
    public void Perform_WindowOffEveryDisplay_UsesThePrimaryDisplay()
    {
        var window = new FakeWindow(new Rect(-9000, -9000, 300, 200));
        NewDirector().Perform(WindowCommand.Maximize, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Primary.WorkArea);
    }

    [Fact]
    public void Restore_ReturnsToTheFrameBeforeTheFirstCommand_Once()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window);
        director.Perform(WindowCommand.TopRight, window);
        director.Perform(WindowCommand.Center, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.RememberedWindowCount.ShouldBe(0);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_NeverRecords_SoRestoreAfterRestoreHasNothing()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
        window.Writes.ShouldBeEmpty();
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void Restore_RefusedWrite_KeepsTheEntryForALaterAttempt()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window);

        window.NextOutcome = CommandOutcome.Rejected(5);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Rejected(5));
        director.RememberedWindowCount.ShouldBe(1);

        window.NextOutcome = CommandOutcome.Moved;
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void RefusedFirstWrite_LeavesNothingToRestore()
    {
        var window = new FakeWindow(Start) { NextOutcome = CommandOutcome.Rejected(5) };
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Rejected(5));
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void DeadIdentities_ArePrunedBeforeEveryCommand()
    {
        var gone = new FakeWindow(Start, hwnd: 7, pid: 7);
        var alive = new FakeWindow(Start, hwnd: 8, pid: 8);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, gone);
        director.Perform(WindowCommand.LeftHalf, alive);
        director.RememberedWindowCount.ShouldBe(2);

        _dead.Add(gone.Identity);
        director.Perform(WindowCommand.Center, alive);
        director.RememberedWindowCount.ShouldBe(1);
    }

    [Fact]
    public void Snap_UsesTheGivenDisplay_NotTheOneUnderTheWindow()
    {
        var window = new FakeWindow(Start);
        NewDirector().Snap(Tile.RightHalf, window, Secondary).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Tile.RightHalf.Frame(Secondary.WorkArea));
    }

    [Fact]
    public void Snap_RecordsTheOriginalFrameForRestore()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Snap(Tile.RightHalf, window, Secondary);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void NextDisplay_MapsTheFrameOntoTheNeighbor()
    {
        var window = new FakeWindow(Tile.LeftHalf.Frame(Primary.WorkArea));
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Layout.Mapped(Tile.LeftHalf.Frame(Primary.WorkArea), Primary.WorkArea, Secondary.WorkArea));
    }

    [Fact]
    public void PreviousDisplay_WrapsAround()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.PreviousDisplay, window).ShouldBe(CommandOutcome.Moved);
        ScreenGeometry.DisplayContaining(window.Frame!.Value, _displays).ShouldBe(Secondary);
    }

    [Fact]
    public void NextDisplay_WithOneDisplay_ReportsNoOtherDisplay()
    {
        _displays = [Primary];
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.NoOtherDisplay);
        window.Writes.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'IMovableWindow' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Commands/CommandOutcome.cs`:

```csharp
namespace Loadstone.Core.Commands;

public enum OutcomeKind
{
    Moved,
    /// <summary>The window's frame could not be read, so nothing was attempted.</summary>
    FrameUnreadable,
    /// <summary>No display is attached.</summary>
    NoDisplay,
    /// <summary>Next/Previous Display with only one display attached.</summary>
    NoOtherDisplay,
    /// <summary>Restore was asked for a window Loadstone has not moved (or has already restored).</summary>
    NothingToRestore,
    /// <summary>The write was refused (elevated process, destroyed window); Win32Error says why.</summary>
    Rejected,
}

/// <summary>What became of a command, so callers can log it and give the user a cue.</summary>
public readonly record struct CommandOutcome(OutcomeKind Kind, int Win32Error = 0)
{
    public static CommandOutcome Moved => new(OutcomeKind.Moved);
    public static CommandOutcome FrameUnreadable => new(OutcomeKind.FrameUnreadable);
    public static CommandOutcome NoDisplay => new(OutcomeKind.NoDisplay);
    public static CommandOutcome NoOtherDisplay => new(OutcomeKind.NoOtherDisplay);
    public static CommandOutcome NothingToRestore => new(OutcomeKind.NothingToRestore);
    public static CommandOutcome Rejected(int win32Error) => new(OutcomeKind.Rejected, win32Error);

    public bool IsMoved => Kind == OutcomeKind.Moved;

    public override string ToString() => Kind == OutcomeKind.Rejected ? $"Rejected({Win32Error})" : Kind.ToString();
}
```

`src/Loadstone.Core/Windows/WindowIdentity.cs`:

```csharp
namespace Loadstone.Core.Windows;

/// <summary>
/// How the director recognises a window across commands, so Restore can find its memory. Windows
/// reuses handles after a window closes, so an entry is only valid while the handle is still a
/// window owned by the same process; the director prunes the rest.
/// </summary>
public readonly record struct WindowIdentity(nint Hwnd, int Pid);
```

`src/Loadstone.Core/Windows/IMovableWindow.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>What the director needs from a window. Win32Window is the real one; tests use a fake.</summary>
public interface IMovableWindow
{
    WindowIdentity Identity { get; }

    /// <summary>The visible frame. Null when it cannot be read (window gone, call failed).</summary>
    Rect? Frame { get; }

    /// <summary>Applies the frame. Returns Moved, or Rejected with the Win32 error.</summary>
    CommandOutcome SetFrame(Rect frame);
}
```

`src/Loadstone.Core/Windows/WindowDirector.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>
/// Runs a <see cref="WindowCommand"/> on an <see cref="IMovableWindow"/>. Keeps the restore ledger:
/// the pre-Loadstone frame per window, recorded on the first command of any kind, never overwritten,
/// removed by Restore, so Restore returns the window to where it was before Loadstone first touched
/// it, not to the previous tile.
/// </summary>
public sealed class WindowDirector
{
    private readonly Dictionary<WindowIdentity, Rect> _originals = new();
    private readonly Func<IReadOnlyList<Display>> _displays;
    private readonly Func<WindowIdentity, bool> _isAlive;

    /// <param name="displays">The attached displays, primary first, at the time of the call.</param>
    /// <param name="isAlive">Whether an identity still names the same window; dead ones are pruned before every command.</param>
    public WindowDirector(Func<IReadOnlyList<Display>> displays, Func<WindowIdentity, bool> isAlive)
    {
        _displays = displays;
        _isAlive = isAlive;
    }

    public int RememberedWindowCount => _originals.Count;

    /// <summary>Runs <paramref name="command"/> on <paramref name="window"/>, using the display under the window's centre.</summary>
    public CommandOutcome Perform(WindowCommand command, IMovableWindow window)
    {
        Prune();
        if (window.Frame is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        var displays = _displays();
        if (command.ToTile() is { } tile)
        {
            if (DisplayFor(current, displays) is not { } display)
            {
                return CommandOutcome.NoDisplay;
            }

            return Apply(tile.Frame(display.WorkArea), window, remembering: current);
        }

        switch (command)
        {
            case WindowCommand.Center:
                if (DisplayFor(current, displays) is not { } display)
                {
                    return CommandOutcome.NoDisplay;
                }

                return Apply(Layout.Centered(current, display.WorkArea), window, remembering: current);

            case WindowCommand.Restore:
                // Restore must not record: it would store the current frame and then "restore" to it.
                // The entry is dropped only once the window has accepted the old frame, so an app that
                // refuses the write can still be restored on a later attempt.
                if (!_originals.TryGetValue(window.Identity, out var original))
                {
                    return CommandOutcome.NothingToRestore;
                }

                var outcome = Apply(original, window, remembering: null);
                if (outcome.IsMoved)
                {
                    _originals.Remove(window.Identity);
                }

                return outcome;

            case WindowCommand.NextDisplay:
                return Move(window, current, +1, displays);

            case WindowCommand.PreviousDisplay:
                return Move(window, current, -1, displays);

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands are handled above");
        }
    }

    /// <summary>
    /// Snaps <paramref name="window"/> into <paramref name="tile"/> on <paramref name="display"/>: the
    /// display the drag ended on, which is not necessarily the one under the window's centre when a
    /// wide window straddles two.
    /// </summary>
    public CommandOutcome Snap(Tile tile, IMovableWindow window, Display display)
    {
        Prune();
        if (window.Frame is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        return Apply(tile.Frame(display.WorkArea), window, remembering: current);
    }

    private CommandOutcome Move(IMovableWindow window, Rect current, int delta, IReadOnlyList<Display> displays)
    {
        if (DisplayFor(current, displays) is not { } display
            || ScreenGeometry.Neighbor(display, delta, displays) is not { } neighbor)
        {
            return CommandOutcome.NoDisplay;
        }

        if (neighbor == display)
        {
            return CommandOutcome.NoOtherDisplay;
        }

        return Apply(Layout.Mapped(current, display.WorkArea, neighbor.WorkArea), window, remembering: current);
    }

    /// <summary>
    /// Writes <paramref name="frame"/>, then records <paramref name="remembering"/> as the frame Restore
    /// should return to, but only once the window has accepted the write. Recording afterwards is what
    /// keeps a refused frame, or a command that never ran, from leaving behind a restore entry.
    /// </summary>
    private CommandOutcome Apply(Rect frame, IMovableWindow window, Rect? remembering)
    {
        var outcome = window.SetFrame(frame);
        if (!outcome.IsMoved)
        {
            return outcome;
        }

        if (remembering is { } previous)
        {
            _originals.TryAdd(window.Identity, previous);
        }

        return CommandOutcome.Moved;
    }

    /// <summary>The display under the window's centre, or the primary display when the window is off every display (after a disconnect) so it can still be brought back.</summary>
    private static Display? DisplayFor(Rect frame, IReadOnlyList<Display> displays) =>
        ScreenGeometry.DisplayContaining(frame, displays) ?? (displays.Count > 0 ? displays[0] : null);

    private void Prune()
    {
        foreach (var identity in _originals.Keys.Where(id => !_isAlive(id)).ToList())
        {
            _originals.Remove(identity);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Commands/CommandOutcome.cs src/Loadstone.Core/Windows tests/Loadstone.Core.Tests/Windows
git commit -F - <<'EOF'
Add the window director with its one-shot restore ledger

The ledger is written only after a window accepts a frame, Restore drops
its entry only once the old frame is accepted, and dead identities are
pruned before every command. Drag snaps pass the display the gesture
ended on explicitly.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 8: Drag snap tracker

Spec section 4.5. Mac reference: `DragSnapTracker.swift`. The Windows version is fed by the OS's move-loop events instead of raw mouse events, so it starts already knowing which window is being moved. It arms once the window's origin has changed with its size unchanged, disarms for the rest of the drag on a size change (a resize), and commits at the end even without having armed, so a drag shown as an outline (whose frame only moves at the end) still snaps.

**Files:**
- Create: `src/Loadstone.Core/Drag/SnapTarget.cs`, `src/Loadstone.Core/Drag/DragSnapTracker.cs`
- Test: `tests/Loadstone.Core.Tests/Drag/DragSnapTrackerTests.cs`

**Interfaces:**
- Consumes: `Point`, `Rect`, `Display`, `Tile`, `SnapZones`, `ScreenGeometry` (Tasks 2 to 4).
- Produces: `readonly record struct SnapTarget(Tile Tile, Display Display)`; `sealed class DragSnapTracker<TWindow> where TWindow : class` with `void Begin(TWindow window, Rect baseline, IReadOnlyList<Display> displays)`, `SnapTarget? Update(Point cursor, Rect? currentFrame)`, `(TWindow Window, SnapTarget Target)? End(Point cursor, Rect? finalFrame)`, `void Reset()`, `bool IsDragging`, `TWindow? Window`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Drag/DragSnapTrackerTests.cs`:

```csharp
using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Drag;

public class DragSnapTrackerTests
{
    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Display Right = new(new Rect(1201, 0, 1000, 700), new Rect(1201, 0, 1000, 660), 96);
    private static readonly Display[] Displays = [Primary, Right];
    private static readonly Rect Baseline = new(300, 300, 401, 301);
    private static readonly Point Middle = new(600, 450);
    private static readonly Point LeftEdge = new(3, 450);

    private sealed class Window;

    private static DragSnapTracker<Window> Begun(Window window)
    {
        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(window, Baseline, Displays);
        return tracker;
    }

    [Fact]
    public void Update_BeforeTheWindowMoves_ShowsNoPreview_EvenInAZone()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Update_OnceTheOriginChanges_Arms_AndReportsTheZoneUnderTheCursor()
    {
        var tracker = Begun(new Window());
        tracker.Update(Middle, Baseline with { X = 310 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
        tracker.Update(Middle, Baseline with { X = 500 }).ShouldBeNull();
    }

    [Fact]
    public void Update_NullFrame_ChangesNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, null).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
    }

    [Fact]
    public void Update_SizeChange_DisarmsForTheRestOfTheDrag()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { Width = 500 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.End(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.IsDragging.ShouldBeFalse();
    }

    [Fact]
    public void Update_ResolvesTheZoneOnTheDisplayUnderTheCursor()
    {
        var tracker = Begun(new Window());
        var cursorOnRightDisplay = new Point(2195, 300);
        tracker.Update(cursorOnRightDisplay, Baseline with { X = 1800 }).ShouldBe(new SnapTarget(Tile.RightHalf, Right));
    }

    [Fact]
    public void End_InAZone_AfterArming_CommitsTheWindowAndTarget()
    {
        var window = new Window();
        var tracker = Begun(window);
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        var result = tracker.End(LeftEdge, Baseline with { X = 3 });
        result.ShouldNotBeNull();
        result.Value.Window.ShouldBeSameAs(window);
        result.Value.Target.ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
        tracker.IsDragging.ShouldBeFalse();
        tracker.Window.ShouldBeNull();
    }

    [Fact]
    public void End_OutlineDrag_FrameMovesOnlyAtTheEnd_StillCommits()
    {
        var window = new Window();
        var tracker = Begun(window);
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
        tracker.End(LeftEdge, Baseline with { X = 3 })!.Value.Target.Tile.ShouldBe(Tile.LeftHalf);
    }

    [Fact]
    public void End_FrameEqualToBaseline_CommitsNothing()
    {
        // Escape cancels the OS move loop and puts the window back; a drag with no net movement looks the same.
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void End_OutsideEveryZone_CommitsNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(Middle, Baseline with { X = 3 }).ShouldBeNull();
    }

    [Fact]
    public void End_NullFrame_CommitsNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(LeftEdge, null).ShouldBeNull();
    }

    [Fact]
    public void End_WhenIdle_IsNull()
    {
        new DragSnapTracker<Window>().End(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Begin_ReplacesAnEarlierDrag()
    {
        var first = new Window();
        var second = new Window();
        var tracker = Begun(first);
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.Begin(second, Baseline, Displays);
        tracker.Window.ShouldBeSameAs(second);
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Reset_ReturnsToIdle()
    {
        var tracker = Begun(new Window());
        tracker.Reset();
        tracker.IsDragging.ShouldBeFalse();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'DragSnapTracker<>' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Drag/SnapTarget.cs`:

```csharp
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Drag;

/// <summary>What a drag will snap to: the tile, and the display whose work area it is laid into.</summary>
public readonly record struct SnapTarget(Tile Tile, Display Display);
```

`src/Loadstone.Core/Drag/DragSnapTracker.cs`:

```csharp
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Drag;

/// <summary>
/// The drag-to-snap state machine, kept pure so it can be tested. The monitor feeds it the OS
/// move-loop events plus the two facts it cannot know (where the cursor is, where the window is now)
/// and gets back what to preview and what to commit.
///
/// Windows raises the move-loop events for resizes too, and the window has not moved yet when the
/// loop starts, so the tracker arms only once the window's origin has changed with its size
/// unchanged. A size change is a resize and disarms for the rest of the drag. The commit at the end
/// does not require having armed: a drag shown as an outline only moves the frame when the loop
/// ends, and that drop still snaps.
/// </summary>
public sealed class DragSnapTracker<TWindow> where TWindow : class
{
    private enum Phase
    {
        Idle,
        /// <summary>In a move loop; waiting to see the window's origin change.</summary>
        Tracking,
        /// <summary>The window moved, so this is a window drag; zones are live.</summary>
        Armed,
        /// <summary>The window's size changed: a resize. Nothing snaps until the loop ends.</summary>
        Disarmed,
    }

    private Phase _phase = Phase.Idle;
    private TWindow? _window;
    private Rect _baseline;
    private IReadOnlyList<Display> _displays = [];

    public bool IsDragging => _phase != Phase.Idle;

    public TWindow? Window => _window;

    /// <summary>A window entered its move loop. <paramref name="baseline"/> is its frame at that moment.</summary>
    public void Begin(TWindow window, Rect baseline, IReadOnlyList<Display> displays)
    {
        _window = window;
        _baseline = baseline;
        _displays = displays;
        _phase = Phase.Tracking;
    }

    /// <summary>Feeds a cursor sample. Returns the tile and display to preview, or null to hide the preview.</summary>
    public SnapTarget? Update(Point cursor, Rect? currentFrame)
    {
        if (_phase is Phase.Idle or Phase.Disarmed || currentFrame is not { } frame)
        {
            return null;
        }

        if (!frame.HasSameSize(_baseline))
        {
            _phase = Phase.Disarmed;
            return null;
        }

        if (_phase == Phase.Tracking)
        {
            if (frame.Origin == _baseline.Origin)
            {
                return null;
            }

            _phase = Phase.Armed;
        }

        return TargetAt(cursor);
    }

    /// <summary>The move loop ended. Returns the window and target to commit, or null. Always returns to idle.</summary>
    public (TWindow Window, SnapTarget Target)? End(Point cursor, Rect? finalFrame)
    {
        var window = _window;
        var phase = _phase;
        var baseline = _baseline;
        Reset();

        if (window is null || phase is Phase.Idle or Phase.Disarmed || finalFrame is not { } frame)
        {
            return null;
        }

        if (frame == baseline || !frame.HasSameSize(baseline))
        {
            return null;
        }

        return TargetAt(cursor) is { } target ? (window, target) : null;
    }

    public void Reset()
    {
        _phase = Phase.Idle;
        _window = null;
    }

    private SnapTarget? TargetAt(Point cursor)
    {
        if (ScreenGeometry.DisplayContaining(cursor, _displays) is not { } display)
        {
            return null;
        }

        return SnapZones.TileAt(cursor, display.Frame, display.Scale) is { } tile
            ? new SnapTarget(tile, display)
            : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Drag tests/Loadstone.Core.Tests/Drag
git commit -F - <<'EOF'
Add the drag snap tracker

Fed by the OS move-loop events: arms once the window's origin changes
with its size unchanged, disarms on a resize, previews the zone under the
cursor on the display under the cursor, and commits at the end unless the
frame is back at its baseline.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---
### Task 9: Settings model and service

Spec section 4.6. A JSON file at `%LOCALAPPDATA%\Loadstone\settings.json`, loaded once, written atomically through a temp file, corrupt files backed up as `settings.json.corrupt`. Every change goes through `Update`, which mutates, saves, and raises `Changed`.

**Files:**
- Create: `src/Loadstone.Core/Settings/AppSettings.cs`, `src/Loadstone.Core/Settings/ISettingsService.cs`, `src/Loadstone.Core/Settings/SettingsService.cs`
- Test: `tests/Loadstone.Core.Tests/Settings/SettingsServiceTests.cs`, `tests/Loadstone.Core.Tests/Settings/InMemorySettings.cs`

**Interfaces:**
- Produces: `sealed class AppSettings { bool DragSnappingEnabled = true; Dictionary<string, string> Shortcuts; Dictionary<string, bool>? NativeSnapPreviousValues; }`; `interface ISettingsService { AppSettings Current { get; } void Update(Action<AppSettings> change); event EventHandler? Changed; }`; `sealed class SettingsService(string path, ILogger<SettingsService> logger) : ISettingsService` with `static string DefaultPath`; test helper `InMemorySettings : ISettingsService` reused by later tasks.

- [ ] **Step 1: Write the in-memory fake and the failing tests**

`tests/Loadstone.Core.Tests/Settings/InMemorySettings.cs`:

```csharp
using Loadstone.Core.Settings;

namespace Loadstone.Core.Tests.Settings;

/// <summary>An ISettingsService that never touches disk. Shared by every test that needs settings.</summary>
public sealed class InMemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public int UpdateCount { get; private set; }
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        UpdateCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

`tests/Loadstone.Core.Tests/Settings/SettingsServiceTests.cs`:

```csharp
using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadstone-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private SettingsService NewService() => new(SettingsPath, NullLogger<SettingsService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeTrue();
        service.Current.Shortcuts.ShouldBeEmpty();
        service.Current.NativeSnapPreviousValues.ShouldBeNull();
        File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public void Update_PersistsAndReloads()
    {
        var service = NewService();
        service.Update(s =>
        {
            s.DragSnappingEnabled = false;
            s.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
            s.Shortcuts["center"] = "";
            s.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false };
        });

        var reloaded = NewService();
        reloaded.Current.DragSnappingEnabled.ShouldBeFalse();
        reloaded.Current.Shortcuts.ShouldBe(new Dictionary<string, string> { ["leftHalf"] = "Ctrl+Alt+Shift+Left", ["center"] = "" });
        reloaded.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false });
    }

    [Fact]
    public void Update_WritesCamelCaseJson_AndOmitsTheMarkerWhenNull()
    {
        var service = NewService();
        service.Update(s => s.DragSnappingEnabled = false);
        var json = File.ReadAllText(SettingsPath);
        json.ShouldContain("\"dragSnappingEnabled\": false");
        json.ShouldContain("\"shortcuts\"");
        json.ShouldNotContain("nativeSnapPreviousValues");
    }

    [Fact]
    public void Update_LeavesNoTempFileBehind()
    {
        var service = NewService();
        service.Update(s => s.DragSnappingEnabled = false);
        Directory.GetFiles(_directory).ShouldBe([SettingsPath]);
    }

    [Fact]
    public void Update_RaisesChanged()
    {
        var service = NewService();
        var raised = 0;
        service.Changed += (_, _) => raised++;
        service.Update(_ => { });
        raised.ShouldBe(1);
    }

    [Fact]
    public void Load_CorruptFile_BacksItUpAndUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeTrue();
        File.ReadAllText(SettingsPath + ".corrupt").ShouldBe("{ this is not json");

        service.Update(s => s.DragSnappingEnabled = false);
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"dragSnappingEnabled\": false, \"futureSetting\": 42 }");
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'AppSettings' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Settings/AppSettings.cs`:

```csharp
namespace Loadstone.Core.Settings;

/// <summary>The settings.json document. Property names serialize in camelCase.</summary>
public sealed class AppSettings
{
    /// <summary>Opt-out: absent means on, as on the Mac.</summary>
    public bool DragSnappingEnabled { get; set; } = true;

    /// <summary>
    /// Customized shortcuts keyed by command id. An absent id means the default; an empty string
    /// means the user cleared the binding.
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    /// <summary>
    /// The Windows edge-snapping flags as they were before Loadstone turned them off, parked here for
    /// the length of a run so an unclean exit can be detected and undone at the next launch. Absent
    /// when Loadstone is not running or exited cleanly.
    /// </summary>
    public Dictionary<string, bool>? NativeSnapPreviousValues { get; set; }
}
```

`src/Loadstone.Core/Settings/ISettingsService.cs`:

```csharp
namespace Loadstone.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Applies <paramref name="change"/> to <see cref="Current"/>, saves, and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> change);

    event EventHandler? Changed;
}
```

`src/Loadstone.Core/Settings/SettingsService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger<SettingsService> _logger;
    private readonly Lock _lock = new();

    public SettingsService(string path, ILogger<SettingsService> logger)
    {
        _path = path;
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        lock (_lock)
        {
            change(Current);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt";
            File.Copy(_path, backup, overwrite: true);
            _logger.LogError(ex, "settings.json could not be parsed; backed it up to {Backup} and using defaults", backup);
            return new AppSettings();
        }
    }

    /// <summary>Atomic write: serialize to a temp file, then move it over the target.</summary>
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass. If `Update_WritesCamelCaseJson` fails on spacing, match the exact `WriteIndented` output (`"dragSnappingEnabled": false`) rather than loosening the assertion.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Settings tests/Loadstone.Core.Tests/Settings
git commit -F - <<'EOF'
Add the settings model and its atomic JSON service

Loaded once, every change saved immediately through a temp file, corrupt
files backed up as settings.json.corrupt and replaced by defaults.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 10: Native snap suppression and the startup seams

Spec sections 7.1 and 7.2, Core side. Mac reference: `NativeTiling.swift`. `NativeSnap` captures the user's edge-snapping flags once per suppression, parks them in settings as the crash marker, turns them off, and puts them back on restore. Recovery from an unclean exit runs before capture, otherwise Loadstone would record its own `false` as what the user had.

**Files:**
- Create: `src/Loadstone.Core/Platform/INativeSnapSettings.cs`, `src/Loadstone.Core/Platform/NativeSnap.cs`, `src/Loadstone.Core/Platform/StartupStatus.cs`, `src/Loadstone.Core/Platform/IStartupRegistration.cs`, `src/Loadstone.Core/Platform/StartupApproval.cs`
- Test: `tests/Loadstone.Core.Tests/Platform/NativeSnapTests.cs`, `tests/Loadstone.Core.Tests/Platform/StartupApprovalTests.cs`

**Interfaces:**
- Consumes: `ISettingsService`, `AppSettings` (Task 9), `InMemorySettings` test helper.
- Produces: `interface INativeSnapSettings { bool? Read(string flag); bool Write(string flag, bool value); }`; `sealed class NativeSnap(INativeSnapSettings native, ISettingsService settings, ILogger<NativeSnap> logger)` with `const string DockMoving = "DockMoving"`, `const string SnapSizing = "SnapSizing"`, `static IReadOnlyList<string> ManagedFlags`, `void Apply(bool dragSnappingEnabled)`, `void Restore()`, `bool IsSuppressing`; `enum StartupStatus { NotRegistered, Enabled, DisabledInTaskManager }`; `interface IStartupRegistration { StartupStatus Status { get; } void Enable(); void Disable(); }`; `static class StartupApproval { static bool IsDisabled(byte[]? approvedValue); }`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.Core.Tests/Platform/NativeSnapTests.cs`:

```csharp
using Loadstone.Core.Platform;
using Loadstone.Core.Tests.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

public class NativeSnapTests
{
    private sealed class FakeNativeSnapSettings : INativeSnapSettings
    {
        public Dictionary<string, bool> Values { get; } = new() { ["DockMoving"] = true, ["SnapSizing"] = true };
        public HashSet<string> Unreadable { get; } = [];
        public HashSet<string> Unwritable { get; } = [];
        public List<(string Flag, bool Value)> Writes { get; } = [];

        public bool? Read(string flag) => Unreadable.Contains(flag) ? null : Values.GetValueOrDefault(flag);

        public bool Write(string flag, bool value)
        {
            Writes.Add((flag, value));
            if (Unwritable.Contains(flag))
            {
                return false;
            }

            Values[flag] = value;
            return true;
        }
    }

    private readonly FakeNativeSnapSettings _native = new();
    private readonly InMemorySettings _settings = new();

    private NativeSnap NewNativeSnap() => new(_native, _settings, NullLogger<NativeSnap>.Instance);

    [Fact]
    public void Apply_On_CapturesThenTurnsBothFlagsOff_AndParksTheMarker()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values["DockMoving"].ShouldBeFalse();
        _native.Values["SnapSizing"].ShouldBeFalse();
        _settings.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_RemembersWhatTheUserHad_NotAlwaysTrue()
    {
        _native.Values["SnapSizing"] = false;
        NewNativeSnap().Apply(dragSnappingEnabled: true);
        _settings.Current.NativeSnapPreviousValues!["SnapSizing"].ShouldBeFalse();
    }

    [Fact]
    public void Restore_PutsTheCapturedValuesBack_AndClearsTheMarker()
    {
        _native.Values["SnapSizing"] = false;
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Values["DockMoving"].ShouldBeTrue();
        _native.Values["SnapSizing"].ShouldBeFalse();
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_Off_IsRestore()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Apply(dragSnappingEnabled: false);
        _native.Values["DockMoving"].ShouldBeTrue();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Restore_WithoutSuppression_DoesNothing()
    {
        NewNativeSnap().Restore();
        _native.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_On_Twice_CapturesOnlyOnce()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Apply(dragSnappingEnabled: true);
        _settings.Current.NativeSnapPreviousValues!["DockMoving"].ShouldBeTrue();
    }

    [Fact]
    public void RestoreThenApply_CapturesTheValuesCurrentAtThatTime()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();
        _native.Values["DockMoving"] = false; // the user changed it in between
        snap.Apply(dragSnappingEnabled: true);
        _settings.Current.NativeSnapPreviousValues!["DockMoving"].ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_RestoresTheMarkerBeforeCapturing()
    {
        // A previous run left both flags off and its marker behind.
        _native.Values["DockMoving"] = false;
        _native.Values["SnapSizing"] = false;
        _settings.Current.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // Captured the recovered values, not the leftover false ones, so a later restore is correct.
        _settings.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.Restore();
        _native.Values["DockMoving"].ShouldBeTrue();
        _native.Values["SnapSizing"].ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_UnreadableFlag_IsLeftAloneAndNeverWritten()
    {
        _native.Unreadable.Add("SnapSizing");
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Writes.ShouldAllBe(w => w.Flag == "DockMoving");
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
    }

    [Fact]
    public void Apply_On_UnwritableFlag_DoesNotThrow()
    {
        _native.Unwritable.Add("DockMoving");
        Should.NotThrow(() => NewNativeSnap().Apply(dragSnappingEnabled: true));
    }

    [Fact]
    public void ManagedFlags_AreDockMovingAndSnapSizing()
    {
        NativeSnap.ManagedFlags.ShouldBe(["DockMoving", "SnapSizing"]);
    }
}
```

`tests/Loadstone.Core.Tests/Platform/StartupApprovalTests.cs`:

```csharp
using Loadstone.Core.Platform;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

public class StartupApprovalTests
{
    [Fact]
    public void IsDisabled_MissingValue_MeansEnabled()
    {
        StartupApproval.IsDisabled(null).ShouldBeFalse();
        StartupApproval.IsDisabled([]).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0x02, false)]
    [InlineData(0x06, false)]
    [InlineData(0x03, true)]
    [InlineData(0x07, true)]
    public void IsDisabled_LowBitOfTheFirstByte_MeansDisabled(byte first, bool disabled)
    {
        var value = new byte[12];
        value[0] = first;
        StartupApproval.IsDisabled(value).ShouldBe(disabled);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: build FAILS with "The type or namespace name 'NativeSnap' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.Core/Platform/INativeSnapSettings.cs`:

```csharp
namespace Loadstone.Core.Platform;

/// <summary>One named Windows edge-snapping flag. The UI implements it over SystemParametersInfo.</summary>
public interface INativeSnapSettings
{
    /// <summary>The flag's current value, or null when it could not be read.</summary>
    bool? Read(string flag);

    /// <summary>Writes the flag so that it persists and Explorer is told. False when the write failed.</summary>
    bool Write(string flag, bool value);
}
```

`src/Loadstone.Core/Platform/NativeSnap.cs`:

```csharp
using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Platform;

/// <summary>
/// Windows snaps a window itself when you drag it to an edge, which fights Loadstone's own
/// snapping. While drag snapping is on this turns the dock-moving and snap-sizing flags off,
/// remembers what the user had, and puts it back on restore. The capture is parked in settings.json
/// for the length of the run, so a crash or force-quit is detected at the next launch and undone
/// before a new baseline is captured.
/// </summary>
public sealed class NativeSnap
{
    public const string DockMoving = "DockMoving";
    public const string SnapSizing = "SnapSizing";

    public static IReadOnlyList<string> ManagedFlags { get; } = [DockMoving, SnapSizing];

    private readonly INativeSnapSettings _native;
    private readonly ISettingsService _settings;
    private readonly ILogger<NativeSnap> _logger;

    /// <summary>The user's values before Loadstone touched them; null while not suppressing.</summary>
    private Dictionary<string, bool>? _captured;

    public NativeSnap(INativeSnapSettings native, ISettingsService settings, ILogger<NativeSnap> logger)
    {
        _native = native;
        _settings = settings;
        _logger = logger;
    }

    public bool IsSuppressing => _captured is not null;

    /// <summary>Suppresses Windows' edge snapping while Loadstone's drag snapping is on, restores it while off.</summary>
    public void Apply(bool dragSnappingEnabled)
    {
        if (dragSnappingEnabled)
        {
            Suppress();
        }
        else
        {
            Restore();
        }
    }

    /// <summary>Writes the captured values back and clears the marker. Safe to call when not suppressing.</summary>
    public void Restore()
    {
        if (_captured is null)
        {
            return;
        }

        foreach (var (flag, value) in _captured)
        {
            if (!_native.Write(flag, value))
            {
                _logger.LogError("could not restore Windows edge snapping flag {Flag} to {Value}", flag, value);
            }
        }

        _captured = null;
        _settings.Update(s => s.NativeSnapPreviousValues = null);
    }

    private void Suppress()
    {
        if (_captured is null)
        {
            RecoverFromUncleanExit();

            var captured = new Dictionary<string, bool>();
            foreach (var flag in ManagedFlags)
            {
                if (_native.Read(flag) is { } value)
                {
                    captured[flag] = value;
                }
                else
                {
                    _logger.LogWarning("could not read Windows edge snapping flag {Flag}; leaving it alone", flag);
                }
            }

            _captured = captured;
            // Park the capture where it outlives the process.
            _settings.Update(s => s.NativeSnapPreviousValues = new Dictionary<string, bool>(captured));
        }

        foreach (var flag in _captured.Keys)
        {
            if (!_native.Write(flag, false) || _native.Read(flag) != false)
            {
                _logger.LogError("could not turn off Windows edge snapping flag {Flag}; it will fight drag snapping", flag);
            }
        }
    }

    /// <summary>
    /// A run that crashed or was force-quit never reached Restore, so its marker is still parked in
    /// settings and the user's flags are still off. Put them back before capturing a new baseline:
    /// capturing first would record Loadstone's own false as "what the user had" and make the
    /// change permanent.
    /// </summary>
    private void RecoverFromUncleanExit()
    {
        var stored = _settings.Current.NativeSnapPreviousValues;
        if (stored is null)
        {
            return;
        }

        if (stored.Count > 0)
        {
            _logger.LogWarning("restoring Windows edge snapping flags left behind by an unclean exit");
            foreach (var (flag, value) in stored)
            {
                _native.Write(flag, value);
            }
        }

        _settings.Update(s => s.NativeSnapPreviousValues = null);
    }
}
```

`src/Loadstone.Core/Platform/StartupStatus.cs`:

```csharp
namespace Loadstone.Core.Platform;

public enum StartupStatus
{
    NotRegistered,
    Enabled,
    /// <summary>Registered, but the user switched it off under Task Manager › Startup apps.</summary>
    DisabledInTaskManager,
}
```

`src/Loadstone.Core/Platform/IStartupRegistration.cs`:

```csharp
namespace Loadstone.Core.Platform;

/// <summary>Launch at login. The UI implements it over the HKCU Run key.</summary>
public interface IStartupRegistration
{
    StartupStatus Status { get; }

    /// <summary>Registers the running exe. Throws on a registry failure; callers roll the toggle back.</summary>
    void Enable();

    void Disable();
}
```

`src/Loadstone.Core/Platform/StartupApproval.cs`:

```csharp
namespace Loadstone.Core.Platform;

/// <summary>
/// Decodes the value Task Manager writes under Explorer's StartupApproved\Run key: a binary blob
/// whose first byte is even (0x02, 0x06) when the entry is enabled and odd (0x03, 0x07) when the
/// user disabled it. A missing value means enabled.
/// </summary>
public static class StartupApproval
{
    public static bool IsDisabled(byte[]? approvedValue) =>
        approvedValue is { Length: > 0 } && (approvedValue[0] & 1) == 1;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.Core/Platform tests/Loadstone.Core.Tests/Platform
git commit -F - <<'EOF'
Add NativeSnap and the launch-at-login seams

NativeSnap captures the user's edge-snapping flags once, parks them in
settings as a crash marker, turns them off, and restores them; recovery
from an unclean exit runs before capture. StartupApproval decodes Task
Manager's enable/disable blob.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 11: Interop declarations, display enumeration, and the manageable-window predicate

Spec sections 5.1, 5.3, 5.4. Every Win32 declaration Loadstone uses lives here. Nothing outside `Interop/` names a DLL.

**Files:**
- Create: `src/Loadstone.UI/Interop/NativeConstants.cs`, `src/Loadstone.UI/Interop/NativeStructs.cs`, `src/Loadstone.UI/Interop/NativeMethods.User32.cs`, `src/Loadstone.UI/Interop/NativeMethods.Dwmapi.cs`, `src/Loadstone.UI/Interop/NativeMethods.Shcore.cs`
- Create: `src/Loadstone.UI/Windows/Displays.cs`, `src/Loadstone.UI/Windows/WindowStyles.cs`
- Test: `tests/Loadstone.UI.Tests/TestSetup.cs`, `tests/Loadstone.UI.Tests/Windows/DisplaysTests.cs`, `tests/Loadstone.UI.Tests/Windows/WindowStylesTests.cs`

**Interfaces:**
- Consumes: `Display`, `Rect` (Tasks 2 and 3).
- Produces: `internal static partial class NativeMethods` with nested `User32`, `Dwmapi`, `Shcore`; `NativeConstants`; `RECT`, `POINT`, `MONITORINFO`; `internal static class Displays { static IReadOnlyList<Display> Snapshot(); static Rect ToRect(RECT) }`; `internal readonly record struct WindowSnapshot(bool IsWindow, bool IsVisible, bool IsCloaked, int Pid, string ClassName, long Style, long ExStyle)`; `internal static class WindowStyles { static bool IsManageable(WindowSnapshot, int ownPid); static WindowSnapshot Snapshot(nint hwnd); }`.

- [ ] **Step 1: Write the test-host DPI initializer and the failing tests**

`tests/Loadstone.UI.Tests/TestSetup.cs`:

```csharp
using System.Runtime.CompilerServices;
using Loadstone.UI.Interop;

namespace Loadstone.UI.Tests;

internal static class TestSetup
{
    /// <summary>
    /// The test host is not DPI aware by default, which would make every rectangle it reads a scaled
    /// lie on a 150 percent display. Opt in to Per-Monitor V2 before any window exists.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeMethods.User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
```

`tests/Loadstone.UI.Tests/Windows/DisplaysTests.cs`:

```csharp
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class DisplaysTests
{
    [Fact]
    public void Snapshot_ReturnsThePrimaryDisplayFirst_WithSaneGeometry()
    {
        var displays = Displays.Snapshot();
        displays.Count.ShouldBeGreaterThan(0);

        var primary = displays[0];
        primary.Frame.Contains(new Loadstone.Core.Geometry.Point(0, 0)).ShouldBeTrue("the primary display's frame starts at the origin");
        primary.Dpi.ShouldBeGreaterThanOrEqualTo(96);

        foreach (var display in displays)
        {
            display.WorkArea.Width.ShouldBeLessThanOrEqualTo(display.Frame.Width);
            display.WorkArea.Height.ShouldBeLessThanOrEqualTo(display.Frame.Height);
            display.WorkArea.Left.ShouldBeGreaterThanOrEqualTo(display.Frame.Left);
            display.WorkArea.Top.ShouldBeGreaterThanOrEqualTo(display.Frame.Top);
        }
    }
}
```

`tests/Loadstone.UI.Tests/Windows/WindowStylesTests.cs`:

```csharp
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class WindowStylesTests
{
    private static readonly WindowSnapshot Normal = new(
        IsWindow: true, IsVisible: true, IsCloaked: false, Pid: 4242, ClassName: "Notepad",
        Style: NativeConstants.WS_CAPTION | NativeConstants.WS_THICKFRAME, ExStyle: 0);

    [Fact]
    public void IsManageable_NormalWindow_IsTrue()
    {
        WindowStyles.IsManageable(Normal, ownPid: 1).ShouldBeTrue();
    }

    [Fact]
    public void IsManageable_OwnProcess_IsFalse()
    {
        WindowStyles.IsManageable(Normal, ownPid: 4242).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_NotAWindow_OrHidden_OrCloaked_IsFalse()
    {
        WindowStyles.IsManageable(Normal with { IsWindow = false }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { IsVisible = false }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { IsCloaked = true }, 1).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void IsManageable_ShellClasses_AreFalse(string className)
    {
        WindowStyles.IsManageable(Normal with { ClassName = className }, 1).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_ToolWindow_IsFalse()
    {
        WindowStyles.IsManageable(Normal with { ExStyle = NativeConstants.WS_EX_TOOLWINDOW }, 1).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_NeedsACaptionOrASizingFrame()
    {
        WindowStyles.IsManageable(Normal with { Style = 0 }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_CAPTION }, 1).ShouldBeTrue();
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_THICKFRAME }, 1).ShouldBeTrue();
        // WS_BORDER alone is only half of WS_CAPTION.
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_BORDER }, 1).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'Interop' does not exist in the namespace 'Loadstone.UI'".

- [ ] **Step 3: Write the interop declarations**

`src/Loadstone.UI/Interop/NativeConstants.cs`:

```csharp
namespace Loadstone.UI.Interop;

internal static class NativeConstants
{
    // Window styles
    public const long WS_BORDER = 0x00800000;
    public const long WS_CAPTION = 0x00C00000; // WS_BORDER | WS_DLGFRAME
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_EX_TOPMOST = 0x00000008;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // GetAncestor
    public const uint GA_ROOT = 2;

    // ShowWindow
    public const int SW_RESTORE = 9;

    // SetWindowPos
    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // DWM
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_CLOAKED = 14;

    // Monitors
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // Hotkeys
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public static readonly nint HWND_MESSAGE = -3;

    // WinEvents
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    // SystemParametersInfo. For the SET actions the BOOL travels in uiParam and pvParam is zero
    // (verified on Windows 11 25H2 on 2026-09-07; passing it in pvParam silently writes FALSE).
    public const uint SPI_GETSNAPSIZING = 0x008E;
    public const uint SPI_SETSNAPSIZING = 0x008F;
    public const uint SPI_GETDOCKMOVING = 0x0090;
    public const uint SPI_SETDOCKMOVING = 0x0091;
    public const uint SPIF_UPDATEINIFILE = 0x0001;
    public const uint SPIF_SENDCHANGE = 0x0002;

    // Keyboard
    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint TOUNICODE_NO_KEYBOARD_STATE_CHANGE = 0x0004;

    // MessageBeep
    public const uint MB_OK = 0x0000;
}
```

`src/Loadstone.UI/Interop/NativeStructs.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}
```

`src/Loadstone.UI/Interop/NativeMethods.User32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class User32
    {
        internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

        internal delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

        [LibraryImport("user32.dll")]
        internal static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        internal static partial nint GetAncestor(nint hwnd, uint gaFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindow(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsZoomed(nint hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

        // x64 only: GetWindowLongPtrW does not exist in the 32-bit user32. Loadstone ships x64 only.
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetClassName(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(nint hWnd, int id);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWinEvent(nint hWinEventHook);

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SystemParametersInfoGet(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SystemParametersInfoSet(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool MessageBeep(uint uType);

        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(nint hwnd);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int ToUnicodeEx(uint wVirtKey, uint wScanCode, [In] byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
        internal static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

        [LibraryImport("user32.dll")]
        internal static partial nint GetKeyboardLayout(uint idThread);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetProcessDpiAwarenessContext(nint value);
    }
}
```

`src/Loadstone.UI/Interop/NativeMethods.Dwmapi.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Dwmapi
    {
        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static partial int DwmGetWindowAttributeRect(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static partial int DwmGetWindowAttributeInt(nint hwnd, uint dwAttribute, out int pvAttribute, uint cbAttribute);
    }
}
```

`src/Loadstone.UI/Interop/NativeMethods.Shcore.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Shcore
    {
        [LibraryImport("shcore.dll")]
        internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
```

- [ ] **Step 4: Write Displays and WindowStyles**

`src/Loadstone.UI/Windows/Displays.cs`:

```csharp
using System.Runtime.InteropServices;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class Displays
{
    /// <summary>Every attached display, primary first, in physical pixels. Empty when nothing is attached.</summary>
    public static IReadOnlyList<Display> Snapshot()
    {
        var found = new List<(Display Display, bool Primary)>();

        User32.MonitorEnumProc callback = (nint hMonitor, nint hdc, ref RECT clip, nint data) =>
        {
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!User32.GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var dpi = Shcore.GetDpiForMonitor(hMonitor, NativeConstants.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? (int)dpiX : 96;
            var display = new Display(ToRect(info.rcMonitor), ToRect(info.rcWork), dpi);
            found.Add((display, (info.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0));
            return true;
        };

        User32.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        return found.OrderByDescending(f => f.Primary).Select(f => f.Display).ToList();
    }

    public static Rect ToRect(RECT r) => Rect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
```

`src/Loadstone.UI/Windows/WindowStyles.cs`:

```csharp
using System.Runtime.InteropServices;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>The facts about a window that decide whether Loadstone will manage it, captured so the decision is pure and testable.</summary>
internal readonly record struct WindowSnapshot(
    bool IsWindow,
    bool IsVisible,
    bool IsCloaked,
    int Pid,
    string ClassName,
    long Style,
    long ExStyle);

internal static class WindowStyles
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// A window Loadstone may move: visible, not cloaked (other virtual desktop, suspended UWP frame),
    /// not Loadstone's own, not the desktop or the taskbar, with a caption or a sizing frame, and not a
    /// tool window.
    /// </summary>
    public static bool IsManageable(WindowSnapshot s, int ownPid)
    {
        if (!s.IsWindow || !s.IsVisible || s.IsCloaked || s.Pid == ownPid)
        {
            return false;
        }

        if (ShellClasses.Contains(s.ClassName))
        {
            return false;
        }

        var hasCaption = (s.Style & NativeConstants.WS_CAPTION) == NativeConstants.WS_CAPTION;
        var hasSizingFrame = (s.Style & NativeConstants.WS_THICKFRAME) != 0;
        var isToolWindow = (s.ExStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0;
        return (hasCaption || hasSizingFrame) && !isToolWindow;
    }

    public static WindowSnapshot Snapshot(nint hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return new WindowSnapshot(false, false, false, 0, "", 0, 0);
        }

        var cloaked = Dwmapi.DwmGetWindowAttributeInt(hwnd, NativeConstants.DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var buffer = new char[256];
        var length = User32.GetClassName(hwnd, buffer, buffer.Length);

        return new WindowSnapshot(
            IsWindow: true,
            IsVisible: User32.IsWindowVisible(hwnd),
            IsCloaked: cloaked,
            Pid: (int)pid,
            ClassName: new string(buffer, 0, Math.Max(0, length)),
            Style: User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_STYLE),
            ExStyle: User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass. If the build reports SYSLIB1051 for a `LibraryImport` parameter, the marshalling of that parameter is unsupported by the generator: for `[Out] char[]` use `Span<char>` instead, and for delegates keep the declaration as shown (the generator supports delegate parameters). If `SetProcessDpiAwarenessContext` returns false in the module initializer, the host already set an awareness; that is harmless.

- [ ] **Step 6: Commit**

```bash
git add src/Loadstone.UI/Interop src/Loadstone.UI/Windows tests/Loadstone.UI.Tests
git commit -F - <<'EOF'
Add the Win32 interop layer, display enumeration, and the manageable-window rule

LibraryImport declarations for user32, dwmapi, and shcore, a display
snapshot in physical pixels, and a pure predicate that decides which
windows Loadstone will move.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 12: Win32Window placement, liveness, and the focused window

Spec sections 5.2 and 5.3. Mac reference: `AXWindow.swift`. `Win32Window` is the only type that knows about invisible borders and DPI. The visible frame is the DWM extended frame bounds; a write expands the target by the border insets, then writes once more if the frame is off, which is what lands a window that resized itself after moving to a monitor with a different DPI.

**Files:**
- Create: `src/Loadstone.UI/Windows/Win32Window.cs`, `src/Loadstone.UI/Windows/WindowLiveness.cs`, `src/Loadstone.UI/Windows/FocusedWindow.cs`
- Test: `tests/Loadstone.UI.Tests/Windows/TestWindow.cs`, `tests/Loadstone.UI.Tests/Windows/Win32WindowTests.cs`, `tests/Loadstone.UI.Tests/Windows/FocusedWindowTests.cs`

**Interfaces:**
- Consumes: `IMovableWindow`, `WindowIdentity`, `CommandOutcome` (Task 7); `Rect`, `Layout` (Tasks 2 and 3); interop and `WindowStyles`, `Displays.ToRect` (Task 11).
- Produces: `internal sealed class Win32Window(nint hwnd) : IMovableWindow` with `nint Hwnd`, `static Rect? VisibleFrame(nint hwnd)`, `static int ProcessIdOf(nint hwnd)`, `static (int Left, int Top, int Right, int Bottom) Insets(Rect outer, Rect visible)`; `internal static class WindowLiveness { static bool IsAlive(WindowIdentity id); }`; `internal static class FocusedWindow { static nint? Resolve(); static nint? Resolve(int ownPid); }`; test helper `TestWindow` that shows a real WPF window and exposes its handle.

- [ ] **Step 1: Write the test window helper and the failing tests**

`tests/Loadstone.UI.Tests/Windows/TestWindow.cs`:

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Loadstone.UI.Tests.Windows;

/// <summary>A real, visible WPF window owned by the test, for driving Win32 code against. Use under [WpfFact].</summary>
internal sealed class TestWindow : IDisposable
{
    public TestWindow(bool resizable = true, WindowState state = WindowState.Normal)
    {
        Window = new Window
        {
            Title = "Loadstone test window",
            Width = 500,
            Height = 400,
            Left = 120,
            Top = 120,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        Window.Show();
        Window.WindowState = state;
        Handle = new WindowInteropHelper(Window).Handle;
        Pump();
    }

    public Window Window { get; }
    public nint Handle { get; }

    /// <summary>Lets WPF and the window manager process pending messages, so a SetWindowPos has taken effect.</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        Window.Close();
        Pump();
    }
}
```

`tests/Loadstone.UI.Tests/Windows/Win32WindowTests.cs`:

```csharp
using System.Windows;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class Win32WindowTests
{
    [WpfFact]
    public void VisibleFrame_ExcludesTheInvisibleResizeBorders()
    {
        using var test = new TestWindow();
        var window = new Win32Window(test.Handle);

        var visible = window.Frame.ShouldNotBeNull();
        Loadstone.UI.Interop.NativeMethods.User32.GetWindowRect(test.Handle, out var outerRect).ShouldBeTrue();
        var outer = Displays.ToRect(outerRect);

        visible.Width.ShouldBeLessThanOrEqualTo(outer.Width);
        visible.Height.ShouldBeLessThanOrEqualTo(outer.Height);
        var insets = Win32Window.Insets(outer, visible);
        insets.Left.ShouldBeGreaterThanOrEqualTo(0);
        insets.Top.ShouldBeGreaterThanOrEqualTo(0);
        insets.Right.ShouldBeGreaterThanOrEqualTo(0);
        insets.Bottom.ShouldBeGreaterThanOrEqualTo(0);
    }

    [WpfFact]
    public void SetFrame_LandsTheVisibleFrameOnTheTarget()
    {
        using var test = new TestWindow();
        var window = new Win32Window(test.Handle);
        var target = new Rect(200, 150, 601, 451);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var landed = window.Frame.ShouldNotBeNull();
        Math.Abs(landed.X - target.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Y - target.Y).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Width - target.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Height - target.Height).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_FixedSizeWindow_KeepsItsSizeAndCentersInTheTarget()
    {
        using var test = new TestWindow(resizable: false);
        var window = new Win32Window(test.Handle);
        var before = window.Frame.ShouldNotBeNull();
        var target = new Rect(100, 100, 1001, 801);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var after = window.Frame.ShouldNotBeNull();
        after.Width.ShouldBe(before.Width);
        after.Height.ShouldBe(before.Height);
        var expected = Layout.Centered(before, target);
        Math.Abs(after.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - expected.Y).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_MaximizedWindow_IsRestoredFirst()
    {
        using var test = new TestWindow(state: WindowState.Maximized);
        var window = new Win32Window(test.Handle);

        window.SetFrame(new Rect(200, 150, 601, 451)).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        Loadstone.UI.Interop.NativeMethods.User32.IsZoomed(test.Handle).ShouldBeFalse();
        window.Frame.ShouldNotBeNull().Width.ShouldBeLessThan(700);
    }

    [WpfFact]
    public void DestroyedWindow_IsUnreadable()
    {
        var test = new TestWindow();
        var window = new Win32Window(test.Handle);
        test.Dispose();

        window.Frame.ShouldBeNull();
        window.SetFrame(new Rect(0, 0, 100, 100)).ShouldBe(CommandOutcome.FrameUnreadable);
    }

    [WpfFact]
    public void Identity_CarriesTheOwningProcess()
    {
        using var test = new TestWindow();
        var window = new Win32Window(test.Handle);
        window.Identity.ShouldBe(new Loadstone.Core.Windows.WindowIdentity(test.Handle, Environment.ProcessId));
    }

    [WpfFact]
    public void WindowLiveness_IsAlive_TracksTheWindow()
    {
        var test = new TestWindow();
        var identity = new Win32Window(test.Handle).Identity;
        WindowLiveness.IsAlive(identity).ShouldBeTrue();
        WindowLiveness.IsAlive(identity with { Pid = identity.Pid + 1 }).ShouldBeFalse();
        test.Dispose();
        WindowLiveness.IsAlive(identity).ShouldBeFalse();
    }
}
```

`tests/Loadstone.UI.Tests/Windows/FocusedWindowTests.cs`:

```csharp
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class FocusedWindowTests
{
    [WpfFact]
    public void Snapshot_OfARealWindow_IsManageableForAnotherProcess_ButNotForItsOwn()
    {
        using var test = new TestWindow();
        var snapshot = WindowStyles.Snapshot(test.Handle);

        snapshot.IsWindow.ShouldBeTrue();
        snapshot.IsVisible.ShouldBeTrue();
        snapshot.Pid.ShouldBe(Environment.ProcessId);
        WindowStyles.IsManageable(snapshot, ownPid: -1).ShouldBeTrue();
        WindowStyles.IsManageable(snapshot, ownPid: Environment.ProcessId).ShouldBeFalse();
    }

    [Fact]
    public void Resolve_NeverReturnsAWindowOfTheOwnProcess()
    {
        // Whatever is in the foreground while the tests run, it is not this test host.
        var resolved = FocusedWindow.Resolve();
        if (resolved is { } hwnd)
        {
            Win32Window.ProcessIdOf(hwnd).ShouldNotBe(Environment.ProcessId);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'Win32Window' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.UI/Windows/Win32Window.cs`:

```csharp
using System.Runtime.InteropServices;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>
/// A top-level window addressed by handle. This is the only type that knows about invisible borders
/// and DPI: everything it exposes is the *visible* frame, the rectangle the user sees.
/// </summary>
internal sealed class Win32Window : IMovableWindow
{
    public Win32Window(nint hwnd)
    {
        Hwnd = hwnd;
        Identity = new WindowIdentity(hwnd, ProcessIdOf(hwnd));
    }

    public nint Hwnd { get; }

    public WindowIdentity Identity { get; }

    public Rect? Frame => VisibleFrame(Hwnd);

    /// <summary>
    /// The DWM extended frame bounds. GetWindowRect includes the invisible resize borders (about 7 px
    /// per side at 100 percent), which is why tiling with it leaves gaps. Falls back to GetWindowRect
    /// if DWM refuses; null when the handle is no longer a window.
    /// </summary>
    public static Rect? VisibleFrame(nint hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return null;
        }

        if (Dwmapi.DwmGetWindowAttributeRect(hwnd, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out var extended, (uint)Marshal.SizeOf<RECT>()) == 0)
        {
            return Displays.ToRect(extended);
        }

        return User32.GetWindowRect(hwnd, out var outer) ? Displays.ToRect(outer) : null;
    }

    public CommandOutcome SetFrame(Rect target)
    {
        if (!User32.IsWindow(Hwnd))
        {
            return CommandOutcome.FrameUnreadable;
        }

        // SetWindowPos misbehaves on minimized and maximized windows: bring them back to normal first.
        if (User32.IsIconic(Hwnd) || User32.IsZoomed(Hwnd))
        {
            User32.ShowWindow(Hwnd, NativeConstants.SW_RESTORE);
        }

        if (Frame is not { } visible)
        {
            return CommandOutcome.FrameUnreadable;
        }

        // A window without a sizing frame is fixed-size: keep its size and centre it in the tile.
        var fixedSize = (User32.GetWindowLongPtr(Hwnd, NativeConstants.GWL_STYLE) & NativeConstants.WS_THICKFRAME) == 0;
        var wanted = fixedSize ? Layout.Centered(visible, target) : target;

        var first = Write(wanted);
        if (!first.IsMoved)
        {
            return first;
        }

        // The DPI dance: a per-monitor-aware window that moved to a monitor with a different scale
        // resizes itself, so read back and write once more now that it lives there. Insets are
        // recomputed because the border thickness changed with the DPI.
        if (Frame is { } landed && landed != wanted)
        {
            var second = Write(wanted);
            if (!second.IsMoved)
            {
                return second;
            }
        }

        return CommandOutcome.Moved;
    }

    /// <summary>Expands the visible target by the current border insets and writes it. Moved even if the app clamped the size; only a refused write is an error.</summary>
    private CommandOutcome Write(Rect visibleTarget)
    {
        if (Frame is not { } visible || !User32.GetWindowRect(Hwnd, out var outerRect))
        {
            return CommandOutcome.FrameUnreadable;
        }

        var insets = Insets(Displays.ToRect(outerRect), visible);
        var x = visibleTarget.X - insets.Left;
        var y = visibleTarget.Y - insets.Top;
        var width = visibleTarget.Width + insets.Left + insets.Right;
        var height = visibleTarget.Height + insets.Top + insets.Bottom;

        if (!User32.SetWindowPos(Hwnd, 0, x, y, width, height, NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE))
        {
            return CommandOutcome.Rejected(Marshal.GetLastWin32Error());
        }

        return CommandOutcome.Moved;
    }

    /// <summary>How far the outer rectangle extends past the visible frame on each side.</summary>
    public static (int Left, int Top, int Right, int Bottom) Insets(Rect outer, Rect visible) =>
        (visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom);

    public static int ProcessIdOf(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }
}
```

`src/Loadstone.UI/Windows/WindowLiveness.cs`:

```csharp
using Loadstone.Core.Windows;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class WindowLiveness
{
    /// <summary>True while the handle is still a window owned by the same process. Windows reuses handles, hence the process check.</summary>
    public static bool IsAlive(WindowIdentity identity) =>
        User32.IsWindow(identity.Hwnd) && Win32Window.ProcessIdOf(identity.Hwnd) == identity.Pid;
}
```

`src/Loadstone.UI/Windows/FocusedWindow.cs`:

```csharp
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class FocusedWindow
{
    /// <summary>The root of the foreground window if Loadstone may manage it, else null.</summary>
    public static nint? Resolve() => Resolve(Environment.ProcessId);

    public static nint? Resolve(int ownPid)
    {
        var foreground = User32.GetForegroundWindow();
        if (foreground == 0)
        {
            return null;
        }

        var root = User32.GetAncestor(foreground, NativeConstants.GA_ROOT);
        if (root == 0)
        {
            root = foreground;
        }

        return WindowStyles.IsManageable(WindowStyles.Snapshot(root), ownPid) ? root : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass. If `SetFrame_LandsTheVisibleFrameOnTheTarget` is off by more than 1 px on a display above 100 percent, add a second `TestWindow.Pump()` before reading the frame: WPF applies its own DPI-change layout asynchronously.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Windows tests/Loadstone.UI.Tests/Windows
git commit -F - <<'EOF'
Add Win32Window placement with the place-twice DPI sequence

The visible frame is the DWM extended frame bounds; writes expand the
target by the invisible border insets, restore maximized or minimized
windows first, keep fixed-size windows at their size, and write once
more when the frame lands off target after a DPI change. Real-window
tests run under WpfFact.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---
### Task 13: Command dispatcher

Spec section 5.5. The counterpart of the Mac director's no-argument `perform(_:)`: resolves the focused window (or wraps one captured by the tray), runs the director, and reports the outcome with a beep and a log line. `Moved` is silent.

**Files:**
- Create: `src/Loadstone.UI/Windows/ICommandDispatcher.cs`, `src/Loadstone.UI/Windows/CommandDispatcher.cs`
- Test: `tests/Loadstone.UI.Tests/Windows/CommandDispatcherTests.cs`

**Interfaces:**
- Consumes: `WindowDirector`, `CommandOutcome`, `OutcomeKind` (Task 7); `WindowCommand`, `WindowCommands.Id` (Task 6); `Win32Window`, `FocusedWindow`, `WindowLiveness`, `Displays` (Tasks 11 and 12).
- Produces: `internal interface ICommandDispatcher { void Run(WindowCommand command); void Run(WindowCommand command, nint hwnd); }`; `internal sealed class CommandDispatcher : ICommandDispatcher` with public constructor `(WindowDirector director, ILogger<CommandDispatcher> logger)` and internal constructor `(WindowDirector director, ILogger<CommandDispatcher> logger, Func<nint?> focusedWindow, Action beep)`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/Windows/CommandDispatcherTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class CommandDispatcherTests
{
    private int _beeps;
    private nint? _focused;

    private CommandDispatcher NewDispatcher() => new(
        new WindowDirector(Displays.Snapshot, WindowLiveness.IsAlive),
        NullLogger<CommandDispatcher>.Instance,
        () => _focused,
        () => _beeps++);

    [WpfFact]
    public void Run_WithHandle_MovesTheWindow_Silently()
    {
        using var test = new TestWindow();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.LeftHalf, test.Handle);
        TestWindow.Pump();

        var displays = Displays.Snapshot();
        var frame = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var display = ScreenGeometry.DisplayContaining(frame, displays).ShouldNotBeNull();
        var expected = Tile.LeftHalf.Frame(display.WorkArea);
        Math.Abs(frame.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(frame.Width - expected.Width).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    [WpfFact]
    public void Run_Restore_BeepsWhenThereIsNothingToRestore()
    {
        using var test = new TestWindow();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.Restore, test.Handle);
        _beeps.ShouldBe(1);
    }

    [WpfFact]
    public void Run_RestoreAfterATile_ReturnsTheWindowSilently()
    {
        using var test = new TestWindow();
        var before = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.TopRight, test.Handle);
        TestWindow.Pump();
        dispatcher.Run(WindowCommand.Restore, test.Handle);
        TestWindow.Pump();

        var after = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        Math.Abs(after.X - before.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - before.Y).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    [Fact]
    public void Run_WithoutAFocusedWindow_BeepsAndDoesNotThrow()
    {
        _focused = null;
        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf));
        _beeps.ShouldBe(1);
    }

    [WpfFact]
    public void Run_UsesTheFocusedWindowResolver()
    {
        using var test = new TestWindow();
        _focused = test.Handle;
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.Maximize);
        TestWindow.Pump();

        var frame = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var display = ScreenGeometry.DisplayContaining(frame, Displays.Snapshot()).ShouldNotBeNull();
        Math.Abs(frame.Width - display.WorkArea.Width).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    [Fact]
    public void Run_DestroyedHandle_BeepsAndDoesNotThrow()
    {
        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf, hwnd: 0x1234));
        _beeps.ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'CommandDispatcher' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.UI/Windows/ICommandDispatcher.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Windows;

/// <summary>Runs a command on the focused window, or on a window captured earlier, and tells the user when it can't.</summary>
internal interface ICommandDispatcher
{
    void Run(WindowCommand command);

    void Run(WindowCommand command, nint hwnd);
}
```

`src/Loadstone.UI/Windows/CommandDispatcher.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly WindowDirector _director;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly Func<nint?> _focusedWindow;
    private readonly Action _beep;

    public CommandDispatcher(WindowDirector director, ILogger<CommandDispatcher> logger)
        : this(director, logger, FocusedWindow.Resolve, () => User32.MessageBeep(NativeConstants.MB_OK))
    {
    }

    internal CommandDispatcher(WindowDirector director, ILogger<CommandDispatcher> logger, Func<nint?> focusedWindow, Action beep)
    {
        _director = director;
        _logger = logger;
        _focusedWindow = focusedWindow;
        _beep = beep;
    }

    /// <summary>Runs <paramref name="command"/> on the foreground window.</summary>
    public void Run(WindowCommand command)
    {
        if (_focusedWindow() is not { } hwnd)
        {
            _logger.LogInformation("{Command} skipped: no manageable foreground window", command.Id());
            _beep();
            return;
        }

        Run(command, hwnd);
    }

    /// <summary>Runs <paramref name="command"/> on <paramref name="hwnd"/>, which the caller resolved earlier (the tray menu path).</summary>
    public void Run(WindowCommand command, nint hwnd)
    {
        var window = new Win32Window(hwnd);
        var outcome = _director.Perform(command, window);
        Report(command, window, outcome);
    }

    private void Report(WindowCommand command, Win32Window window, CommandOutcome outcome)
    {
        var id = command.Id();
        var pid = window.Identity.Pid;
        switch (outcome.Kind)
        {
            case OutcomeKind.Moved:
                _logger.LogDebug("{Command}: moved window {Hwnd} of pid {Pid} to {Frame}", id, window.Hwnd, pid, window.Frame);
                return;
            case OutcomeKind.Rejected:
                _logger.LogError("{Command}: window {Hwnd} of pid {Pid} rejected the frame (Win32 error {Error})", id, window.Hwnd, pid, outcome.Win32Error);
                break;
            case OutcomeKind.FrameUnreadable:
                _logger.LogError("{Command}: could not read the frame of window {Hwnd} of pid {Pid}", id, window.Hwnd, pid);
                break;
            case OutcomeKind.NoDisplay:
                _logger.LogError("{Command}: no display attached", id);
                break;
            case OutcomeKind.NoOtherDisplay:
                _logger.LogInformation("{Command}: only one display attached", id);
                break;
            case OutcomeKind.NothingToRestore:
                _logger.LogInformation("{Command}: nothing remembered for window {Hwnd} of pid {Pid}", id, window.Hwnd, pid);
                break;
        }

        _beep();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Windows tests/Loadstone.UI.Tests/Windows/CommandDispatcherTests.cs
git commit -F - <<'EOF'
Add the command dispatcher

Resolves the foreground window or wraps a captured handle, runs the
director, and reports every outcome but Moved with a beep and a log line.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 14: Hotkey registrar and keyboard layout probe

Spec section 6.1, the Win32 half. `HotkeyRegistrar` owns a message-only window and one `RegisterHotKey` per id; a chord another application owns fails with `ERROR_HOTKEY_ALREADY_REGISTERED`, which becomes the `InUse` status. `KeyboardLayoutProbe` asks the active layout whether a Ctrl+Alt chord types a character (AltGr).

**Files:**
- Create: `src/Loadstone.UI/Input/HotkeyStatus.cs`, `src/Loadstone.UI/Input/IHotkeyRegistrar.cs`, `src/Loadstone.UI/Input/HotkeyRegistrar.cs`, `src/Loadstone.UI/Input/IKeyboardLayoutProbe.cs`, `src/Loadstone.UI/Input/KeyboardLayoutProbe.cs`
- Test: `tests/Loadstone.UI.Tests/Input/HotkeyRegistrarTests.cs`, `tests/Loadstone.UI.Tests/Input/KeyboardLayoutProbeTests.cs`

**Interfaces:**
- Consumes: `Shortcut`, `KeyModifiers`, `VirtualKeys` (Task 5); interop (Task 11).
- Produces: `internal enum HotkeyState { Registered, Unbound, InUse, Failed }`; `internal readonly record struct HotkeyStatus(HotkeyState State, int Win32Error = 0)` with static `Registered`, `Unbound`, `InUse(int)`, `Failed(int)`; `internal interface IHotkeyRegistrar : IDisposable { HotkeyStatus Register(int id, Shortcut shortcut); void Unregister(int id); event Action<int>? HotkeyPressed; }`; `internal sealed class HotkeyRegistrar : IHotkeyRegistrar` (create on the UI thread); `internal interface IKeyboardLayoutProbe { string? CharacterTypedBy(Shortcut shortcut); }`; `internal sealed class KeyboardLayoutProbe : IKeyboardLayoutProbe`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/Input/HotkeyRegistrarTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class HotkeyRegistrarTests
{
    // A chord no application is likely to own.
    private static readonly Shortcut Obscure = new(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, VirtualKeys.F1 + 22);

    [WpfFact]
    public void Register_FreeChord_IsRegistered()
    {
        using var registrar = new HotkeyRegistrar();
        registrar.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_ChordOwnedByAnotherRegistration_IsInUse()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);

        var status = second.Register(1, Obscure);
        status.State.ShouldBe(HotkeyState.InUse);
        status.Win32Error.ShouldBe(1409);
    }

    [WpfFact]
    public void Unregister_FreesTheChord()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(1, Obscure);
        first.Unregister(1);
        second.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_SameIdAgain_ReplacesTheChord()
    {
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        registrar.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
        registrar.Register(1, Obscure with { VirtualKey = VirtualKeys.F1 + 21 }).ShouldBe(HotkeyStatus.Registered);
        other.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Dispose_FreesEveryChord()
    {
        var registrar = new HotkeyRegistrar();
        registrar.Register(1, Obscure);
        registrar.Dispose();
        using var other = new HotkeyRegistrar();
        other.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }
}
```

`tests/Loadstone.UI.Tests/Input/KeyboardLayoutProbeTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class KeyboardLayoutProbeTests
{
    private readonly KeyboardLayoutProbe _probe = new();

    [Fact]
    public void CharacterTypedBy_ArrowKeys_IsNull()
    {
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_WithoutBothCtrlAndAlt_IsNull()
    {
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Alt, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, 'E')).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_Letter_IsNullOrAPrintableCharacter()
    {
        // Layout dependent: "€" on Norwegian and German layouts, null on US. Never a control character.
        var result = _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, 'E'));
        if (result is not null)
        {
            result.ShouldNotBeEmpty();
            result.All(c => !char.IsControl(c)).ShouldBeTrue();
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'HotkeyRegistrar' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.UI/Input/HotkeyStatus.cs`:

```csharp
namespace Loadstone.UI.Input;

internal enum HotkeyState
{
    Registered,
    /// <summary>No chord: the user cleared it, or the stored text did not parse.</summary>
    Unbound,
    /// <summary>Another application already owns the chord.</summary>
    InUse,
    /// <summary>RegisterHotKey failed for another reason; Win32Error says which.</summary>
    Failed,
}

internal readonly record struct HotkeyStatus(HotkeyState State, int Win32Error = 0)
{
    public static HotkeyStatus Registered => new(HotkeyState.Registered);
    public static HotkeyStatus Unbound => new(HotkeyState.Unbound);
    public static HotkeyStatus InUse(int win32Error) => new(HotkeyState.InUse, win32Error);
    public static HotkeyStatus Failed(int win32Error) => new(HotkeyState.Failed, win32Error);
}
```

`src/Loadstone.UI/Input/IHotkeyRegistrar.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Input;

/// <summary>One global hotkey per id. The real one wraps RegisterHotKey; tests substitute it.</summary>
internal interface IHotkeyRegistrar : IDisposable
{
    HotkeyStatus Register(int id, Shortcut shortcut);

    void Unregister(int id);

    /// <summary>Raised on the UI thread with the id whose chord was pressed.</summary>
    event Action<int>? HotkeyPressed;
}
```

`src/Loadstone.UI/Input/HotkeyRegistrar.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Loadstone.Core.Commands;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// Registers global hotkeys on a message-only window and raises <see cref="HotkeyPressed"/> from its
/// WM_HOTKEY messages. Windows fires on key down; MOD_NOREPEAT stops auto-repeat while the chord is
/// held. Create and use on the UI thread.
/// </summary>
internal sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = [];

    public HotkeyRegistrar()
    {
        var parameters = new HwndSourceParameters("LoadstoneHotkeys")
        {
            ParentWindow = NativeConstants.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public nint Handle => _source.Handle;

    public event Action<int>? HotkeyPressed;

    public HotkeyStatus Register(int id, Shortcut shortcut)
    {
        Unregister(id);
        if (User32.RegisterHotKey(Handle, id, (uint)shortcut.Modifiers | NativeConstants.MOD_NOREPEAT, (uint)shortcut.VirtualKey))
        {
            _registered.Add(id);
            return HotkeyStatus.Registered;
        }

        var error = Marshal.GetLastWin32Error();
        return error == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyStatus.InUse(error) : HotkeyStatus.Failed(error);
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            User32.UnregisterHotKey(Handle, id);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeConstants.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((int)wParam);
            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered)
        {
            User32.UnregisterHotKey(Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
```

`src/Loadstone.UI/Input/IKeyboardLayoutProbe.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Input;

internal interface IKeyboardLayoutProbe
{
    /// <summary>The character the chord would type on the active keyboard layout (AltGr), or null when it types nothing.</summary>
    string? CharacterTypedBy(Shortcut shortcut);
}
```

`src/Loadstone.UI/Input/KeyboardLayoutProbe.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// On Norwegian and most European layouts AltGr is Ctrl+Alt as far as Windows is concerned, so a
/// global Ctrl+Alt+E hotkey swallows the € key. This asks the active layout what a Ctrl+Alt chord
/// would type so Settings can warn about it.
/// </summary>
internal sealed class KeyboardLayoutProbe : IKeyboardLayoutProbe
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RMENU = 0xA5;

    public string? CharacterTypedBy(Shortcut shortcut)
    {
        var modifiers = shortcut.Modifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control) || !modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Win))
        {
            return null;
        }

        var layout = User32.GetKeyboardLayout(ForegroundThreadId());
        var state = new byte[256];
        state[VK_CONTROL] = 0x80;
        state[VK_LCONTROL] = 0x80;
        state[VK_MENU] = 0x80;
        state[VK_RMENU] = 0x80;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            state[VK_SHIFT] = 0x80;
            state[VK_LSHIFT] = 0x80;
        }

        var scanCode = User32.MapVirtualKeyEx((uint)shortcut.VirtualKey, NativeConstants.MAPVK_VK_TO_VSC, layout);
        var buffer = new char[8];
        var count = User32.ToUnicodeEx((uint)shortcut.VirtualKey, scanCode, state, buffer, buffer.Length, NativeConstants.TOUNICODE_NO_KEYBOARD_STATE_CHANGE, layout);
        if (count <= 0)
        {
            return null;
        }

        var text = new string(buffer, 0, count);
        return text.Length == 0 || text.Any(char.IsControl) || string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static uint ForegroundThreadId()
    {
        var foreground = User32.GetForegroundWindow();
        return foreground == 0 ? 0 : User32.GetWindowThreadProcessId(foreground, out _);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass. If `HwndSourceParameters.ParentWindow` rejects `-3`, the property expects an `IntPtr`; `NativeConstants.HWND_MESSAGE` is already `nint`, which is the same type.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Input tests/Loadstone.UI.Tests/Input
git commit -F - <<'EOF'
Add the hotkey registrar and the keyboard layout probe

RegisterHotKey on a message-only window, with a chord another app owns
reported as InUse instead of swallowed. The probe asks the active layout
whether a Ctrl+Alt chord types a character, which AltGr layouts do.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 15: Hotkey center

Spec section 6.1, the policy half. Mac reference: `HotkeyCenter.swift`. Resolves each command's effective shortcut (custom, else default), registers all of them, logs the effective binding and status per command at startup, re-registers a single command when its shortcut changes, and suspends registrations while a recorder is recording.

**Files:**
- Create: `src/Loadstone.UI/Input/HotkeyCenter.cs`
- Test: `tests/Loadstone.UI.Tests/Input/HotkeyCenterTests.cs`

**Interfaces:**
- Consumes: `IHotkeyRegistrar`, `HotkeyStatus`, `HotkeyState`, `IKeyboardLayoutProbe` (Task 14); `ICommandDispatcher` (Task 13); `ISettingsService` (Task 9); `WindowCommand`, `WindowCommands`, `Shortcut` (Tasks 5 and 6); `InMemorySettings` from the Core tests is duplicated into the UI tests (below) because test projects do not reference each other.
- Produces: `internal sealed class HotkeyCenter(IHotkeyRegistrar registrar, ISettingsService settings, ICommandDispatcher dispatcher, IKeyboardLayoutProbe probe, ILogger<HotkeyCenter> logger) : IDisposable` with `void Start()`, `Shortcut? EffectiveShortcut(WindowCommand)`, `bool IsCustomized(WindowCommand)`, `HotkeyStatus StatusOf(WindowCommand)`, `string? LayoutWarning(WindowCommand)`, `void SetShortcut(WindowCommand, Shortcut?)`, `void ResetAll()`, `void Suspend()`, `void Resume()`, `event EventHandler? BindingsChanged`.

- [ ] **Step 1: Write the UI-side in-memory settings and the failing tests**

`tests/Loadstone.UI.Tests/InMemorySettings.cs`:

```csharp
using Loadstone.Core.Settings;

namespace Loadstone.UI.Tests;

public sealed class InMemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

`tests/Loadstone.UI.Tests/Input/HotkeyCenterTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class HotkeyCenterTests
{
    private readonly IHotkeyRegistrar _registrar = Substitute.For<IHotkeyRegistrar>();
    private readonly ICommandDispatcher _dispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IKeyboardLayoutProbe _probe = Substitute.For<IKeyboardLayoutProbe>();
    private readonly InMemorySettings _settings = new();

    public HotkeyCenterTests()
    {
        _registrar.Register(Arg.Any<int>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        _probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
    }

    private HotkeyCenter NewCenter() => new(_registrar, _settings, _dispatcher, _probe, NullLogger<HotkeyCenter>.Instance);

    [Fact]
    public void Start_RegistersEveryCommandWithItsDefault()
    {
        NewCenter().Start();
        foreach (var command in WindowCommands.All)
        {
            _registrar.Received(1).Register(Arg.Any<int>(), command.DefaultShortcut());
        }

        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void Start_Twice_RegistersOnce()
    {
        var center = NewCenter();
        center.Start();
        center.Start();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void EffectiveShortcut_PrefersACustomization_AndTreatsEmptyAsUnbound()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();

        center.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(Shortcut.Parse("Ctrl+Alt+Shift+Left"));
        center.EffectiveShortcut(WindowCommand.Center).ShouldBeNull();
        center.EffectiveShortcut(WindowCommand.RightHalf).ShouldBe(WindowCommand.RightHalf.DefaultShortcut());
        center.IsCustomized(WindowCommand.LeftHalf).ShouldBeTrue();
        center.IsCustomized(WindowCommand.RightHalf).ShouldBeFalse();
    }

    [Fact]
    public void Start_UnboundCommand_IsUnregisteredAndReportedUnbound()
    {
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();
        center.Start();

        center.StatusOf(WindowCommand.Center).ShouldBe(HotkeyStatus.Unbound);
        _registrar.DidNotReceive().Register(Arg.Any<int>(), WindowCommand.Center.DefaultShortcut());
    }

    [Fact]
    public void Start_RecordsInUseStatus()
    {
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopLeft.DefaultShortcut()).Returns(HotkeyStatus.InUse(1409));
        var center = NewCenter();
        center.Start();
        center.StatusOf(WindowCommand.TopLeft).State.ShouldBe(HotkeyState.InUse);
        center.StatusOf(WindowCommand.TopRight).State.ShouldBe(HotkeyState.Registered);
    }

    [Fact]
    public void Start_RecordsLayoutWarnings()
    {
        _probe.CharacterTypedBy(WindowCommand.LeftTwoThirds.DefaultShortcut()).Returns("€");
        var center = NewCenter();
        center.Start();
        center.LayoutWarning(WindowCommand.LeftTwoThirds).ShouldBe("€");
        center.LayoutWarning(WindowCommand.LeftHalf).ShouldBeNull();
    }

    [Fact]
    public void SetShortcut_PersistsAndReregistersThatCommand()
    {
        var center = NewCenter();
        center.Start();
        _registrar.ClearReceivedCalls();
        var raised = 0;
        center.BindingsChanged += (_, _) => raised++;

        var custom = Shortcut.Parse("Ctrl+Alt+Shift+Left");
        center.SetShortcut(WindowCommand.LeftHalf, custom);

        _settings.Current.Shortcuts["leftHalf"].ShouldBe("Ctrl+Alt+Shift+Left");
        _registrar.Received(1).Register(Arg.Any<int>(), custom);
        center.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(custom);
        raised.ShouldBe(1);
    }

    [Fact]
    public void SetShortcut_Null_ClearsTheBinding()
    {
        var center = NewCenter();
        center.Start();
        center.SetShortcut(WindowCommand.LeftHalf, null);
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("");
        center.StatusOf(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);
    }

    [Fact]
    public void SetShortcut_BackToTheDefault_RemovesTheCustomization()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        var center = NewCenter();
        center.Start();
        center.SetShortcut(WindowCommand.LeftHalf, WindowCommand.LeftHalf.DefaultShortcut());
        _settings.Current.Shortcuts.ContainsKey("leftHalf").ShouldBeFalse();
    }

    [Fact]
    public void ResetAll_ClearsEveryCustomization()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();
        center.Start();
        center.ResetAll();
        _settings.Current.Shortcuts.ShouldBeEmpty();
        center.StatusOf(WindowCommand.Center).ShouldBe(HotkeyStatus.Registered);
    }

    [Fact]
    public void Suspend_UnregistersAll_AndResume_Reregisters()
    {
        var center = NewCenter();
        center.Start();
        _registrar.ClearReceivedCalls();

        center.Suspend();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Unregister)).ShouldBe(WindowCommands.All.Count);
        _registrar.ClearReceivedCalls();

        center.Resume();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void HotkeyPressed_RunsTheCommandWithThatId()
    {
        var center = NewCenter();
        center.Start();
        var leftHalfId = (int)_registrar.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register) && Equals(c.GetArguments()[1], WindowCommand.LeftHalf.DefaultShortcut()))
            .GetArguments()[0]!;

        _registrar.HotkeyPressed += Raise.Event<Action<int>>(leftHalfId);

        _dispatcher.Received(1).Run(WindowCommand.LeftHalf);
    }

    [Fact]
    public void HotkeyPressed_WhileSuspended_IsIgnored()
    {
        var center = NewCenter();
        center.Start();
        center.Suspend();
        _registrar.HotkeyPressed += Raise.Event<Action<int>>(1);
        _dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'HotkeyCenter' could not be found".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.UI/Input/HotkeyCenter.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Settings;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.Input;

/// <summary>
/// Owns one global hotkey per <see cref="WindowCommand"/>: resolves the effective chord (custom,
/// else default), registers it, records the registration status and any keyboard-layout warning
/// for Settings, and routes presses to the dispatcher.
/// </summary>
internal sealed class HotkeyCenter : IDisposable
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly ISettingsService _settings;
    private readonly ICommandDispatcher _dispatcher;
    private readonly IKeyboardLayoutProbe _probe;
    private readonly ILogger<HotkeyCenter> _logger;
    private readonly Dictionary<WindowCommand, HotkeyStatus> _status = new();
    private readonly Dictionary<WindowCommand, string?> _layoutWarnings = new();
    private bool _started;
    private bool _suspended;

    public HotkeyCenter(
        IHotkeyRegistrar registrar,
        ISettingsService settings,
        ICommandDispatcher dispatcher,
        IKeyboardLayoutProbe probe,
        ILogger<HotkeyCenter> logger)
    {
        _registrar = registrar;
        _settings = settings;
        _dispatcher = dispatcher;
        _probe = probe;
        _logger = logger;
        _registrar.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>Raised after any binding changes, so menus and settings rows can refresh.</summary>
    public event EventHandler? BindingsChanged;

    /// <summary>Registers every command. Safe to call more than once.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RegisterAll();
        foreach (var command in WindowCommands.All)
        {
            _logger.LogInformation("{Command}: {Shortcut} ({State})", command.Id(), EffectiveShortcut(command)?.ToString() ?? "unbound", StatusOf(command).State);
        }
    }

    /// <summary>The custom chord if the user set one, null if they cleared it, else the default.</summary>
    public Shortcut? EffectiveShortcut(WindowCommand command)
    {
        if (_settings.Current.Shortcuts.TryGetValue(command.Id(), out var text))
        {
            return Shortcut.TryParse(text, out var custom) ? custom : null;
        }

        return command.DefaultShortcut();
    }

    public bool IsCustomized(WindowCommand command) => _settings.Current.Shortcuts.ContainsKey(command.Id());

    public HotkeyStatus StatusOf(WindowCommand command) =>
        _status.TryGetValue(command, out var status) ? status : HotkeyStatus.Unbound;

    /// <summary>The character the chord types on the active layout, or null.</summary>
    public string? LayoutWarning(WindowCommand command) => _layoutWarnings.GetValueOrDefault(command);

    /// <summary>Persists and re-registers. Null clears the binding; the default removes the customization.</summary>
    public void SetShortcut(WindowCommand command, Shortcut? shortcut)
    {
        var id = command.Id();
        _settings.Update(s =>
        {
            if (shortcut is null)
            {
                s.Shortcuts[id] = "";
            }
            else if (shortcut == command.DefaultShortcut())
            {
                s.Shortcuts.Remove(id);
            }
            else
            {
                s.Shortcuts[id] = shortcut.ToString();
            }
        });
        RegisterOne(command);
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        _settings.Update(s => s.Shortcuts.Clear());
        RegisterAll();
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases every chord while a recorder is recording, so pressing the current chord records instead of moving a window.</summary>
    public void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        foreach (var command in WindowCommands.All)
        {
            _registrar.Unregister(IdOf(command));
        }
    }

    public void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        RegisterAll();
    }

    public void Dispose()
    {
        _registrar.HotkeyPressed -= OnHotkeyPressed;
    }

    private void RegisterAll()
    {
        foreach (var command in WindowCommands.All)
        {
            RegisterOne(command);
        }
    }

    private void RegisterOne(WindowCommand command)
    {
        var id = IdOf(command);
        var shortcut = EffectiveShortcut(command);
        _layoutWarnings[command] = shortcut is null ? null : _probe.CharacterTypedBy(shortcut);

        if (_suspended)
        {
            return;
        }

        if (shortcut is null)
        {
            _registrar.Unregister(id);
            _status[command] = HotkeyStatus.Unbound;
            return;
        }

        var status = _registrar.Register(id, shortcut);
        _status[command] = status;
        if (status.State != HotkeyState.Registered)
        {
            _logger.LogWarning("{Command}: {Shortcut} not registered ({State}, Win32 error {Error})", command.Id(), shortcut, status.State, status.Win32Error);
        }

        if (_layoutWarnings[command] is { } character)
        {
            _logger.LogWarning("{Command}: {Shortcut} types '{Character}' on the active keyboard layout", command.Id(), shortcut, character);
        }
    }

    /// <summary>Hotkey ids are 1-based so that id 0, which Windows also uses for "no id", never appears.</summary>
    private static int IdOf(WindowCommand command) => (int)command + 1;

    private void OnHotkeyPressed(int id)
    {
        if (_suspended)
        {
            return;
        }

        var command = (WindowCommand)(id - 1);
        if (Enum.IsDefined(command))
        {
            _dispatcher.Run(command);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Input/HotkeyCenter.cs tests/Loadstone.UI.Tests/InMemorySettings.cs tests/Loadstone.UI.Tests/Input/HotkeyCenterTests.cs
git commit -F - <<'EOF'
Add the hotkey center

Registers the effective chord per command, records in-use and
keyboard-layout warnings for Settings, re-registers on change, suspends
while a recorder records, and routes presses to the dispatcher.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 16: Move-size events, the snap preview, and the drag monitor

Spec sections 6.2 and 6.3. Mac reference: `EdgeDragMonitor.swift`, `SnapPreview.swift`. The monitor installs a WinEvent hook for the move-loop start and end events while drag snapping is on, polls the cursor at 60 Hz during a drag, previews through `ISnapPreview`, and commits through the director after the OS has finished the drop.

**Files:**
- Create: `src/Loadstone.UI/Input/IMoveSizeEvents.cs`, `src/Loadstone.UI/Input/WinEventMoveSizeEvents.cs`, `src/Loadstone.UI/Input/ISnapPreview.cs`, `src/Loadstone.UI/Input/SnapPreview.cs`, `src/Loadstone.UI/Input/DragSnapMonitor.cs`
- Test: `tests/Loadstone.UI.Tests/Input/DragSnapMonitorTests.cs`

**Interfaces:**
- Consumes: `DragSnapTracker<IMovableWindow>`, `SnapTarget` (Task 8); `WindowDirector`, `IMovableWindow` (Task 7); `ISettingsService` (Task 9); `Displays`, `WindowStyles`, `Win32Window` (Tasks 11 and 12); interop.
- Produces: `internal interface IMoveSizeEvents { event Action<nint>? Started; event Action<nint>? Ended; bool Install(); void Uninstall(); bool IsInstalled { get; } }`; `internal sealed class WinEventMoveSizeEvents : IMoveSizeEvents`; `internal interface ISnapPreview { void Show(Rect frame); void Hide(); }`; `internal sealed class SnapPreview : Window, ISnapPreview`; `internal sealed class DragSnapMonitor : IDisposable` with public constructor `(IMoveSizeEvents events, WindowDirector director, ISettingsService settings, ISnapPreview preview, ILogger<DragSnapMonitor> logger)`, internal constructor adding `(Func<IReadOnlyList<Display>> displays, Func<Point> cursor, Func<nint, IMovableWindow?> windowFactory)`, `void Start()`, `void Stop()`, `bool IsMonitoring`, and `internal void Tick()`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/Input/DragSnapMonitorTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class DragSnapMonitorTests
{
    private sealed class FakeMoveSizeEvents : IMoveSizeEvents
    {
        public bool InstallSucceeds { get; set; } = true;
        public int Installs { get; private set; }
        public int Uninstalls { get; private set; }
        public bool IsInstalled { get; private set; }
        public event Action<nint>? Started;
        public event Action<nint>? Ended;

        public bool Install()
        {
            Installs++;
            IsInstalled = InstallSucceeds;
            return InstallSucceeds;
        }

        public void Uninstall()
        {
            Uninstalls++;
            IsInstalled = false;
        }

        public void RaiseStarted(nint hwnd) => Started?.Invoke(hwnd);
        public void RaiseEnded(nint hwnd) => Ended?.Invoke(hwnd);
    }

    private sealed class FakeWindow(nint hwnd, Rect frame) : IMovableWindow
    {
        public WindowIdentity Identity { get; } = new(hwnd, 100);
        public Rect? Frame { get; set; } = frame;
        public List<Rect> Writes { get; } = [];

        public CommandOutcome SetFrame(Rect frame)
        {
            Writes.Add(frame);
            Frame = frame;
            return CommandOutcome.Moved;
        }
    }

    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Rect Baseline = new(300, 300, 401, 301);

    private readonly FakeMoveSizeEvents _events = new();
    private readonly InMemorySettings _settings = new();
    private readonly ISnapPreview _preview = Substitute.For<ISnapPreview>();
    private readonly Dictionary<nint, FakeWindow> _windows = new();
    private Point _cursor = new(600, 450);

    private DragSnapMonitor NewMonitor() => new(
        _events,
        new WindowDirector(() => [Primary], _ => true),
        _settings,
        _preview,
        NullLogger<DragSnapMonitor>.Instance,
        () => [Primary],
        () => _cursor,
        hwnd => _windows.GetValueOrDefault(hwnd));

    private FakeWindow AddWindow(nint hwnd = 10)
    {
        var window = new FakeWindow(hwnd, Baseline);
        _windows[hwnd] = window;
        return window;
    }

    [WpfFact]
    public void Start_WithDragSnappingOn_InstallsTheHook()
    {
        using var monitor = NewMonitor();
        monitor.Start();
        _events.Installs.ShouldBe(1);
        monitor.IsMonitoring.ShouldBeTrue();
    }

    [WpfFact]
    public void Start_WithDragSnappingOff_DoesNotInstall_AndFollowsTheSettingLater()
    {
        _settings.Current.DragSnappingEnabled = false;
        using var monitor = NewMonitor();
        monitor.Start();
        monitor.IsMonitoring.ShouldBeFalse();

        _settings.Update(s => s.DragSnappingEnabled = true);
        monitor.IsMonitoring.ShouldBeTrue();

        _settings.Update(s => s.DragSnappingEnabled = false);
        monitor.IsMonitoring.ShouldBeFalse();
        _events.Uninstalls.ShouldBe(1);
        _preview.Received().Hide();
    }

    [WpfFact]
    public void Start_WhenTheHookCannotBeInstalled_IsNotMonitoring()
    {
        _events.InstallSucceeds = false;
        using var monitor = NewMonitor();
        monitor.Start();
        monitor.IsMonitoring.ShouldBeFalse();
    }

    [WpfFact]
    public void Drag_ShowsThePreviewOnceTheWindowMovesIntoAZone_AndSnapsOnEnd()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        monitor.Tick();
        _preview.DidNotReceive().Show(Arg.Any<Rect>());

        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _preview.Received(1).Show(Tile.LeftHalf.Frame(Primary.WorkArea));

        _events.RaiseEnded(10);
        _preview.Received().Hide();
        window.Writes.ShouldBe([Tile.LeftHalf.Frame(Primary.WorkArea)]);
    }

    [WpfFact]
    public void Drag_EndingBackAtTheBaseline_DoesNotSnap()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        window.Frame = Baseline;
        _events.RaiseEnded(10);

        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Drag_OfAWindowTheFactoryRejects_IsIgnored()
    {
        using var monitor = NewMonitor();
        monitor.Start();
        _events.RaiseStarted(99);
        _cursor = new Point(3, 450);
        monitor.Tick();
        _events.RaiseEnded(99);
        _preview.DidNotReceive().Show(Arg.Any<Rect>());
    }

    [WpfFact]
    public void Drag_EndEventForAnotherWindow_ResetsWithoutSnapping()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _events.RaiseEnded(11);

        window.Writes.ShouldBeEmpty();
        _events.RaiseEnded(10);
        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Stop_UninstallsAndHidesThePreview()
    {
        var monitor = NewMonitor();
        monitor.Start();
        monitor.Stop();
        _events.Uninstalls.ShouldBe(1);
        monitor.IsMonitoring.ShouldBeFalse();
        _preview.Received().Hide();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'IMoveSizeEvents' could not be found".

- [ ] **Step 3: Write the event seam and its WinEvent implementation**

`src/Loadstone.UI/Input/IMoveSizeEvents.cs`:

```csharp
namespace Loadstone.UI.Input;

/// <summary>Windows' own signal that a window entered and left its move/size loop. The real one is a WinEvent hook.</summary>
internal interface IMoveSizeEvents
{
    event Action<nint>? Started;

    event Action<nint>? Ended;

    /// <summary>Installs the hook. False when the system refused it.</summary>
    bool Install();

    void Uninstall();

    bool IsInstalled { get; }
}
```

`src/Loadstone.UI/Input/WinEventMoveSizeEvents.cs`:

```csharp
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// One SetWinEventHook covering EVENT_SYSTEM_MOVESIZESTART through EVENT_SYSTEM_MOVESIZEEND,
/// out-of-context so callbacks arrive through this thread's message loop. Install from the UI
/// thread. The delegate is kept in a field for the life of the hook so the GC cannot collect it.
/// </summary>
internal sealed class WinEventMoveSizeEvents : IMoveSizeEvents
{
    private User32.WinEventProc? _callback;
    private nint _hook;

    public event Action<nint>? Started;

    public event Action<nint>? Ended;

    public bool IsInstalled => _hook != 0;

    public bool Install()
    {
        if (IsInstalled)
        {
            return true;
        }

        _callback = OnEvent;
        _hook = User32.SetWinEventHook(
            NativeConstants.EVENT_SYSTEM_MOVESIZESTART,
            NativeConstants.EVENT_SYSTEM_MOVESIZEEND,
            0,
            _callback,
            0,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0)
        {
            _callback = null;
            return false;
        }

        return true;
    }

    public void Uninstall()
    {
        if (!IsInstalled)
        {
            return;
        }

        User32.UnhookWinEvent(_hook);
        _hook = 0;
        _callback = null;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeConstants.OBJID_WINDOW || hwnd == 0)
        {
            return;
        }

        if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZESTART)
        {
            Started?.Invoke(hwnd);
        }
        else if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZEEND)
        {
            Ended?.Invoke(hwnd);
        }
    }
}
```

- [ ] **Step 4: Write the preview window**

`src/Loadstone.UI/Input/ISnapPreview.cs`:

```csharp
using Loadstone.Core.Geometry;

namespace Loadstone.UI.Input;

internal interface ISnapPreview
{
    /// <summary>Shows the preview over <paramref name="frame"/> (physical pixels).</summary>
    void Show(Rect frame);

    void Hide();
}
```

`src/Loadstone.UI/Input/SnapPreview.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// The translucent blue rectangle shown over the tile a drag will snap to. Borderless, click-through,
/// never activated, topmost, placed with SetWindowPos in physical pixels because WPF's own placement
/// is wrong on mixed-DPI desks. Only ever hidden, never closed.
/// </summary>
internal sealed class SnapPreview : Window, ISnapPreview
{
    public SnapPreview()
    {
        Title = "Loadstone snap preview";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;

        var accent = SystemParameters.WindowGlassColor;
        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B)),   // 22 percent
            BorderBrush = new SolidColorBrush(Color.FromArgb(217, accent.R, accent.G, accent.B)), // 85 percent
        };

        SourceInitialized += (_, _) =>
        {
            var handle = Handle;
            var exStyle = User32.GetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE);
            User32.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE,
                exStyle | (nint)(NativeConstants.WS_EX_TRANSPARENT | NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE));
        };
    }

    private nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public void Show(Rect frame)
    {
        // Inset a little so it reads as a preview rather than as the window itself.
        var inset = frame.Inflate(-6, -6);
        if (!IsVisible)
        {
            Show();
        }

        // Topmost is asserted by this process on its own window, which Windows honors even for a
        // background process; a request from another process would be refused.
        User32.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, inset.X, inset.Y, inset.Width, inset.Height,
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_SHOWWINDOW);
    }

    public new void Hide()
    {
        if (IsVisible)
        {
            base.Hide();
        }
    }
}
```

- [ ] **Step 5: Write the drag monitor**

`src/Loadstone.UI/Input/DragSnapMonitor.cs`:

```csharp
using System.Windows.Threading;
using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// Snaps a window dragged to a screen edge. Listens to the OS move-loop events while drag snapping
/// is on, samples the cursor at 60 Hz during a drag, previews the tile, and commits after the OS
/// has finished the drop so Loadstone's frame is the last write. The decisions live in
/// <see cref="DragSnapTracker{TWindow}"/>.
/// </summary>
internal sealed class DragSnapMonitor : IDisposable
{
    private readonly IMoveSizeEvents _events;
    private readonly WindowDirector _director;
    private readonly ISettingsService _settings;
    private readonly ISnapPreview _preview;
    private readonly ILogger<DragSnapMonitor> _logger;
    private readonly Func<IReadOnlyList<Display>> _displays;
    private readonly Func<Point> _cursor;
    private readonly Func<nint, IMovableWindow?> _windowFactory;
    private readonly DragSnapTracker<IMovableWindow> _tracker = new();
    private readonly DispatcherTimer _timer;
    private bool _started;

    public DragSnapMonitor(IMoveSizeEvents events, WindowDirector director, ISettingsService settings, ISnapPreview preview, ILogger<DragSnapMonitor> logger)
        : this(events, director, settings, preview, logger, Displays.Snapshot, CursorPosition, ManageableWindow)
    {
    }

    internal DragSnapMonitor(
        IMoveSizeEvents events,
        WindowDirector director,
        ISettingsService settings,
        ISnapPreview preview,
        ILogger<DragSnapMonitor> logger,
        Func<IReadOnlyList<Display>> displays,
        Func<Point> cursor,
        Func<nint, IMovableWindow?> windowFactory)
    {
        _events = events;
        _director = director;
        _settings = settings;
        _preview = preview;
        _logger = logger;
        _displays = displays;
        _cursor = cursor;
        _windowFactory = windowFactory;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    /// <summary>True while the hook is installed: drag snapping is on and the system accepted the hook.</summary>
    public bool IsMonitoring => _events.IsInstalled;

    /// <summary>Installs the hook while drag snapping is on and removes it while it is off, following the setting from then on.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _events.Started += OnStarted;
        _events.Ended += OnEnded;
        _settings.Changed += OnSettingsChanged;
        Follow();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _settings.Changed -= OnSettingsChanged;
        _events.Started -= OnStarted;
        _events.Ended -= OnEnded;
        Uninstall();
    }

    public void Dispose() => Stop();

    private void OnSettingsChanged(object? sender, EventArgs e) => Follow();

    private void Follow()
    {
        if (_settings.Current.DragSnappingEnabled)
        {
            Install();
        }
        else
        {
            Uninstall();
        }
    }

    private void Install()
    {
        if (_events.IsInstalled)
        {
            return;
        }

        if (!_events.Install())
        {
            _logger.LogError("could not install the move-size event hook; drag snapping is off");
            return;
        }

        _logger.LogInformation("drag snapping on");
    }

    private void Uninstall()
    {
        _timer.Stop();
        _tracker.Reset();
        _preview.Hide();
        if (!_events.IsInstalled)
        {
            return;
        }

        _events.Uninstall();
        _logger.LogInformation("drag snapping off");
    }

    private void OnStarted(nint hwnd)
    {
        if (_windowFactory(hwnd) is not { } window || window.Frame is not { } baseline)
        {
            _tracker.Reset();
            _timer.Stop();
            return;
        }

        _tracker.Begin(window, baseline, _displays());
        _timer.Start();
    }

    /// <summary>One cursor sample. Public to tests; the timer calls it at 60 Hz during a drag.</summary>
    internal void Tick()
    {
        if (!_tracker.IsDragging || _tracker.Window is not { } window)
        {
            _timer.Stop();
            return;
        }

        if (_tracker.Update(_cursor(), window.Frame) is { } target)
        {
            _preview.Show(target.Tile.Frame(target.Display.WorkArea));
        }
        else
        {
            _preview.Hide();
        }
    }

    private void OnEnded(nint hwnd)
    {
        _timer.Stop();
        _preview.Hide();
        if (!_tracker.IsDragging || _tracker.Window is not { } window)
        {
            return;
        }

        if (window.Identity.Hwnd != hwnd)
        {
            _tracker.Reset();
            return;
        }

        if (_tracker.End(_cursor(), window.Frame) is not var (dragged, target))
        {
            return;
        }

        var outcome = _director.Snap(target.Tile, dragged, target.Display);
        if (outcome.IsMoved)
        {
            _logger.LogDebug("snapped window {Hwnd} to {Tile}", dragged.Identity.Hwnd, target.Tile);
        }
        else
        {
            _logger.LogError("snap of window {Hwnd} to {Tile} failed: {Outcome}", dragged.Identity.Hwnd, target.Tile, outcome);
            User32.MessageBeep(NativeConstants.MB_OK);
        }
    }

    private static Point CursorPosition()
    {
        User32.GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    /// <summary>The root of the window in the move loop, if Loadstone may manage it.</summary>
    private static IMovableWindow? ManageableWindow(nint hwnd)
    {
        var root = User32.GetAncestor(hwnd, NativeConstants.GA_ROOT);
        if (root == 0)
        {
            root = hwnd;
        }

        return WindowStyles.IsManageable(WindowStyles.Snapshot(root), Environment.ProcessId) ? new Win32Window(root) : null;
    }
}
```

The `is not var (dragged, target)` pattern deconstructs the nullable tuple `End` returns; if the compiler rejects it, write `var ended = _tracker.End(...); if (ended is null) return; var (dragged, target) = ended.Value;`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass. In `Drag_EndEventForAnotherWindow_ResetsWithoutSnapping`, the second `RaiseEnded(10)` must not snap because the tracker was reset by the first.

- [ ] **Step 7: Commit**

```bash
git add src/Loadstone.UI/Input tests/Loadstone.UI.Tests/Input/DragSnapMonitorTests.cs
git commit -F - <<'EOF'
Add drag-to-snap: move-size events, the preview overlay, and the monitor

A WinEvent hook reports when a window enters and leaves its move loop;
the monitor samples the cursor at 60 Hz during a drag, previews the
tile through a topmost click-through window placed in physical pixels,
and commits after the OS finishes the drop.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---
### Task 17: Platform services: snap flags, launch at login, single instance

Spec sections 7.1 (UI half), 7.2, and 9 (single instance). `SystemParametersNativeSnapSettings` reads and writes the two edge-snapping flags; for the SET actions the boolean travels in `uiParam`. `RegistryStartupRegistration` manages the Run key value and reads Task Manager's approval blob. `SingleInstance` is a named mutex plus a named event.

**Files:**
- Create: `src/Loadstone.UI/Services/SystemParametersNativeSnapSettings.cs`, `src/Loadstone.UI/Services/RegistryStartupRegistration.cs`, `src/Loadstone.UI/Services/SingleInstance.cs`
- Test: `tests/Loadstone.UI.Tests/Services/SystemParametersNativeSnapSettingsTests.cs`, `tests/Loadstone.UI.Tests/Services/RegistryStartupRegistrationTests.cs`, `tests/Loadstone.UI.Tests/Services/SingleInstanceTests.cs`

**Interfaces:**
- Consumes: `INativeSnapSettings`, `NativeSnap.DockMoving`, `NativeSnap.SnapSizing`, `IStartupRegistration`, `StartupStatus`, `StartupApproval` (Task 10); interop (Task 11).
- Produces: `internal sealed class SystemParametersNativeSnapSettings : INativeSnapSettings`; `internal sealed class RegistryStartupRegistration : IStartupRegistration` with public constructor `()` and internal constructor `(string runKeyPath, string approvedKeyPath, string exePath)`, plus `void RefreshPathIfStale()`; `internal static class SingleInstance { static Mutex? TryAcquire(); static void SignalShowSettings(); static IDisposable ListenForShowSettings(Action onSignal); }`.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/Services/SystemParametersNativeSnapSettingsTests.cs`:

```csharp
using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class SystemParametersNativeSnapSettingsTests
{
    private readonly SystemParametersNativeSnapSettings _settings = new();

    [Fact]
    public void Read_KnownFlags_HaveAValue()
    {
        _settings.Read(NativeSnap.DockMoving).ShouldNotBeNull();
        _settings.Read(NativeSnap.SnapSizing).ShouldNotBeNull();
    }

    [Fact]
    public void Read_UnknownFlag_IsNull()
    {
        _settings.Read("NoSuchFlag").ShouldBeNull();
        _settings.Write("NoSuchFlag", true).ShouldBeFalse();
    }

    [Fact]
    public void Write_TheCurrentValueBack_SucceedsAndReadsTheSame()
    {
        // Writes only what is already there, so the user's setting is unchanged by this test.
        foreach (var flag in NativeSnap.ManagedFlags)
        {
            var current = _settings.Read(flag).ShouldNotBeNull();
            _settings.Write(flag, current).ShouldBeTrue();
            _settings.Read(flag).ShouldBe(current);
        }
    }
}
```

`tests/Loadstone.UI.Tests/Services/RegistryStartupRegistrationTests.cs`:

```csharp
using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Microsoft.Win32;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class RegistryStartupRegistrationTests : IDisposable
{
    private readonly string _root = @"Software\LoadstoneTests\" + Guid.NewGuid().ToString("N");
    private string RunKey => _root + @"\Run";
    private string ApprovedKey => _root + @"\StartupApproved";
    private const string Exe = @"C:\Program Files\Loadstone\Loadstone.exe";

    private RegistryStartupRegistration NewRegistration(string exe = Exe) => new(RunKey, ApprovedKey, exe);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Status_WithoutAValue_IsNotRegistered()
    {
        NewRegistration().Status.ShouldBe(StartupStatus.NotRegistered);
    }

    [Fact]
    public void Enable_WritesTheQuotedExePath_AndStatusIsEnabled()
    {
        var registration = NewRegistration();
        registration.Enable();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{Exe}\"");
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    [Fact]
    public void Disable_RemovesTheValue()
    {
        var registration = NewRegistration();
        registration.Enable();
        registration.Disable();
        registration.Status.ShouldBe(StartupStatus.NotRegistered);
        Should.NotThrow(registration.Disable);
    }

    [Fact]
    public void Status_WhenTaskManagerDisabledIt_SaysSo()
    {
        var registration = NewRegistration();
        registration.Enable();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        registration.Status.ShouldBe(StartupStatus.DisabledInTaskManager);
    }

    [Fact]
    public void Enable_ClearsTaskManagersDisable()
    {
        var registration = NewRegistration();
        registration.Enable();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        registration.Enable();
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    [Fact]
    public void RefreshPathIfStale_RewritesAMovedExe_AndLeavesAnUnregisteredEntryAlone()
    {
        NewRegistration(@"C:\old\Loadstone.exe").Enable();
        var moved = NewRegistration(@"C:\new\Loadstone.exe");
        moved.RefreshPathIfStale();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe("\"C:\\new\\Loadstone.exe\"");

        moved.Disable();
        moved.RefreshPathIfStale();
        moved.Status.ShouldBe(StartupStatus.NotRegistered);
    }
}
```

`tests/Loadstone.UI.Tests/Services/SingleInstanceTests.cs`:

```csharp
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_SecondCallerGetsNothing_UntilTheFirstReleases()
    {
        using var first = SingleInstance.TryAcquire();
        first.ShouldNotBeNull();
        SingleInstance.TryAcquire().ShouldBeNull();
        first.ReleaseMutex();
        first.Dispose();

        using var again = SingleInstance.TryAcquire();
        again.ShouldNotBeNull();
        again.ReleaseMutex();
    }

    [Fact]
    public void SignalShowSettings_ReachesTheListener()
    {
        using var signalled = new ManualResetEventSlim();
        using var listener = SingleInstance.ListenForShowSettings(() => signalled.Set());

        SingleInstance.SignalShowSettings();

        signalled.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'Services' does not exist in the namespace 'Loadstone.UI'".

- [ ] **Step 3: Write the implementation**

`src/Loadstone.UI/Services/SystemParametersNativeSnapSettings.cs`:

```csharp
using Loadstone.Core.Platform;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Services;

/// <summary>
/// Windows' drag-to-edge docking and drag-to-edge sizing flags, over SystemParametersInfo. Reads
/// take a pointer to a BOOL; for the SET actions the BOOL travels in uiParam with pvParam zero
/// (passing it in pvParam silently writes FALSE). Writes persist and broadcast the change.
/// </summary>
internal sealed class SystemParametersNativeSnapSettings : INativeSnapSettings
{
    private static readonly Dictionary<string, (uint Get, uint Set)> Actions = new()
    {
        [NativeSnap.DockMoving] = (NativeConstants.SPI_GETDOCKMOVING, NativeConstants.SPI_SETDOCKMOVING),
        [NativeSnap.SnapSizing] = (NativeConstants.SPI_GETSNAPSIZING, NativeConstants.SPI_SETSNAPSIZING),
    };

    public bool? Read(string flag)
    {
        if (!Actions.TryGetValue(flag, out var action))
        {
            return null;
        }

        return User32.SystemParametersInfoGet(action.Get, 0, out var value, 0) ? value != 0 : null;
    }

    public bool Write(string flag, bool value)
    {
        if (!Actions.TryGetValue(flag, out var action))
        {
            return false;
        }

        return User32.SystemParametersInfoSet(action.Set, value ? 1u : 0u, 0, NativeConstants.SPIF_UPDATEINIFILE | NativeConstants.SPIF_SENDCHANGE);
    }
}
```

`src/Loadstone.UI/Services/RegistryStartupRegistration.cs`:

```csharp
using Loadstone.Core.Platform;
using Microsoft.Win32;

namespace Loadstone.UI.Services;

/// <summary>
/// Launch at login through the user's Run key. Task Manager's Startup tab records its own
/// enable/disable under the StartupApproved key; enabling here clears that so the entry is live again.
/// </summary>
internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    public const string ValueName = "Loadstone";

    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;
    private readonly string _exePath;

    public RegistryStartupRegistration()
        : this(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            Environment.ProcessPath ?? throw new InvalidOperationException("the running exe has no path"))
    {
    }

    internal RegistryStartupRegistration(string runKeyPath, string approvedKeyPath, string exePath)
    {
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
        _exePath = exePath;
    }

    private string QuotedExePath => $"\"{_exePath}\"";

    public StartupStatus Status
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            if (run?.GetValue(ValueName) is not string)
            {
                return StartupStatus.NotRegistered;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath);
            return StartupApproval.IsDisabled(approved?.GetValue(ValueName) as byte[])
                ? StartupStatus.DisabledInTaskManager
                : StartupStatus.Enabled;
        }
    }

    public void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
        {
            run.SetValue(ValueName, QuotedExePath);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>After an installer moved the exe the Run value still points at the old path; rewrite it. Does nothing when not registered.</summary>
    public void RefreshPathIfStale()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (run?.GetValue(ValueName) is string current && current != QuotedExePath)
        {
            run.SetValue(ValueName, QuotedExePath);
        }
    }
}
```

`src/Loadstone.UI/Services/SingleInstance.cs`:

```csharp
namespace Loadstone.UI.Services;

/// <summary>
/// One Loadstone at a time. The first instance holds a named mutex; a second instance sets a named
/// event and exits, and the first opens Settings. A broadcast window message would not do: message-only
/// windows never receive broadcasts, and Loadstone has no other window that is always alive.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = "Loadstone-SingleInstance";
    private const string EventName = "Loadstone-ShowSettings";

    /// <summary>The mutex to hold for the life of the process, or null when another instance holds it.</summary>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalShowSettings()
    {
        using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        handle.Set();
    }

    /// <summary>Calls <paramref name="onSignal"/> on a thread-pool thread each time another instance signals. Dispose to stop listening.</summary>
    public static IDisposable ListenForShowSettings(Action onSignal)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
        return new Listener(registration, handle);
    }

    private sealed class Listener(RegisteredWaitHandle registration, EventWaitHandle handle) : IDisposable
    {
        public void Dispose()
        {
            registration.Unregister(null);
            handle.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: all pass. `Write_TheCurrentValueBack` writes the value already present, so the desktop's snap setting is unchanged; confirm with `Get-ItemProperty 'HKCU:\Control Panel\Desktop' | Select DockMoving, SnapSizing` before and after if in doubt.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Services tests/Loadstone.UI.Tests/Services
git commit -F - <<'EOF'
Add the snap-flag, launch-at-login, and single-instance services

SystemParametersInfo over the dock-moving and snap-sizing flags with the
value in uiParam, the Run key with Task Manager's approval decoded, and a
named mutex plus event so a second launch opens Settings in the first.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 18: Tray icons, menu builder, and controller

Spec section 8.1. Mac reference: `StatusItemController.swift`. The icons are generated by a script so they can be regenerated; the menu is built from `WindowCommands` by a pure builder; the controller captures the foreground window before the menu opens because opening a tray menu makes Loadstone the foreground application.

**Files:**
- Create: `tools/icons/generate.ps1`, `src/Loadstone.UI/Assets/loadstone.ico`, `src/Loadstone.UI/Assets/tray-dark.ico`, `src/Loadstone.UI/Assets/tray-light.ico`
- Create: `src/Loadstone.UI/Tray/TrayMenuEntry.cs`, `src/Loadstone.UI/Tray/TrayMenuBuilder.cs`, `src/Loadstone.UI/Tray/IShellActions.cs`, `src/Loadstone.UI/Tray/TrayController.cs`
- Modify: `src/Loadstone.UI/Loadstone.UI.csproj` (application icon, resources)
- Test: `tests/Loadstone.UI.Tests/Tray/TrayMenuBuilderTests.cs`

**Interfaces:**
- Consumes: `WindowCommand`, `WindowCommands`, `Shortcut.DisplayText` (Tasks 5 and 6); `ICommandDispatcher` (Task 13); `HotkeyCenter` (Task 15); `FocusedWindow` (Task 12).
- Produces: `internal enum TrayMenuEntryKind { Command, Separator, Settings, Help, Quit }`; `internal sealed record TrayMenuEntry(TrayMenuEntryKind Kind, string Title = "", string? Gesture = null, WindowCommand? Command = null)`; `internal static class TrayMenuBuilder { static IReadOnlyList<TrayMenuEntry> Build(Func<WindowCommand, Shortcut?> effectiveShortcut); }`; `internal interface IShellActions { void OpenSettings(); void Quit(); }`; `internal sealed class TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, IShellActions shell) : IDisposable` with `const string HelpUrl` and `static bool IsTaskbarLight()`.

- [ ] **Step 1: Write the icon generator and run it**

`tools/icons/generate.ps1`:

```powershell
# Regenerates the .ico assets. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
# tray-dark.ico  : white glyph, for the dark taskbar
# tray-light.ico : near-black glyph, for the light taskbar
# loadstone.ico  : the app icon, a white glyph on a blue rounded square
param([string]$OutDir = (Join-Path $PSScriptRoot '..\..\src\Loadstone.UI\Assets'))

Add-Type -AssemblyName System.Drawing

function New-PngFrame([int]$size, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $draw $g $size
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# An ICO is a header, one directory entry per image, then the images. PNG-compressed entries are
# supported since Vista, which is what lets a 256 px frame fit.
function Write-Ico([string]$path, [int[]]$sizes, [scriptblock]$draw) {
    $frames = foreach ($s in $sizes) { ,@($s, (New-PngFrame $s $draw)) }
    $stream = [System.IO.File]::Create($path)
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $s = $frame[0]; $bytes = $frame[1]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
    $writer.Dispose(); $stream.Dispose()
    "wrote $path"
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90)
    $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90)
    $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $p.CloseFigure()
    return $p
}

# The glyph: a rounded rectangle split into a left and a right pane.
function Draw-Glyph($g, [int]$size, [System.Drawing.Color]$color) {
    $stroke = [math]::Max(1.0, $size / 9.0)
    $pen = New-Object System.Drawing.Pen $color, $stroke
    $inset = $stroke
    $x = $inset; $y = $size * 0.2 + $inset; $w = $size - 2 * $inset; $h = $size * 0.6 - 2 * $inset
    $path = New-RoundedPath $x $y $w $h ([math]::Max(2.0, $size * 0.18))
    $g.DrawPath($pen, $path)
    $mid = $x + $w / 2
    $g.DrawLine($pen, $mid, $y, $mid, $y + $h)
    $pen.Dispose(); $path.Dispose()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Ico (Join-Path $OutDir 'tray-dark.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Glyph $g $s ([System.Drawing.Color]::White) }
Write-Ico (Join-Path $OutDir 'tray-light.ico') @(16, 20, 24, 32, 48) { param($g, $s) Draw-Glyph $g $s ([System.Drawing.Color]::FromArgb(255, 31, 31, 31)) }
Write-Ico (Join-Path $OutDir 'loadstone.ico') @(16, 24, 32, 48, 64, 128, 256) {
    param($g, $s)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 120, 212))
    $bg = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $g.FillPath($brush, $bg)
    $inner = [int]($s * 0.72)
    $g.TranslateTransform(($s - $inner) / 2, ($s - $inner) / 2)
    Draw-Glyph $g $inner ([System.Drawing.Color]::White)
    $g.ResetTransform()
    $brush.Dispose(); $bg.Dispose()
}
```

Run: `& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File tools/icons/generate.ps1`
Expected: three `wrote ...` lines and three `.ico` files under `src/Loadstone.UI/Assets/`. Open `loadstone.ico` in Explorer's preview to eyeball it: a blue rounded square with a white two-pane glyph.

- [ ] **Step 2: Register the icons in the project**

Add to `src/Loadstone.UI/Loadstone.UI.csproj`, inside the first `PropertyGroup`:

```xml
    <ApplicationIcon>Assets\loadstone.ico</ApplicationIcon>
```

and a new item group:

```xml
  <ItemGroup>
    <Resource Include="Assets\*.ico" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing menu builder tests**

`tests/Loadstone.UI.Tests/Tray/TrayMenuBuilderTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Tray;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Tray;

public class TrayMenuBuilderTests
{
    [Fact]
    public void Build_ListsEveryCommandInSectionOrder_WithSeparatorsAndFixedItems()
    {
        var entries = TrayMenuBuilder.Build(c => c.DefaultShortcut());

        var commands = entries.Where(e => e.Kind == TrayMenuEntryKind.Command).Select(e => e.Command!.Value).ToList();
        commands.ShouldBe(WindowCommands.All);

        // One separator after each of the five sections, one before Quit.
        entries.Count(e => e.Kind == TrayMenuEntryKind.Separator).ShouldBe(6);

        var tail = entries.Skip(entries.Count - 4).Select(e => e.Kind).ToList();
        tail.ShouldBe([TrayMenuEntryKind.Settings, TrayMenuEntryKind.Help, TrayMenuEntryKind.Separator, TrayMenuEntryKind.Quit]);
        entries.Single(e => e.Kind == TrayMenuEntryKind.Settings).Title.ShouldBe("Settings…");
        entries.Single(e => e.Kind == TrayMenuEntryKind.Help).Title.ShouldBe("Loadstone Help");
        entries.Single(e => e.Kind == TrayMenuEntryKind.Quit).Title.ShouldBe("Quit Loadstone");
    }

    [Fact]
    public void Build_SectionsAreSeparated()
    {
        var entries = TrayMenuBuilder.Build(c => c.DefaultShortcut());
        var firstSeparator = entries.ToList().FindIndex(e => e.Kind == TrayMenuEntryKind.Separator);
        firstSeparator.ShouldBe(4);
        entries[firstSeparator + 1].Command.ShouldBe(WindowCommand.TopLeft);
    }

    [Fact]
    public void Build_ShowsTheEffectiveChordAsGestureText()
    {
        var entries = TrayMenuBuilder.Build(c => c == WindowCommand.Center ? null : c.DefaultShortcut());
        entries.Single(e => e.Command == WindowCommand.LeftHalf).Gesture.ShouldBe("Ctrl+Alt+←");
        entries.Single(e => e.Command == WindowCommand.Center).Gesture.ShouldBeNull();
        entries.Single(e => e.Command == WindowCommand.TopLeft).Title.ShouldBe("Top Left Corner");
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'Tray' does not exist in the namespace 'Loadstone.UI'".

- [ ] **Step 5: Write the menu model, the builder, the shell actions seam, and the controller**

`src/Loadstone.UI/Tray/TrayMenuEntry.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Tray;

internal enum TrayMenuEntryKind
{
    Command,
    Separator,
    Settings,
    Help,
    Quit,
}

/// <summary>One line of the tray menu, as data, so the menu's shape is testable without WPF.</summary>
internal sealed record TrayMenuEntry(TrayMenuEntryKind Kind, string Title = "", string? Gesture = null, WindowCommand? Command = null);
```

`src/Loadstone.UI/Tray/TrayMenuBuilder.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Tray;

internal static class TrayMenuBuilder
{
    /// <summary>Every command in section order with a separator after each section, then Settings, Help, a separator, and Quit.</summary>
    public static IReadOnlyList<TrayMenuEntry> Build(Func<WindowCommand, Shortcut?> effectiveShortcut)
    {
        var entries = new List<TrayMenuEntry>();
        foreach (var section in WindowCommands.SectionOrder)
        {
            foreach (var command in WindowCommands.In(section))
            {
                entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Command, command.Title(), effectiveShortcut(command)?.DisplayText, command));
            }

            entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Separator));
        }

        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Settings, "Settings…"));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Help, "Loadstone Help"));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Separator));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Quit, "Quit Loadstone"));
        return entries;
    }
}
```

`src/Loadstone.UI/Tray/IShellActions.cs`:

```csharp
namespace Loadstone.UI.Tray;

/// <summary>What the tray asks the application to do. App implements it; the tray never references App.</summary>
internal interface IShellActions
{
    void OpenSettings();

    void Quit();
}
```

`src/Loadstone.UI/Tray/TrayController.cs`:

```csharp
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Windows;
using Microsoft.Win32;

namespace Loadstone.UI.Tray;

/// <summary>
/// The tray icon and its menu. Right click opens the menu, left click opens Settings. The foreground
/// window is captured just before the menu opens, because opening it makes Loadstone the foreground
/// application, and menu commands act on that captured window.
/// </summary>
internal sealed class TrayController : IDisposable
{
    public const string HelpUrl = "https://github.com/mortenbrudvik/loadstone-win#readme";

    private readonly ICommandDispatcher _dispatcher;
    private readonly HotkeyCenter _hotkeys;
    private readonly IShellActions _shell;
    private readonly ContextMenu _menu = new();
    private readonly TaskbarIcon _icon;
    private Icon? _trayIcon;
    private nint _capturedForeground;

    public TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, IShellActions shell)
    {
        _dispatcher = dispatcher;
        _hotkeys = hotkeys;
        _shell = shell;

        Rebuild();
        _icon = new TaskbarIcon
        {
            ToolTipText = "Loadstone",
            ContextMenu = _menu,
            MenuActivation = PopupActivationMode.RightClick,
        };
        ApplyThemeIcon();
        _icon.ForceCreate();

        _icon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen += OnPreviewTrayContextMenuOpen;
        _hotkeys.BindingsChanged += OnBindingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Light taskbar means a dark glyph. Read from the personalization key Windows keeps for the system theme.</summary>
    public static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hotkeys.BindingsChanged -= OnBindingsChanged;
        _icon.TrayLeftMouseUp -= OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen -= OnPreviewTrayContextMenuOpen;
        _icon.Dispose();
        _trayIcon?.Dispose();
    }

    private void OnTrayLeftMouseUp(object? sender, RoutedEventArgs e) => _shell.OpenSettings();

    private void OnPreviewTrayContextMenuOpen(object? sender, RoutedEventArgs e) => _capturedForeground = FocusedWindow.Resolve() ?? 0;

    private void OnBindingsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(ApplyThemeIcon);
        }
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        foreach (var entry in TrayMenuBuilder.Build(_hotkeys.EffectiveShortcut))
        {
            _menu.Items.Add(ToItem(entry));
        }
    }

    private object ToItem(TrayMenuEntry entry) => entry.Kind switch
    {
        TrayMenuEntryKind.Separator => new Separator(),
        TrayMenuEntryKind.Command => new MenuItem
        {
            Header = entry.Title,
            InputGestureText = entry.Gesture,
            Command = new RelayCommand(() => RunCaptured(entry.Command!.Value)),
        },
        TrayMenuEntryKind.Settings => new MenuItem { Header = entry.Title, Command = new RelayCommand(_shell.OpenSettings) },
        TrayMenuEntryKind.Help => new MenuItem { Header = entry.Title, Command = new RelayCommand(OpenHelp) },
        TrayMenuEntryKind.Quit => new MenuItem { Header = entry.Title, Command = new RelayCommand(_shell.Quit) },
    };

    private void RunCaptured(WindowCommand command)
    {
        if (_capturedForeground != 0)
        {
            _dispatcher.Run(command, _capturedForeground);
        }
        else
        {
            _dispatcher.Run(command);
        }
    }

    private static void OpenHelp() => Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true });

    private void ApplyThemeIcon()
    {
        var name = IsTaskbarLight() ? "tray-light.ico" : "tray-dark.ico";
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;
        var icon = new Icon(stream);
        _icon.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;
    }
}
```

`Application` here is `System.Windows.Application` and `Icon` is `System.Drawing.Icon`; both `using`s are listed. If `System.Windows.Forms` types leak in through H.NotifyIcon and make `Application` ambiguous, add `using Application = System.Windows.Application;`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Loadstone.UI.Tests` and `dotnet build Loadstone.sln`
Expected: all pass; the build is warning-free. `TrayController` has no unit test; Task 21's smoke run exercises it.

- [ ] **Step 7: Commit**

```bash
git add tools/icons src/Loadstone.UI/Assets src/Loadstone.UI/Tray src/Loadstone.UI/Loadstone.UI.csproj tests/Loadstone.UI.Tests/Tray
git commit -F - <<'EOF'
Add the tray icon, its generated assets, and the menu

The menu is built as data from WindowCommands, with each command's
effective chord as gesture text. The controller captures the foreground
window before the menu opens and follows the taskbar theme with a light
or dark glyph. An icon generator script produces the .ico files.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 19: Recorder key handling and the shortcut recorder control

Spec section 8.3. The key rules are a pure class; the control is a `UserControl` that records on focus, commits, clears, or cancels through the handler, and exposes a two-way `Shortcut` property plus `IsRecording` for the view model to suspend hotkeys.

**Files:**
- Create: `src/Loadstone.UI/Controls/RecorderKeyHandler.cs`, `src/Loadstone.UI/Controls/ShortcutRecorder.xaml`, `src/Loadstone.UI/Controls/ShortcutRecorder.xaml.cs`
- Test: `tests/Loadstone.UI.Tests/Controls/RecorderKeyHandlerTests.cs`

**Interfaces:**
- Consumes: `Shortcut`, `KeyModifiers`, `VirtualKeys` (Task 5).
- Produces: `internal enum RecorderActionKind { Ignore, Commit, Clear, Cancel }`; `internal readonly record struct RecorderAction(RecorderActionKind Kind, Shortcut? Shortcut = null)`; `internal static class RecorderKeyHandler { static RecorderAction Handle(KeyModifiers held, int virtualKey); }`; `public partial class ShortcutRecorder : UserControl` with dependency properties `Shortcut` (`Shortcut?`, two-way by default) and `IsRecording` (`bool`, two-way by default).

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/Controls/RecorderKeyHandlerTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Controls;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Controls;

public class RecorderKeyHandlerTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

    [Fact]
    public void Handle_ModifierWithKey_Commits()
    {
        var action = RecorderKeyHandler.Handle(CtrlAlt, 'E');
        action.Kind.ShouldBe(RecorderActionKind.Commit);
        action.Shortcut.ShouldBe(new Shortcut(CtrlAlt, 'E'));
    }

    [Fact]
    public void Handle_FunctionKeyAlone_Commits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.F1 + 4).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(KeyModifiers.None, VirtualKeys.F1 + 4)));
    }

    [Fact]
    public void Handle_PlainKeyAlone_IsIgnored()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, 'E').Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Left).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Theory]
    [InlineData(VirtualKeys.ShiftKey)]
    [InlineData(VirtualKeys.ControlKey)]
    [InlineData(VirtualKeys.MenuKey)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RMenu)]
    public void Handle_LoneModifier_IsIgnored_EvenWithOthersHeld(int modifierKey)
    {
        RecorderKeyHandler.Handle(CtrlAlt, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Fact]
    public void Handle_Escape_Cancels()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
    }

    [Fact]
    public void Handle_BackspaceOrDeleteAlone_Clears_ButWithModifiersCommits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Backspace).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Delete).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Backspace).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(CtrlAlt, VirtualKeys.Backspace)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'Controls' does not exist in the namespace 'Loadstone.UI'".

- [ ] **Step 3: Write the handler and the control**

`src/Loadstone.UI/Controls/RecorderKeyHandler.cs`:

```csharp
using Loadstone.Core.Commands;

namespace Loadstone.UI.Controls;

internal enum RecorderActionKind
{
    Ignore,
    Commit,
    Clear,
    Cancel,
}

internal readonly record struct RecorderAction(RecorderActionKind Kind, Shortcut? Shortcut = null);

/// <summary>The recorder's key rules, kept pure so they can be tested without a keyboard.</summary>
internal static class RecorderKeyHandler
{
    /// <param name="held">The modifier keys down at the moment of the key press.</param>
    /// <param name="virtualKey">The key pressed.</param>
    public static RecorderAction Handle(KeyModifiers held, int virtualKey)
    {
        if (VirtualKeys.IsModifier(virtualKey))
        {
            return new RecorderAction(RecorderActionKind.Ignore);
        }

        if (virtualKey == VirtualKeys.Escape)
        {
            return new RecorderAction(RecorderActionKind.Cancel);
        }

        if (held == KeyModifiers.None && virtualKey is VirtualKeys.Backspace or VirtualKeys.Delete)
        {
            return new RecorderAction(RecorderActionKind.Clear);
        }

        var isFunctionKey = virtualKey is >= VirtualKeys.F1 and <= VirtualKeys.F24;
        if (held == KeyModifiers.None && !isFunctionKey)
        {
            return new RecorderAction(RecorderActionKind.Ignore);
        }

        return new RecorderAction(RecorderActionKind.Commit, new Shortcut(held, virtualKey));
    }
}
```

`src/Loadstone.UI/Controls/ShortcutRecorder.xaml`:

```xml
<UserControl
    x:Class="Loadstone.UI.Controls.ShortcutRecorder"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Focusable="True"
    MinWidth="180"
    Height="32">

  <Border
      x:Name="Outline"
      Background="{DynamicResource ControlFillColorDefaultBrush}"
      BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
      BorderThickness="1"
      CornerRadius="4">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
      </Grid.ColumnDefinitions>

      <TextBlock
          x:Name="Text"
          Grid.Column="0"
          Margin="10,0,4,0"
          VerticalAlignment="Center"
          Foreground="{DynamicResource TextFillColorPrimaryBrush}" />

      <ui:Button
          x:Name="ClearButton"
          Grid.Column="1"
          Margin="2"
          Padding="4,0"
          Appearance="Transparent"
          Icon="{ui:SymbolIcon Dismiss16}"
          ToolTip="Clear"
          Focusable="False"
          Click="OnClearClick" />
    </Grid>
  </Border>

</UserControl>
```

`src/Loadstone.UI/Controls/ShortcutRecorder.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Loadstone.Core.Commands;

namespace Loadstone.UI.Controls;

/// <summary>
/// Records a global shortcut. Click or focus starts recording; a key press with a modifier commits,
/// a function key commits alone, Escape cancels, Backspace or Delete alone clears. While recording,
/// the owning view model suspends the real hotkeys through <see cref="IsRecording"/>.
/// </summary>
public partial class ShortcutRecorder : UserControl
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(Shortcut), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutRecorder)d).Refresh()));

    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording), typeof(bool), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutRecorder)d).Refresh()));

    public ShortcutRecorder()
    {
        InitializeComponent();
        GotKeyboardFocus += (_, _) => IsRecording = true;
        LostKeyboardFocus += (_, _) => IsRecording = false;
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            e.Handled = true;
        };
        Refresh();
    }

    public Shortcut? Shortcut
    {
        get => (Shortcut?)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        set => SetValue(IsRecordingProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var action = RecorderKeyHandler.Handle(Held(Keyboard.Modifiers), virtualKey);
        switch (action.Kind)
        {
            case RecorderActionKind.Commit:
                Shortcut = action.Shortcut;
                StopRecording();
                break;
            case RecorderActionKind.Clear:
                Shortcut = null;
                StopRecording();
                break;
            case RecorderActionKind.Cancel:
                StopRecording();
                break;
            case RecorderActionKind.Ignore:
                break;
        }
    }

    private static KeyModifiers Held(ModifierKeys modifiers)
    {
        var held = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) held |= KeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) held |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) held |= KeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) held |= KeyModifiers.Win;
        return held;
    }

    private void StopRecording()
    {
        IsRecording = false;
        // Move focus away so the next click starts a fresh recording.
        Keyboard.ClearFocus();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Shortcut = null;
        StopRecording();
    }

    private void Refresh()
    {
        Text.Text = IsRecording ? "Press a shortcut" : Shortcut?.DisplayText ?? "Not set";
        Text.FontStyle = IsRecording || Shortcut is null ? FontStyles.Italic : FontStyles.Normal;
        // TryFindResource: in the constructor the control is not in a tree yet and the theme brushes may be unreachable.
        if (TryFindResource(IsRecording ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush") is Brush brush)
        {
            Outline.BorderBrush = brush;
        }

        ClearButton.Visibility = Shortcut is null || IsRecording ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 4: Run the tests and build**

Run: `dotnet test tests/Loadstone.UI.Tests` and `dotnet build Loadstone.sln`
Expected: all pass; the build is warning-free.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Controls tests/Loadstone.UI.Tests/Controls
git commit -F - <<'EOF'
Add the shortcut recorder and its pure key rules

A modifier plus a key commits, a function key commits alone, Escape
cancels, Backspace or Delete alone clears. The control records on focus
and exposes two-way Shortcut and IsRecording properties.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---
### Task 20: Settings window and view models

Spec section 8.2. Mac reference: `SettingsWindow.swift`. Two tabs in a WPF-UI `FluentWindow`: General (drag toggle, launch at login, About) and Shortcuts (grouped rows with a recorder and a status caption, plus Restore defaults). View models carry all the logic; the views have none.

**Files:**
- Create: `src/Loadstone.UI/ViewModels/ShortcutRowViewModel.cs`, `src/Loadstone.UI/ViewModels/ShortcutGroupViewModel.cs`, `src/Loadstone.UI/ViewModels/ShortcutsViewModel.cs`, `src/Loadstone.UI/ViewModels/SettingsViewModel.cs`
- Create: `src/Loadstone.UI/Views/SettingsWindow.xaml`, `src/Loadstone.UI/Views/SettingsWindow.xaml.cs`
- Test: `tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs`, `tests/Loadstone.UI.Tests/ViewModels/ShortcutRowViewModelTests.cs`

**Interfaces:**
- Consumes: `HotkeyCenter` (Task 15), `IHotkeyRegistrar`, `HotkeyStatus` (Task 14), `ISettingsService` (Task 9), `IStartupRegistration`, `StartupStatus` (Task 10), `ShortcutRecorder` (Task 19), `WindowCommands`, `Shortcut`.
- Produces: `internal sealed partial class ShortcutRowViewModel(WindowCommand command, HotkeyCenter hotkeys)` with `Title`, `Shortcut`, `IsRecording`, `Status`, `Refresh()`; `internal sealed record ShortcutGroupViewModel(string Header, IReadOnlyList<ShortcutRowViewModel> Rows)`; `internal sealed partial class ShortcutsViewModel(HotkeyCenter hotkeys)` with `Groups` and `RestoreDefaultsCommand`; `internal sealed partial class SettingsViewModel(ISettingsService settings, IStartupRegistration startup, ILogger<SettingsViewModel> logger)` with `DragSnappingEnabled`, `LaunchAtLogin`, `LaunchAtLoginMessage`, `ShowOpenStartupApps`, `Version`, `OpenStartupAppsCommand`, `RefreshStartup()`; `internal partial class SettingsWindow : FluentWindow` constructed with `(SettingsViewModel general, ShortcutsViewModel shortcuts)`, hiding instead of closing. Every one of these is `internal` with a `public` constructor: they depend on internal types, so they cannot be public themselves, and Autofac resolves internal classes through their public constructors without trouble. WPF binds to public properties of internal types and XAML in the same assembly may name them.

- [ ] **Step 1: Write the failing tests**

`tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using Loadstone.Core.Platform;
using Loadstone.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly InMemorySettings _settings = new();
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();

    private SettingsViewModel NewViewModel() => new(_settings, _startup, NullLogger<SettingsViewModel>.Instance);

    [Fact]
    public void Construction_ReadsTheDragSettingAndStartupStatus()
    {
        _settings.Current.DragSnappingEnabled = false;
        _startup.Status.Returns(StartupStatus.Enabled);
        var vm = NewViewModel();
        vm.DragSnappingEnabled.ShouldBeFalse();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.ShowOpenStartupApps.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    [Fact]
    public void DragSnappingEnabled_Change_PersistsImmediately()
    {
        var vm = NewViewModel();
        vm.DragSnappingEnabled = false;
        _settings.Current.DragSnappingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void LaunchAtLogin_On_EnablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered, StartupStatus.Enabled);
        var vm = NewViewModel();
        vm.LaunchAtLogin = true;
        _startup.Received(1).Enable();
        vm.LaunchAtLogin.ShouldBeTrue();
    }

    [Fact]
    public void LaunchAtLogin_Off_DisablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.Enabled, StartupStatus.NotRegistered);
        var vm = NewViewModel();
        vm.LaunchAtLogin = false;
        _startup.Received(1).Disable();
        vm.LaunchAtLogin.ShouldBeFalse();
    }

    [Fact]
    public void LaunchAtLogin_WhenTheRegistryWriteFails_RollsBackAndExplains()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered);
        _startup.When(s => s.Enable()).Do(_ => throw new UnauthorizedAccessException("denied"));
        var vm = NewViewModel();

        vm.LaunchAtLogin = true;

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldContain("denied");
        _startup.Received(1).Enable();
    }

    [Fact]
    public void DisabledInTaskManager_ShowsTheMessageAndTheButton()
    {
        _startup.Status.Returns(StartupStatus.DisabledInTaskManager);
        var vm = NewViewModel();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.ShowOpenStartupApps.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldContain("Task Manager");
    }

    [Fact]
    public void Version_IsNotEmpty()
    {
        NewViewModel().Version.ShouldNotBeNullOrWhiteSpace();
    }
}
```

`tests/Loadstone.UI.Tests/ViewModels/ShortcutRowViewModelTests.cs`:

```csharp
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public class ShortcutRowViewModelTests
{
    private readonly IHotkeyRegistrar _registrar = Substitute.For<IHotkeyRegistrar>();
    private readonly IKeyboardLayoutProbe _probe = Substitute.For<IKeyboardLayoutProbe>();
    private readonly InMemorySettings _settings = new();
    private readonly HotkeyCenter _hotkeys;

    public ShortcutRowViewModelTests()
    {
        _registrar.Register(Arg.Any<int>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        _probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        _hotkeys = new HotkeyCenter(_registrar, _settings, Substitute.For<ICommandDispatcher>(), _probe, NullLogger<HotkeyCenter>.Instance);
        _hotkeys.Start();
    }

    [Fact]
    public void Construction_ShowsTheEffectiveShortcut()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        row.Title.ShouldBe("Left Half");
        row.Shortcut.ShouldBe(WindowCommand.LeftHalf.DefaultShortcut());
        row.Status.ShouldBe("");
    }

    [Fact]
    public void SettingTheShortcut_PersistsThroughTheHotkeyCenter()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        var custom = Shortcut.Parse("Ctrl+Alt+Shift+Left");
        row.Shortcut = custom;
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("Ctrl+Alt+Shift+Left");
        _hotkeys.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(custom);
    }

    [Fact]
    public void ClearingTheShortcut_Unbinds()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        row.Shortcut = null;
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("");
    }

    [Fact]
    public void Recording_SuspendsAndResumesHotkeys()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        _registrar.ClearReceivedCalls();

        row.IsRecording = true;
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Unregister)).ShouldBe(WindowCommands.All.Count);
        _registrar.ClearReceivedCalls();

        row.IsRecording = false;
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void Status_ReportsInUse_Failed_AndLayoutWarnings()
    {
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopLeft.DefaultShortcut()).Returns(HotkeyStatus.InUse(1409));
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopRight.DefaultShortcut()).Returns(HotkeyStatus.Failed(87));
        _probe.CharacterTypedBy(WindowCommand.LeftTwoThirds.DefaultShortcut()).Returns("€");
        _hotkeys.ResetAll();

        new ShortcutRowViewModel(WindowCommand.TopLeft, _hotkeys).Status.ShouldBe("In use by another app");
        new ShortcutRowViewModel(WindowCommand.TopRight, _hotkeys).Status.ShouldBe("Could not register (error 87)");
        new ShortcutRowViewModel(WindowCommand.LeftTwoThirds, _hotkeys).Status.ShouldBe("Types € on your keyboard layout");
    }

    [Fact]
    public void ShortcutsViewModel_GroupsRowsBySection_AndRestoresDefaults()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        var vm = new ShortcutsViewModel(_hotkeys);
        vm.Groups.Select(g => g.Header).ShouldBe(["Halves", "Corners", "Thirds", "Window", "Displays"]);
        vm.Groups.Sum(g => g.Rows.Count).ShouldBe(WindowCommands.All.Count);

        vm.RestoreDefaultsCommand.Execute(null);
        _settings.Current.Shortcuts.ShouldBeEmpty();
        vm.Groups[0].Rows[0].Shortcut.ShouldBe(WindowCommand.LeftHalf.DefaultShortcut());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Loadstone.UI.Tests`
Expected: build FAILS with "The type or namespace name 'ViewModels' does not exist in the namespace 'Loadstone.UI'".

- [ ] **Step 3: Write the view models**

`src/Loadstone.UI/ViewModels/ShortcutRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;

namespace Loadstone.UI.ViewModels;

/// <summary>One row of the Shortcuts tab: a command, its recorder, and a status caption.</summary>
internal sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly HotkeyCenter _hotkeys;
    private bool _refreshing;

    public ShortcutRowViewModel(WindowCommand command, HotkeyCenter hotkeys)
    {
        Command = command;
        _hotkeys = hotkeys;
        Status = "";
        Refresh();
    }

    public WindowCommand Command { get; }

    public string Title => Command.Title();

    // Partial properties take no initializer; the constructor assigns Status before anything reads it.
    [ObservableProperty]
    public partial Shortcut? Shortcut { get; set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    /// <summary>Re-reads the effective shortcut and status from the hotkey center without writing anything back.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            Shortcut = _hotkeys.EffectiveShortcut(Command);
        }
        finally
        {
            _refreshing = false;
        }

        Status = StatusText();
    }

    partial void OnShortcutChanged(Shortcut? value)
    {
        if (_refreshing)
        {
            return;
        }

        _hotkeys.SetShortcut(Command, value);
        Status = StatusText();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            _hotkeys.Suspend();
        }
        else
        {
            _hotkeys.Resume();
        }
    }

    private string StatusText()
    {
        var status = _hotkeys.StatusOf(Command);
        return status.State switch
        {
            HotkeyState.InUse => "In use by another app",
            HotkeyState.Failed => $"Could not register (error {status.Win32Error})",
            _ => _hotkeys.LayoutWarning(Command) is { } character ? $"Types {character} on your keyboard layout" : "",
        };
    }
}
```

`src/Loadstone.UI/ViewModels/ShortcutGroupViewModel.cs`:

```csharp
namespace Loadstone.UI.ViewModels;

internal sealed record ShortcutGroupViewModel(string Header, IReadOnlyList<ShortcutRowViewModel> Rows);
```

`src/Loadstone.UI/ViewModels/ShortcutsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;

namespace Loadstone.UI.ViewModels;

internal sealed partial class ShortcutsViewModel : ObservableObject
{
    private readonly HotkeyCenter _hotkeys;

    public ShortcutsViewModel(HotkeyCenter hotkeys)
    {
        _hotkeys = hotkeys;
        Groups = WindowCommands.SectionOrder
            .Select(section => new ShortcutGroupViewModel(
                section.ToString(),
                WindowCommands.In(section).Select(command => new ShortcutRowViewModel(command, hotkeys)).ToList()))
            .ToList();
    }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _hotkeys.ResetAll();
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.Refresh();
        }
    }
}
```

`src/Loadstone.UI/ViewModels/SettingsViewModel.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.ViewModels;

/// <summary>The General tab. Every change applies immediately; there is no Save button.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _applying;

    public SettingsViewModel(ISettingsService settings, IStartupRegistration startup, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _logger = logger;
        LaunchAtLoginMessage = "";
        _applying = true;
        DragSnappingEnabled = settings.Current.DragSnappingEnabled;
        _applying = false;
        RefreshStartup();
    }

    [ObservableProperty]
    public partial bool DragSnappingEnabled { get; set; }

    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }

    [ObservableProperty]
    public partial string LaunchAtLoginMessage { get; set; }

    [ObservableProperty]
    public partial bool ShowOpenStartupApps { get; set; }

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    partial void OnDragSnappingEnabledChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        _settings.Update(s => s.DragSnappingEnabled = value);
    }

    /// <summary>
    /// The setter re-enters itself on rollback (the assignment below goes through the same
    /// generated setter), so the guard is what stops a failing Enable from recursing forever.
    /// </summary>
    partial void OnLaunchAtLoginChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            if (value)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }

            RefreshStartup();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Launch at login {Action} failed", value ? "enable" : "disable");
            LaunchAtLoginMessage = $"Could not change Launch at login: {ex.Message}";
            LaunchAtLogin = _startup.Status != StartupStatus.NotRegistered;
        }
        finally
        {
            _applying = false;
        }
    }

    [RelayCommand]
    private void OpenStartupApps() =>
        Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });

    /// <summary>Re-reads the registry; the launch-at-login state lives there, not in settings.json.</summary>
    public void RefreshStartup()
    {
        var status = _startup.Status;
        var wasApplying = _applying;
        _applying = true;
        try
        {
            LaunchAtLogin = status != StartupStatus.NotRegistered;
        }
        finally
        {
            _applying = wasApplying;
        }

        ShowOpenStartupApps = status == StartupStatus.DisabledInTaskManager;
        LaunchAtLoginMessage = status == StartupStatus.DisabledInTaskManager
            ? "Disabled in Task Manager › Startup apps."
            : "";
    }
}
```

- [ ] **Step 4: Write the window**

`src/Loadstone.UI/Views/SettingsWindow.xaml`:

```xml
<ui:FluentWindow
    x:Class="Loadstone.UI.Views.SettingsWindow"
    x:ClassModifier="internal"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:controls="clr-namespace:Loadstone.UI.Controls"
    xmlns:views="clr-namespace:Loadstone.UI.Views"
    xmlns:vm="clr-namespace:Loadstone.UI.ViewModels"
    Title="Loadstone Settings"
    Width="520"
    Height="560"
    MinWidth="480"
    MinHeight="480"
    WindowBackdropType="Mica"
    WindowCornerPreference="Round"
    WindowStartupLocation="CenterScreen"
    ExtendsContentIntoTitleBar="True">

  <ui:FluentWindow.Resources>
    <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    <views:StringToVisibilityConverter x:Key="StringToVisibilityConverter" />
  </ui:FluentWindow.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <ui:TitleBar Grid.Row="0" Title="Loadstone Settings" ShowMaximize="False" />

    <TabControl Grid.Row="1" Margin="16,8,16,16">

      <TabItem Header="General">
        <ScrollViewer VerticalScrollBarVisibility="Auto">
          <StackPanel DataContext="{Binding General}" Margin="0,12,0,0">

            <ui:Card Margin="0,0,0,12">
              <StackPanel>
                <ui:ToggleSwitch Content="Snap windows by dragging to screen edges" IsChecked="{Binding DragSnappingEnabled}" />
                <ui:TextBlock
                    Margin="0,8,0,0"
                    Appearance="Secondary"
                    FontTypography="Caption"
                    Text="Windows' own edge snapping is off while Loadstone runs."
                    Visibility="{Binding DragSnappingEnabled, Converter={StaticResource BooleanToVisibilityConverter}}" />
              </StackPanel>
            </ui:Card>

            <ui:Card Margin="0,0,0,12">
              <StackPanel>
                <ui:ToggleSwitch Content="Launch at login" IsChecked="{Binding LaunchAtLogin}" />
                <DockPanel Margin="0,8,0,0">
                  <ui:Button
                      DockPanel.Dock="Right"
                      Content="Open Startup Apps"
                      Command="{Binding OpenStartupAppsCommand}"
                      Visibility="{Binding ShowOpenStartupApps, Converter={StaticResource BooleanToVisibilityConverter}}" />
                  <ui:TextBlock
                      VerticalAlignment="Center"
                      Appearance="Secondary"
                      FontTypography="Caption"
                      TextWrapping="Wrap"
                      Text="{Binding LaunchAtLoginMessage}" />
                </DockPanel>
              </StackPanel>
            </ui:Card>

            <ui:Card>
              <StackPanel>
                <ui:TextBlock FontTypography="BodyStrong" Text="About" />
                <ui:TextBlock Margin="0,4,0,0" Text="Loadstone" />
                <ui:TextBlock Appearance="Secondary" Text="{Binding Version, StringFormat='Version {0}'}" />
              </StackPanel>
            </ui:Card>

          </StackPanel>
        </ScrollViewer>
      </TabItem>

      <TabItem Header="Shortcuts">
        <DockPanel DataContext="{Binding Shortcuts}" Margin="0,12,0,0">
          <ui:Button
              DockPanel.Dock="Bottom"
              Margin="0,12,0,0"
              HorizontalAlignment="Right"
              Content="Restore defaults"
              Command="{Binding RestoreDefaultsCommand}" />
          <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Groups}">
              <ItemsControl.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:ShortcutGroupViewModel}">
                  <StackPanel Margin="0,0,0,16">
                    <ui:TextBlock Margin="0,0,0,6" FontTypography="BodyStrong" Text="{Binding Header}" />
                    <ItemsControl ItemsSource="{Binding Rows}">
                      <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type vm:ShortcutRowViewModel}">
                          <Grid Margin="0,0,0,6">
                            <Grid.ColumnDefinitions>
                              <ColumnDefinition Width="*" />
                              <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0" VerticalAlignment="Center">
                              <ui:TextBlock Text="{Binding Title}" />
                              <ui:TextBlock
                                  Appearance="Caution"
                                  FontTypography="Caption"
                                  Text="{Binding Status}"
                                  Visibility="{Binding Status, Converter={StaticResource StringToVisibilityConverter}}" />
                            </StackPanel>
                            <controls:ShortcutRecorder
                                Grid.Column="1"
                                Margin="12,0,0,0"
                                Shortcut="{Binding Shortcut, Mode=TwoWay}"
                                IsRecording="{Binding IsRecording, Mode=TwoWay}" />
                          </Grid>
                        </DataTemplate>
                      </ItemsControl.ItemTemplate>
                    </ItemsControl>
                  </StackPanel>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>
        </DockPanel>
      </TabItem>

    </TabControl>
  </Grid>

</ui:FluentWindow>
```

`src/Loadstone.UI/Views/SettingsWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Loadstone.UI.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Loadstone.UI.Views;

/// <summary>Created once and hidden on close; shown again by the tray or a second launch.</summary>
internal partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel general, ShortcutsViewModel shortcuts)
    {
        InitializeComponent();
        General = general;
        Shortcuts = shortcuts;
        DataContext = this;
        SystemThemeWatcher.Watch(this);
    }

    public SettingsViewModel General { get; }

    public ShortcutsViewModel Shortcuts { get; }

    /// <summary>Brings the window to the front, re-reading the registry-backed state first.</summary>
    public void ShowAndActivate()
    {
        General.RefreshStartup();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

/// <summary>Visible for a non-empty string, collapsed otherwise.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

`x:ClassModifier="internal"` makes the generated half of the partial class internal to match the code-behind; without it the two halves disagree and the build fails.

- [ ] **Step 5: Run the tests and build**

Run: `dotnet test tests/Loadstone.UI.Tests` and `dotnet build Loadstone.sln`
Expected: all pass; the build is warning-free. The MVVM Toolkit's partial-property form needs C# 13 or later, which `LangVersion latest` on the .NET 10 SDK provides.

- [ ] **Step 6: Commit**

```bash
git add src/Loadstone.UI/ViewModels src/Loadstone.UI/Views tests/Loadstone.UI.Tests/ViewModels
git commit -F - <<'EOF'
Add the settings window and its view models

General: drag snapping, launch at login with Task Manager's status and
rollback on failure, About. Shortcuts: rows grouped by section with a
recorder and a status caption, and Restore defaults. The window hides
instead of closing.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 21: Application bootstrap and end-to-end smoke run

Spec section 9. Wires the host, logging, single instance, exception handlers, startup order (settings, native snap, tray, hotkeys, drag monitor), and exit. Ends with a manual run through every feature.

**Files:**
- Create: `src/Loadstone.UI/DependencyInjection/AppModule.cs`, `src/Loadstone.UI/Services/LoggingSetup.cs`, `src/Loadstone.UI/Services/ShellActions.cs`
- Modify: `src/Loadstone.UI/App.xaml.cs` (replace the placeholder body)

**Interfaces:**
- Consumes: everything above.
- Produces: `internal sealed class ShellActions : IShellActions` with settable `Action OpenSettings` and `Action Quit` delegates; `internal static class LoggingSetup { static string LogDirectory; static LoggingConfiguration Build(); }`; `AppModule : Autofac.Module`.

- [ ] **Step 1: Write the DI module, logging setup, and shell actions**

`src/Loadstone.UI/Services/ShellActions.cs`:

```csharp
using Loadstone.UI.Tray;

namespace Loadstone.UI.Services;

/// <summary>The tray's requests to the application, filled in by App once the host is built.</summary>
internal sealed class ShellActions : IShellActions
{
    public Action OpenSettingsAction { get; set; } = () => { };

    public Action QuitAction { get; set; } = () => { };

    public void OpenSettings() => OpenSettingsAction();

    public void Quit() => QuitAction();
}
```

`src/Loadstone.UI/Services/LoggingSetup.cs`:

```csharp
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace Loadstone.UI.Services;

internal static class LoggingSetup
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "Logs");

    /// <summary>Daily files, 14 kept, 10 MB archive threshold, written asynchronously.</summary>
    public static LoggingConfiguration Build()
    {
        var config = new LoggingConfiguration();
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(LogDirectory, "Loadstone-${shortdate}.log"),
            Layout = "${longdate} ${level:uppercase=true} ${logger:shortName=true} ${message} ${exception:format=tostring}",
            ArchiveAboveSize = 10_000_000,
            MaxArchiveFiles = 14,
        };
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, new AsyncTargetWrapper(file));
        return config;
    }
}
```

`src/Loadstone.UI/DependencyInjection/AppModule.cs`:

```csharp
using Autofac;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Loadstone.UI.Services;
using Loadstone.UI.Tray;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Views;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.DependencyInjection;

public sealed class AppModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Core
        builder.Register(c => new SettingsService(SettingsService.DefaultPath, c.Resolve<ILogger<SettingsService>>()))
            .As<ISettingsService>().SingleInstance();
        builder.RegisterType<SystemParametersNativeSnapSettings>().As<INativeSnapSettings>().SingleInstance();
        builder.RegisterType<NativeSnap>().AsSelf().SingleInstance();
        builder.RegisterType<RegistryStartupRegistration>().As<IStartupRegistration>().AsSelf().SingleInstance();
        builder.Register(_ => new WindowDirector(Displays.Snapshot, WindowLiveness.IsAlive)).AsSelf().SingleInstance();

        // Input
        builder.RegisterType<CommandDispatcher>().As<ICommandDispatcher>().SingleInstance();
        builder.RegisterType<HotkeyRegistrar>().As<IHotkeyRegistrar>().SingleInstance();
        builder.RegisterType<KeyboardLayoutProbe>().As<IKeyboardLayoutProbe>().SingleInstance();
        builder.RegisterType<HotkeyCenter>().AsSelf().SingleInstance();
        builder.RegisterType<WinEventMoveSizeEvents>().As<IMoveSizeEvents>().SingleInstance();
        builder.RegisterType<SnapPreview>().As<ISnapPreview>().SingleInstance();
        builder.RegisterType<DragSnapMonitor>().AsSelf().SingleInstance();

        // Shell
        builder.RegisterType<ShellActions>().As<IShellActions>().AsSelf().SingleInstance();
        builder.RegisterType<TrayController>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutsViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsWindow>().AsSelf().SingleInstance();
    }
}
```

Every registered class is `internal` with a `public` constructor, which is what Autofac's reflection-based `RegisterType` needs. If Autofac reports "No constructors on type ... can be found with the constructor finder" for one of them, that class has a non-public constructor; make it public rather than changing the registration.

- [ ] **Step 2: Replace the App code-behind**

`src/Loadstone.UI/App.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Input;
using Loadstone.UI.Services;
using Loadstone.UI.Tray;
using Loadstone.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Wpf.Ui.Appearance;

namespace Loadstone.UI;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _mutex;
    private IDisposable? _showSettingsListener;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = SingleInstance.TryAcquire();
        if (_mutex is null)
        {
            // Another Loadstone is running: ask it to show Settings and leave.
            SingleInstance.SignalShowSettings();
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(builder => builder.RegisterModule<AppModule>())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddNLog(LoggingSetup.Build());
            })
            .Build();
        _host.Start();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        InstallExceptionHandlers();
        ApplicationThemeManager.ApplySystemTheme();

        var services = _host.Services;
        var settings = services.GetRequiredService<ISettingsService>();
        var nativeSnap = services.GetRequiredService<NativeSnap>();
        var shell = services.GetRequiredService<ShellActions>();
        shell.OpenSettingsAction = ShowSettings;
        shell.QuitAction = Shutdown;

        // Order matters, as on the Mac: native snap off before the first drag can happen, monitors last.
        nativeSnap.Apply(settings.Current.DragSnappingEnabled);
        settings.Changed += (_, _) => nativeSnap.Apply(settings.Current.DragSnappingEnabled);
        services.GetRequiredService<RegistryStartupRegistration>().RefreshPathIfStale();
        services.GetRequiredService<TrayController>();
        services.GetRequiredService<HotkeyCenter>().Start();
        var drag = services.GetRequiredService<DragSnapMonitor>();
        drag.Start();
        _showSettingsListener = SingleInstance.ListenForShowSettings(() => Dispatcher.BeginInvoke(ShowSettings));

        _logger.LogInformation(
            "launched: version {Version} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed} startup={Startup}",
            typeof(App).Assembly.GetName().Version,
            settings.Current.DragSnappingEnabled,
            drag.IsMonitoring,
            nativeSnap.IsSuppressing,
            services.GetRequiredService<IStartupRegistration>().Status);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var services = _host.Services;
            services.GetRequiredService<DragSnapMonitor>().Stop();
            services.GetRequiredService<HotkeyCenter>().Dispose();
            services.GetRequiredService<IHotkeyRegistrar>().Dispose();
            services.GetRequiredService<NativeSnap>().Restore();
            services.GetRequiredService<TrayController>().Dispose();
            _showSettingsListener?.Dispose();
            _logger?.LogInformation("exiting");
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        NLog.LogManager.Shutdown();
        base.OnExit(e);
    }

    private void ShowSettings() => _host?.Services.GetRequiredService<SettingsWindow>().ShowAndActivate();

    /// <summary>Log as fatal, give Windows its edge snapping back on a best-effort basis, and let the process end.</summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.Exception, "unhandled exception on the UI thread");
            RestoreNativeSnapQuietly();
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.ExceptionObject as Exception, "unhandled exception");
            RestoreNativeSnapQuietly();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "unobserved task exception");
            args.SetObserved();
        };
    }

    private void RestoreNativeSnapQuietly()
    {
        try
        {
            _host?.Services.GetService<NativeSnap>()?.Restore();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "could not restore Windows edge snapping while crashing");
        }
    }
}
```

- [ ] **Step 3: Build and run the full test suite**

Run: `dotnet build Loadstone.sln` and `dotnet test Loadstone.sln`
Expected: warning-free build; every test passes.

- [ ] **Step 4: Smoke run every feature by hand**

Run: `dotnet run --project src/Loadstone.UI`

Work through this list and note anything that misbehaves; fix it before committing:

1. A tray icon appears with a two-pane glyph matching the taskbar theme. Hovering shows "Loadstone".
2. Right-click shows the menu: 18 commands in five groups with their chords, then Settings…, Loadstone Help, Quit Loadstone.
3. Focus Notepad or a browser. Ctrl+Alt+Left pins it to the left half with no gap at the screen edge. Ctrl+Alt+U, I, J, K give the four corners. Ctrl+Alt+D, F, G give thirds; E and T give two-thirds. Ctrl+Alt+Enter fills the work area. Ctrl+Alt+C centers without resizing. Ctrl+Alt+Backspace puts the window back where it was before the first command; a second press beeps.
4. With two displays, Ctrl+Alt+Win+Right moves the window to the next display keeping its relative frame; on a single display it beeps.
5. Drag a window by its title bar toward the left edge: the blue preview appears once the window has moved and the pointer reaches the band; release snaps it to the left half. Corners give quarters, the top edge maximizes, the bottom edge gives thirds. Windows' own snap preview does not appear. Select text in an editor by dragging to the edge: nothing snaps. Resize a window by its edge past the screen edge: nothing snaps. Press Escape mid-drag: the window returns and nothing snaps.
6. Left-click the tray icon: Settings opens. Toggle "Snap windows by dragging to screen edges" off: drag-to-edge does nothing and Windows' own snapping is back (drag to the left edge and see Windows' preview). Toggle it on again.
7. Shortcuts tab: click a recorder, press Ctrl+Alt+Shift+Left, it commits and the tray menu shows the new chord; the hotkey works. Press the recorder's clear button: the row reads "Not set" and the hotkey no longer fires. Restore defaults brings everything back. On a Norwegian layout, the Left Two Thirds row reads "Types € on your keyboard layout".
8. Launch at login on: `Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'` shows the quoted exe path. Off removes it.
9. Close Settings with the X: the app keeps running. Run `dotnet run --project src/Loadstone.UI` again while it runs: the second process exits and Settings comes to the front.
10. Quit from the tray. `Get-ItemProperty 'HKCU:\Control Panel\Desktop' | Select DockMoving, SnapSizing` shows both back at 1. `%LOCALAPPDATA%\Loadstone\settings.json` has no `nativeSnapPreviousValues`. The log under `%LOCALAPPDATA%\Loadstone\Logs` shows the startup summary, one line per hotkey binding, and "exiting".
11. Kill the process from Task Manager while running (both flags are 0), then start it again: the log says it restored the flags left by an unclean exit, and after a clean quit both are 1 again.

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI
git commit -F - <<'EOF'
Wire the application: host, logging, single instance, tray, hotkeys, drag

Startup follows the Mac's order (settings, native snap, tray, hotkeys,
drag monitor); exit unregisters everything and restores Windows' edge
snapping. A second launch opens Settings in the running instance.
Unhandled exceptions log as fatal and restore the flags best-effort.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

---

### Task 22: README, diagrams, CLAUDE.md, license, and CI verification

Spec section 13 and 11. The README follows the Mac README's structure with Windows content; the diagrams come from the Mac repo with their labels adjusted; `CLAUDE.md` carries the invariants and the spike's results.

**Files:**
- Create: `README.md`, `CLAUDE.md`, `LICENSE`, `docs/layouts.svg`, `docs/snap-zones.svg`

- [ ] **Step 1: Copy and adjust the diagrams**

Run from the repository root (bash):

```bash
cp /c/code/projects/loadstone/docs/snap-zones.svg docs/snap-zones.svg
cp /c/code/projects/loadstone/docs/layouts.svg docs/layouts.svg
sed -i 's/⌃⌥⌫/Ctrl+Alt+Backspace/g; s/⌃⌥/Ctrl+Alt+/g' docs/layouts.svg
sed -i 's/16 pt deep and corners are 140 pt squares/16 px deep and corners are 140 px squares at 100% scaling/' docs/snap-zones.svg
grep -c 'Ctrl+Alt+' docs/layouts.svg
```

Expected: the last command prints a count of at least 12. Open both files in a browser and confirm the labels fit inside their boxes; if `Ctrl+Alt+←` overflows a box in `layouts.svg`, reduce that text element's `font-size` attribute by 2.

- [ ] **Step 2: Write the license**

`LICENSE`:

```
MIT License

Copyright (c) 2026 Morten Brudvik

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 3: Write the README**

`README.md`:

```markdown
# Loadstone for Windows

<p align="center">
  <strong>Snap any window into place.</strong><br>
  Halves, corners, and thirds — from the keyboard or by dragging to an edge.<br>
  A native tray app. No browser engine, no background service.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-blue" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-purple" alt=".NET 10">
  <img src="https://img.shields.io/badge/status-early-blue" alt="Early">
</p>

Loadstone sits in the tray and stays out of the way. When you need two documents side by side, a browser in a third, or a window parked in a corner, it moves the focused window to a precise tile of the display's work area — the taskbar is left uncovered. It is the Windows port of the [macOS Loadstone](https://github.com/mortenbrudvik/loadstone), with the same commands and the same defaults.

<p align="center">
  <img src="docs/layouts.svg" alt="Halves, corner quarters, and thirds with their default shortcuts" width="960">
</p>

## Features

- **Halves** — left, right, top, bottom
- **Corners** — four quarters
- **Thirds** — left, center, right, plus two-thirds
- **Maximize, center, restore** — restore puts the window back where it was before Loadstone first moved it
- **Drag to snap** — edges for halves, corners for quarters, top to maximize, bottom for thirds. Only a window that is actually being moved snaps; selecting text or resizing near an edge does nothing
- **Multi-display** — send a window to the next or previous display, walking displays left to right as they sit on the desk, with the window's size scaled for the display's DPI
- **Custom shortcuts** — Magnet-style defaults, all editable, with a warning when a chord is taken by another app or types a character on your keyboard layout
- **Launch at login** — optional, from Settings

Loadstone turns off Windows' own drag-to-edge snapping while its drag snapping is on, so the two don't fight, and turns it back on when it quits or when you switch drag snapping off. If Loadstone is killed before it can put the setting back, the next launch notices and restores it. Win+Arrow and Snap Layouts are untouched.

## Default shortcuts

All of these are Ctrl+Alt unless noted. Change them in **Settings → Shortcuts**.

| Layout | Shortcut |
| --- | --- |
| Left / right / top / bottom half | `Ctrl+Alt+←` `Ctrl+Alt+→` `Ctrl+Alt+↑` `Ctrl+Alt+↓` |
| Top left / top right corner | `Ctrl+Alt+U` `Ctrl+Alt+I` |
| Bottom left / bottom right corner | `Ctrl+Alt+J` `Ctrl+Alt+K` |
| Left / center / right third | `Ctrl+Alt+D` `Ctrl+Alt+F` `Ctrl+Alt+G` |
| Left / right two thirds | `Ctrl+Alt+E` `Ctrl+Alt+T` |
| Maximize | `Ctrl+Alt+Enter` |
| Center (keeps the current size) | `Ctrl+Alt+C` |
| Restore | `Ctrl+Alt+Backspace` |
| Next / previous display | `Ctrl+Alt+Win+→` `Ctrl+Alt+Win+←` |

The corner keys are a square on the keyboard:

```
U  I
J  K
```

On Norwegian, German, and most other European layouts, AltGr is Ctrl+Alt to Windows, so `Ctrl+Alt+E` also captures the `€` key while Loadstone runs. Settings shows "Types € on your keyboard layout" next to any such binding; rebind it if you type that character.

## Drag to snap

Drag a window by its title bar until the pointer reaches an edge or corner of the display. A blue preview shows the tile; let go to snap. Only a window that is actually moving snaps, so selecting text or dragging a slider to an edge does nothing.

<p align="center">
  <img src="docs/snap-zones.svg" alt="Drag zones: corners give quarters, the sides give halves, the top edge maximizes, the bottom edge gives thirds; on a portrait display the sides give corner, vertical third, corner and the bottom edge gives halves" width="960">
</p>

- **Corners** — a quarter. The corner zones are 140 px squares (scaled with the display) and win over the edges.
- **Left and right edges** — a half.
- **Top edge** — maximize.
- **Bottom edge** — a third, chosen by where along the edge you let go.
- **Portrait displays** — the sides give corner, vertical third, corner along their length, and the bottom edge gives halves.
- **More than one display** — every display has its own zones, including the edge it shares with a neighbor. The pointer does not stop at a shared edge the way it does at an outer one, so slow down there and let go while the preview is showing. The window snaps onto the display the pointer is on.

The edge zones are 16 px deep at 100% scaling and extend the same distance past an outer edge, so overshooting still counts.

## Install

Download `Loadstone-<version>-Setup.exe` from the [latest release](https://github.com/mortenbrudvik/loadstone-win/releases/latest) and run it. It installs for the current user, no administrator rights needed, and adds a Start Menu entry. Windows 10 or 11, x64.

Optional: **Settings → Launch at login**.

### Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/mortenbrudvik/loadstone-win.git
cd loadstone-win
dotnet build Loadstone.sln
dotnet run --project src/Loadstone.UI
```

## Troubleshooting

Tray → Loadstone → **Loadstone Help** opens this page.

**One window will not move.** Windows of programs running as administrator cannot be moved by a program that isn't, and some windows are fixed-size. Loadstone beeps and logs the refusal; everything else keeps working. Fixed-size windows are centered in the tile instead of resized.

**A shortcut is ignored.** Another app owns that key combination; the Shortcuts tab says "In use by another app" next to it. Pick a different one. If the row says "Types € on your keyboard layout", the chord doubles as an AltGr character on your layout and captures it while Loadstone runs; rebind if you need the character.

**Dragging to an edge does not snap.** Drag by the title bar; the window has to move before Loadstone treats the gesture as a window drag. On the edge between two displays the pointer passes straight through, so slow down and release while the preview is showing. If Windows' own Snap Layouts flyout appears at the top edge, turn it off under Settings → System → Multitasking → Snap windows.

**Restore does nothing.** Restore is one-shot: it puts the window back where it was before Loadstone first moved it, then forgets. The next Loadstone command starts a new memory. The memory is also dropped when the window closes or Loadstone restarts.

**Next / previous display beeps.** Only one display is attached.

**Windows' own edge snapping is off after Loadstone crashed or was killed.** Loadstone turns it off while its drag snapping is on and back on when it quits normally; after an unclean exit, simply launching Loadstone again restores it. To put it back by hand, use Settings → System → Multitasking → Snap windows (`ms-settings:multitasking`).

**Launch at login is on but Loadstone does not start.** Task Manager → Startup apps may have it disabled; Settings shows "Disabled in Task Manager" with a button to open that page.

**Reading the log.** Loadstone writes daily logs to `%LOCALAPPDATA%\Loadstone\Logs`. The startup summary lists the effective binding and status of every shortcut.

## Build and test

```powershell
dotnet build Loadstone.sln
dotnet test Loadstone.sln
```

## How it works

Loadstone is a tray-only WPF app. Shortcuts and drag gestures go to a small window director that:

1. Finds the window (the foreground window, or the one being dragged)
2. Reads its visible frame, which excludes Windows' invisible resize borders
3. Lays out a tile against the display's **work area**
4. Writes the frame, then writes it once more if the window resized itself on a display with a different DPI
5. Reports what the window answered: a refused write makes Loadstone beep and log

Drag snapping listens to Windows' own move-loop events instead of the mouse, so a text selection or a slider dragged to a screen edge is left alone, and the snap is applied after Windows has finished the drop.

## Name

A lodestone is a naturally magnetic rock. Loadstone is the window magnet.

## License

[MIT](LICENSE) © 2026 Morten Brudvik.
```

- [ ] **Step 4: Write CLAUDE.md**

`CLAUDE.md`:

```markdown
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Loadstone for Windows is a tray-only WPF window tiler (.NET 10, WPF-UI, Win32 through `LibraryImport`). It tiles the focused window into halves, corners, and thirds from global hotkeys or by dragging a window to a screen edge. It is the Windows port of the macOS Loadstone at `C:\code\projects\loadstone`, which remains the authority on behavior: same command ids, same tile math, same drag zones, same one-shot Restore. Architecture: **Pure WPF** (see `docs/superpowers/specs/2026-09-07-loadstone-win-design.md`).

## Commands

```powershell
dotnet build Loadstone.sln                      # warnings are errors
dotnet test Loadstone.sln
dotnet test tests/Loadstone.Core.Tests
dotnet test tests/Loadstone.UI.Tests --filter "FullyQualifiedName~Win32WindowTests"
dotnet run --project src/Loadstone.UI
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1   # regenerate the .ico assets
```

The 32-bit `pwsh` is first on PATH on the development machine; scripts that touch Win32 must run under the 64-bit one above. Real-window tests run under `[WpfFact]` and need a desktop session; they run on GitHub's windows-latest runner.

Reading the app's log: `%LOCALAPPDATA%\Loadstone\Logs\Loadstone-<date>.log`. Settings: `%LOCALAPPDATA%\Loadstone\settings.json`.

## Architecture

Data flows one direction: input → `CommandDispatcher` / `DragSnapMonitor` → `WindowDirector` → `Win32Window` → `SetWindowPos`.

- **Core (`src/Loadstone.Core`)** has no Win32 and no WPF. `Tile.Frame` floors the left and top pieces and gives the remainder to the right and bottom ones so complementary tiles meet on odd sizes. `SnapZones` maps the cursor against the *full* monitor frame (tiles are laid into the *work area*) and scales the 16 px bands and 140 px corners with the display's DPI. `ScreenGeometry.Neighbor` walks displays in reading order (x, then y). `WindowDirector` keeps the restore ledger: written only after a window accepts a frame, one-shot Restore, dead identities pruned before every command. `DragSnapTracker` arms once the dragged window's origin changes with its size unchanged, disarms on a resize, and commits at the end unless the frame is back at its baseline (Escape). `NativeSnap` captures Windows' edge-snapping flags once, parks them in settings as a crash marker, and restores them; recovery runs before capture.
- **UI (`src/Loadstone.UI`)**. `Interop/` holds every Win32 declaration. `Win32Window` is the only type that knows about invisible borders and DPI: `Frame` is the DWM extended frame bounds, `SetFrame` restores a maximized or minimized window first, expands the target by the border insets, and writes once more when the frame lands off target after a DPI change. `HotkeyRegistrar` uses `RegisterHotKey` on a message-only window; `HotkeyCenter` resolves the effective chord per command and records in-use and keyboard-layout warnings. `WinEventMoveSizeEvents` hooks `EVENT_SYSTEM_MOVESIZESTART`/`END`; `DragSnapMonitor` samples the cursor at 60 Hz during a drag and commits after the OS finishes the drop. `SnapPreview` asserts topmost from its own process on every show. `TrayController` captures the foreground window before the menu opens.

### Invariants worth knowing before editing

- `WindowCommand` is the single source of truth for hotkeys, the tray menu, and the Shortcuts tab. Its ids (`leftHalf` … `previousDisplay`) are settings keys and match the Mac app; never rename one. `WindowCommandTests` pins the list. Switch expressions over `Tile` and `WindowCommand` have no discard arm on purpose, and CS8524 is suppressed so a new member fails the build until every switch covers it.
- Coordinates are physical pixels, y down, everywhere. The app manifest declares Per-Monitor V2; the test host opts in through a module initializer.
- `SystemParametersInfo` for `SPI_SETDOCKMOVING` and `SPI_SETSNAPSIZING` takes the BOOL in `uiParam` with `pvParam` zero. Passing it in `pvParam` silently writes FALSE (found the hard way on 2026-09-07).
- A background process cannot make another process's window topmost, but can make its own window topmost once it exists. The preview relies on the second fact; never ask another process's window to change z-order.
- Test fixtures use 1201 by 901 and negative-origin displays on purpose; even sizes hide rounding bugs.
- Every test that creates a window runs under `[WpfFact]`. Tests never touch the real Run key (they use a key under `Software\LoadstoneTests`), the real settings file (temp directories), or change the snap flags (they write back the value they read).

### Spike results (2026-09-07, Windows 11 25H2 build 26200)

With `DockMoving` and `SnapSizing` off, a window dragged to the left edge, a corner, or the top edge is left where it was dropped; with them on it snaps to the half, the quarter, or maximizes. The Snap Layouts flyout could not be observed (off on that machine) and is documented in the README instead of managed.
```

- [ ] **Step 5: Verify the CI commands locally and commit**

Run the workflow's steps locally:

```bash
dotnet restore Loadstone.sln
dotnet build Loadstone.sln -c Release --no-restore
dotnet test Loadstone.sln -c Release --no-build --logger "trx" --results-directory TestResults
```

Expected: build warning-free, every test passes, a `.trx` file lands in `TestResults/` (which is gitignored).

```bash
git add README.md CLAUDE.md LICENSE docs/layouts.svg docs/snap-zones.svg
git commit -F - <<'EOF'
Add the README, diagrams, CLAUDE.md, and license

The README follows the Mac README with Windows chords, install, and
troubleshooting. CLAUDE.md carries the invariants and the spike results.
The diagrams are the Mac's with Windows labels.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015cpR7JhV1NSxMBUCTFLYNQ
EOF
```

The first push to GitHub runs the workflow; if the hosted runner cannot create windows, add an environment check to `TestWindow` that skips real-window tests when `GITHUB_ACTIONS` is set, as the Mac skips its Accessibility tests, and note it in `CLAUDE.md`. Creating the GitHub repository and pushing are the user's call. The installer is the next plan, through the wpf-installer skill.

---

## Plan self-review

- **Spec coverage.** 1.1 parity: Tasks 2 to 8 (commands, tiles, restore, drag), 12 (placement), 15 (shortcuts), 17 (launch at login, native snap), 16 (drag preview), 3 and 7 (displays). 1.2 additions: Task 14 and 15 (in-use, layout warning), 17 and 21 (second launch opens Settings), 17 and 20 (Task Manager status). 3.1 and 3.2: Task 1. 4.x Core: Tasks 2 to 10. 5.x: Tasks 11 to 13. 6.x: Tasks 14 to 16. 7.x: Tasks 10 and 17. 8.x: Tasks 18 to 20. 9: Task 21. 10: every task's tests; 10.4 was run during planning and is recorded in Task 22's CLAUDE.md. 11: Tasks 1 and 22. 12 (installer): a later plan, by design. 13: Task 22.
- **Placeholders.** None: every step carries its code, command, or expected output. Two steps name a fallback (STA attribute, StaticResource converters) with the exact alternative code.
- **Type consistency.** `KeyModifiers` (Task 5) is used by Tasks 6, 14, 15, 19. `Shortcut.DisplayText` (Task 5) by Tasks 18 and 19. `CommandOutcome`/`OutcomeKind` (Task 7) by Tasks 12, 13, 16. `WindowCommands.In`/`SectionOrder` (Task 6) by Tasks 18 and 20. `ICommandDispatcher.Run(command, hwnd)` (Task 13) by Tasks 15 and 18. `HotkeyCenter.EffectiveShortcut/StatusOf/LayoutWarning/SetShortcut/ResetAll/Suspend/Resume/BindingsChanged` (Task 15) by Tasks 18 and 20. `IMoveSizeEvents.Install() → bool` (Task 16) matches the fake in its tests. `NativeSnap.Apply/Restore/IsSuppressing` (Task 10) by Task 21. `RegistryStartupRegistration.RefreshPathIfStale` (Task 17) by Task 21. `SettingsWindow.ShowAndActivate` (Task 20) by Task 21. `ShellActions.OpenSettingsAction/QuitAction` (Task 21) implement `IShellActions` (Task 18).
