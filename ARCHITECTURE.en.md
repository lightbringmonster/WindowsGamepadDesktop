# Architecture

[Documentation home](README.en.md) · [简体中文](ARCHITECTURE.md) · **English**

The application uses C#, the .NET Framework 4.8 family, and WinForms. The keyboard is rendered according to the target screen's physical pixels, using a PerMonitorV2 manifest. WinForms is suitable for the tray message window, non-activating top-level windows, and local offline builds. The product has no browser engine, Node process, or NuGet runtime dependency.

| Layer | Source | Responsibility |
|---|---|---|
| Device | src/Controllers.cs | Provider, device instances, capabilities, normalized snapshots, deduplication, and primary-controller selection. XInput slot scanning and battery caching |
| Configuration | src/Configuration.cs | Desktop and keyboard bindings, named-profile association, validation, version conversion, and atomic JSON replacement |
| Modes and mapping | src/ApplicationProfiles.cs, src/MappingEngine.cs | Foreground rules, manual override, chord timing, trigger hysteresis, recentering, and key repeat |
| Pointer and scrolling | src/PointerWorker.cs, src/MappingEngine.cs | Background primary-device sampling, foreground checks, curves, and displacement accumulation |
| Desktop quick actions | src/DesktopGestures.cs, src/RadialMenuWindow.cs | Sector hysteresis, shortcut commands, and a non-activating radial menu |
| System output | src/NativeInput.cs | IInputSink, SendInput structures, and registration and release of simulated keys currently held down |
| Focus | src/FocusDetection.cs | UIA focus-change notifications, on-demand checks, editability, screen bounds, and debouncing |
| On-screen keyboard | src/Keyboard.cs, src/PinyinDictionary.cs | Model navigation, offline Pinyin segmentation and candidate ranking, system IME key codes, non-activating window, monitor handling, and avoidance |
| System integration | src/Host.cs, src/SystemIntegration.cs | Message loop, tray, startup at sign-in, system hotkeys, physical input activity, session locking, and short-lived overlays |
| Settings | src/SettingsWindow.cs | Configuration draft, capability-driven grid generation, live device status, and import/export |
| Entry point | src/Program.cs | Single instance per current logon session, settings loading, normal exit, and suspend on exception |

## Each Input Sampling Cycle

1. The main thread reads the foreground HWND and queries the process file name when the window changes. Mode changes produced by rules take effect before this round's simulated input. User manual overrides are retained within the same rule. When unlocked, leaving the matching process restores the desktop. While locked, foreground matching rules continue to be tracked, and automatic and manual switching share the mode-lock check. The current mode is retained when unlocking.
2. A dedicated thread samples the selected controller and outputs stick motion and scrolling at a target interval of 8 ms. It uses a Windows high-resolution waitable timer when available. Motion requires approval from the UI thread for the current desktop-mapping context. A foreground window change or disconnect immediately stops motion. The worker waits while there is no connected primary controller.
3. The message window still receives SendNotifyMessage requests at a target interval of 15 ms and processes button mappings and gestures from the newest snapshot. Cross-thread messages can deliver simulated mouse releases during title-bar capture, where Windows can filter WM_TIMER. Other slots are scanned about every 850 ms, and the message timer drops to 250 ms with no primary device. Connected spare devices are also refreshed while settings are open. The button mapping uses 0.52 / 0.32 trigger thresholds for press and release. The worker computes velocity from the radial dead zone and curve exponent, accumulates fractional displacement, and emits complete wheel steps.
4. Chord gestures are sampled independently in each control mode and in the keyboard overlay. Releasing between 2 and 3 seconds requests a switch. Holding for 3 seconds toggles the lock state. The next gesture is allowed only after both keys are released. Native mode continues to detect the switch chord and the lock gesture. When the keyboard is visible, the keyboard mapping table is used. Desktop mode compares the required keys and mouse buttons with the state actually emitted in the previous round. The same output can be held by multiple gamepad keys. Releases are performed in reverse order, and if SendInput rejects a release, the record is retained for retry in the next round.
5. The UI thread interprets precision presses and passes the current gear to the pointer worker. Standard uses the configured curve. Fine and ultra-fine each use their own dead zone and curve. Opening the quick actions wheel suspends desktop output. Releasing the wheel binding confirms the highlighted action only if its target is still foreground.
6. Mode changes, device switches, disconnects, suspension, session locking, and shutdown trigger release and recentering. The application does not call XInputSetState. Games continue to manage rumble.

## Input Fields and Focus

A completed simulated left click or an explicit focus shortcut starts an eligibility window of about 930 ms. The first sample waits about 85 ms for the target to update focus. A click must land within the focused editable control. For a handoff from Start to Windows Search, the host verifies the destination process and the search field's UI Automation identifier. Dragging away from the press position or using a physical mouse or keyboard cancels the request. Raw Input processing reads message headers. One background MTA worker queries UI Automation and another registers focus events. Each request carries a generation number so results from earlier input are ignored. Focus is queried during the eligibility window or while the keyboard is visible.

In desktop mode, a mapped B click or selection suppresses the matching Windows shell Back press and release. This applies to Start, Search, and taskbar surfaces.

It checks enabled state, keyboard focus, ControlType, IsPassword, ValuePattern.IsReadOnly, and the read-only property of TextPattern. For host windows, it checks the owning process or ancestor HWND. UIA RuntimeId is used to identify a change of control within the same window. The determination does not request Value, TextRange.GetText, or input content. Brief jitter when a field loses focus delays re-checking. A foreground window change, device disconnect, or mode switch dismisses it immediately.

Offline Pinyin prioritizes whole-word matches and also segments pronunciations that are not matched directly, by syllable or dictionary entry. Candidate combinations limit the number of paths at each position to avoid unbounded searches through long Pinyin strings. When a segmented candidate is selected, only the Pinyin consumed by that candidate is removed. The remainder stays in the keyboard.

The on-screen keyboard uses WS_EX_NOACTIVATE, ShowWithoutActivation, and WM_MOUSEACTIVATE. Before sending characters, it verifies that the foreground window is still the original target. If the target closes or loses foreground, input stops. The layout uses the physical screen bounds of the target control and the monitor working area. For edit areas taller than half the screen, full avoidance may not be possible. Targets that cannot be reliably classified can still be typed into manually.

## Failure and State Boundaries

Settings are validated first, then saved using a temporary file in the same directory and a replacement operation. When the format is corrupted, the user is prompted and default settings are used. The old file is replaced only after the user explicitly saves. Future configuration versions are explicitly rejected. Configuration contains no live state, input content, or clipboard data. Normal exit releases simulated keys and unregisters hotkeys. If the process is forcibly killed by the system or power is lost, a user-mode program cannot guarantee cleanup.

The program registers only XInput. Among multiple backend objects with the same trusted physical identity, DeviceManager selects the preferred one. For unknown physical correspondences, a new Provider must first resolve identity merging or use explicit user binding. See the hardware extension document.
