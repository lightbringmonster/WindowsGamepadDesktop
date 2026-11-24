# Architecture

[Documentation home](README.en.md) · [简体中文](ARCHITECTURE.md) · **English**

The application uses C#, the .NET Framework 4.8 family, and WinForms. The keyboard is rendered according to the target screen's physical pixels, using a PerMonitorV2 manifest. WinForms is suitable for the tray message window, non-activating top-level windows, and local offline builds. The product has no browser engine, Node process, or NuGet runtime dependency.

| Layer | Source | Responsibility |
|---|---|---|
| Device | src/Controllers.cs | Provider, device instances, capabilities, normalized snapshots, deduplication, and primary-controller selection. XInput slot scanning and battery caching |
| Configuration | src/Configuration.cs | Desktop and keyboard bindings, named-profile association, validation, version conversion, and atomic JSON replacement |
| Modes and mapping | src/ApplicationProfiles.cs, src/MappingEngine.cs | Foreground rules, manual override, chord timing, trigger hysteresis, recentering, key repeat, curves, and displacement accumulation |
| System output | src/NativeInput.cs | IInputSink, SendInput structures, and registration and release of simulated keys currently held down |
| Focus | src/FocusDetection.cs | UIA focus-change notifications, on-demand checks, editability, screen bounds, and debouncing |
| On-screen keyboard | src/Keyboard.cs, src/PinyinDictionary.cs | Model navigation, offline Pinyin segmentation and candidate ranking, system IME key codes, non-activating window, monitor handling, and avoidance |
| System integration | src/Host.cs, src/SystemIntegration.cs | Message loop, tray, startup at sign-in, system hotkeys, physical input activity, session locking, and short-lived overlays |
| Settings | src/SettingsWindow.cs | Configuration draft, capability-driven grid generation, live device status, and import/export |
| Entry point | src/Program.cs | Single instance per current logon session, settings loading, normal exit, and suspend on exception |

## Each Input Sampling Cycle

1. The main thread reads the foreground HWND and queries the process file name when the window changes. Mode changes produced by rules take effect before this round's simulated input. User manual overrides are retained within the same rule. When unlocked, leaving the matching process restores the desktop. While locked, foreground matching rules continue to be tracked, and automatic and manual switching share the mode-lock check. The current mode is retained when unlocking.
2. When a primary controller is connected, a background timer targets a 15 ms interval and requests sampling from the message window via SendNotifyMessage. Only one unprocessed request is kept at a time, and device state and mapping are still updated on the UI thread. The message loop during Windows title-bar mouse capture may filter WM_TIMER. Cross-thread sent messages can be delivered in that loop, so simulated mouse releases can still be processed. The actual sampling resolution is affected by Windows message scheduling. Other slots are scanned approximately every 850 ms. With no device, the timer drops to 250 ms. When the Devices page is open, connected spare devices are also refreshed for viewing.
3. The primary controller drives the mapping engine with a normalized snapshot. The left and right triggers are evaluated with press and release thresholds of 0.52 / 0.32, respectively. Magnitude beyond the radial dead zone is normalized, speed is calculated according to the curve exponent, and fractional displacement is accumulated. Scrolling is output in complete wheel steps.
4. Chord gestures are sampled independently in each control mode and in the keyboard overlay. Releasing between 2 and 3 seconds requests a switch. Holding for 3 seconds toggles the lock state. The next gesture is allowed only after both keys are released. Native mode continues to detect the switch chord and the lock gesture. When the keyboard is visible, the keyboard mapping table is used. Desktop mode compares the required keys and mouse buttons with the state actually emitted in the previous round. The same output can be held by multiple gamepad keys. Releases are performed in reverse order, and if SendInput rejects a release, the record is retained for retry in the next round.
5. Mode changes, device switches, disconnects, suspension, session locking, and shutdown trigger release and recentering. The application does not call XInputSetState. Games continue to manage rumble.

## Input Fields and Focus

A completed simulated left click or an explicit focus shortcut starts an eligibility window of about 930 ms. The first sample waits about 85 ms for the target to update focus. A click must land within an editable control in the same foreground window. Dragging away from the press position or using a physical mouse or keyboard cancels the request. Raw Input processing reads message headers and skips key codes and text. One background MTA worker queries UI Automation and another registers focus events. Each request carries a generation number so results from earlier input are ignored. Focus is queried during the eligibility window or while the keyboard is visible.

It checks enabled state, keyboard focus, ControlType, IsPassword, ValuePattern.IsReadOnly, and the read-only property of TextPattern. For host windows, it checks the owning process or ancestor HWND. UIA RuntimeId is used to identify a change of control within the same window. The determination does not request Value, TextRange.GetText, or input content. Brief jitter when a field loses focus delays re-checking. A foreground window change, device disconnect, or mode switch dismisses it immediately.

Offline Pinyin prioritizes whole-word matches and also segments pronunciations that are not matched directly, by syllable or dictionary entry. Candidate combinations limit the number of paths at each position to avoid unbounded searches through long Pinyin strings. When a segmented candidate is selected, only the Pinyin consumed by that candidate is removed. The remainder stays in the keyboard.

The on-screen keyboard uses WS_EX_NOACTIVATE, ShowWithoutActivation, and WM_MOUSEACTIVATE. Before sending characters, it verifies that the foreground window is still the original target. If the target closes or loses foreground, input stops. The layout uses the physical screen bounds of the target control and the monitor working area. For edit areas taller than half the screen, full avoidance may not be possible. Targets that cannot be reliably classified can still be typed into manually.

## Failure and State Boundaries

Settings are validated first, then saved using a temporary file in the same directory and a replacement operation. When the format is corrupted, the user is prompted and default settings are used. The old file is replaced only after the user explicitly saves. Future configuration versions are explicitly rejected. Configuration contains no live state, input content, or clipboard data. Normal exit releases simulated keys and unregisters hotkeys. If the process is forcibly killed by the system or power is lost, a user-mode program cannot guarantee cleanup.

The program registers only XInput. Among multiple backend objects with the same trusted physical identity, DeviceManager selects the preferred one. For unknown physical correspondences, a new Provider must first resolve identity merging or use explicit user binding. See the hardware extension document.
