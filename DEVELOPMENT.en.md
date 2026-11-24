# Development and Build

[Documentation home](README.en.md) · [简体中文](DEVELOPMENT.md) · **English**

Version 0.9.5, Windows 11 x64 portable edition. The release directory contains product source, test source, offline data, and the executable. Development and testing use a Dell G15 5520, Windows 11 Pro build 26300.

## Building from Source

Run the following commands in the project directory, using the 64-bit C# compiler included with Windows .NET Framework. Output defaults to bin and does not overwrite the EXE shipped with the package.

```powershell
.\build.ps1 -Test
.\bin\WindowsGamepadDesktop.exe --settings
```

Runtime requires the system .NET Framework 4.8 family, UI Automation, and XInput 1.4. The development machine has multiple modern .NET runtimes and no dotnet SDK installed. The main build path does not require downloading the SDK, NuGet packages, or npm packages. `build.ps1 -OutDir <directory>` can specify a separate output. Keep the data directory alongside the exe. Compilation targets x64, includes the PerMonitorV2 manifest, and treats warnings as errors.

Startup arguments include `--settings`, `--tray`, and `--exit`. Repeating a normal launch within the same session requests that the existing instance show settings. Repeated `--tray` launches return silently. `--exit` requests normal cleanup of the existing instance through the message window.

## Technical Choices

The reference source review is pinned to commit `e428606bea52104ad0b8484e5170fbc06fdf0ded`. The reference project is MIT-licensed and uses .NET Framework 4.8, WinForms, WebView2, and System.Text.Json, with a React/Vite settings frontend. Controller.cs uses Raw Input and activates the first device with activity. KeyboardAssist.cs has UIA detection and touch keyboard switching. Games.cs has process-identification logic. And InputEngine.cs additionally handles full screen. Its approach of separating the input engine from system integration is a valuable reference. This project requires an explicitly selected XInput primary controller, per-device configuration, a controllable B-confirm keyboard, and per-application switching, so an independent implementation was chosen. This version uses no source files, icons, or UI assets from the reference project. [Reference project source](https://github.com/DoobDewd/Desktop-Gamepad/tree/e428606bea52104ad0b8484e5170fbc06fdf0ded)

Windows 11 has a native touch keyboard with gamepad navigation, X to delete, and Y for space. Active display of the system InputPane is best effort and depends on the application view, hardware keyboard, and system policy. Public interfaces cannot provide the forced invocation in arbitrary third-party windows, B confirmation, and selected-cell control required by this project. The app uses its own non-activating keyboard. Ordinary text uses Unicode SendInput, system IME layouts use virtual keys, and offline Pinyin uses a replaceable dictionary. The touch keyboard component was checked on this machine. Native layout cross-application compatibility has not been regarded as verified on real hardware. [Microsoft Gamepad layout description](https://support.microsoft.com/en-us/servicing/os/windows-11/2025/03/march-25-2025-kb5053657-os-builds-22621-5126-and-22631-5126-preview) · [InputPane interface](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.inputpane)

## Stage Record

| Stage | Build and actual checks |
|---|---|
| 1 Basic framework | First build of the application and test program; 4 checks passed. The first read of all four slots showed not connected. A BETOP XInput device was later detected. Its Windows bus property was BTP-KP20 XINPUT. |
| 2 Desktop control | 13 cumulative checks passed, covering chord timing, drag references, switching, disconnect release, recentering, and configuration read/write. |
| 3 On-screen keyboard | 19 cumulative checks passed, verifying everything from cell navigation to Chinese and English submission, integration into the tray main loop, and real SendInput. |
| 4 Focus detection | 23 cumulative checks passed. Editing, password, read-only, focus loss, and manual fallback were then verified against real Windows application windows. |
| 5 Chinese and display | Verified Simplified Chinese conversion, dictionary-entry candidates, character delivery to target windows, negative-coordinate monitors, and layout at multiple resolutions. During regression testing, an occasional candidate exception caused by the Win32 string termination length was fixed. |
| 6 System integration | Completed settings, primary controller and named profiles, startup-at-sign-in toggle, foreground rules, and emergency suspend. Opened all settings pages and checked keyboard rendering in practice. |
| 7 Release verification | 39 core checks passed, plus cross-process window, Windows Search, File Explorer, Edge, and VS Code tests. Fixed the first-click timeout caused by cold registration of Windows Search. Event registration and fast sampling now run on separate threads. Performed extraction and startup checks on the portable package. See TESTING.en.md for details. |
| 8 Flydigi-specific work | Delivered only the capability model, extension interfaces, simulated-axis validation, and integration documentation, pending physical hardware and protocol evidence. |

The reference repository's file-location list was extracted read-only by DeepSeek Harness, and the coordinator reviewed the license, project files, and concrete implementation. The headless configuration declared the model deepseek-flash. Run events included thinking output and did not provide a named thinking-effort level.

## 0.9.5

The keyboard opens automatically after a completed click inside an editable control or an explicit focus shortcut. Dragging, canceled clicks, and indirect focus changes during browser tab navigation leave the keyboard closed. The mode chord shows a release prompt as soon as it reaches two seconds.

## 0.9.4

Pinyin mode generates multi-character and whole-sentence candidates from whole-word and segmented matches. When accepting a partial candidate, the Pinyin not yet converted is retained. The keyboard uses rounded keycaps, a dark gradient panel, and an independent candidate preview, retaining the existing non-activating window and gamepad operation.

## 0.9.3

The mode chord is changed to release after holding for 2 full seconds to switch, and holding for 3 full seconds to lock or unlock. Locking applies uniformly to manual and application rules, and the key gesture does not depend on whether the window is full screen.

## 0.9.2

Fixed the left mouse button failing to release after a gamepad click on a native title-bar button. Input polling continues during mouse tracking in the Windows title bar, and reopening settings from the tray restores the minimized window.

## 0.9.1

Documented the physical buttons, sticks, triggers, and actual mode switching of the BETOP Kunpeng 20. Fixed the issue where desktop Esc triggered the automatic keyboard in the original edit box. After adding regressions, 41 core checks, 28 Windows integration checks, and a Windows Search retest passed.

## Extension and Regression

Device adaptation starts with Controllers.cs, and platform output is implemented only in NativeInput.cs. Mapping problems should first be reproduced with a simulated Provider, then SendInput should be tested in a real foreground window. UIA queries must remain in the background and use properties that do not contain text content. Different interactive tests contend for the foreground, system hotkeys, and the mouse, so they should be run in the order given in TESTING.en.md.
