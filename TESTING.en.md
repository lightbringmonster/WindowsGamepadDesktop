# Test Log

[Documentation home](README.en.md) · [简体中文](TESTING.md) · **English**

Tests were performed on 2026-11-24. The environment was a Dell G15 5520 with Windows 11 Pro x64 build 26300 and an RTX 3060 Laptop GPU. Display outputs at 1920×1080 and 2560×1440 were detected on this machine. The records below distinguish real Windows software paths, a simulated gamepad, and physical BETOP hardware.

## Automated and Windows Verification Performed

| Scope | Verified results |
|---|---|
| Core regression | 41 checks passed. Coverage included XInput decoding, capability extension axes, primary-controller deduplication and reconnection, button combinations, recentering, key repeat, double-click timing, configuration read/write and version rejection, offline Pinyin, layout avoidance, and process rules. |
| Real Win32 input | 28 integration checks passed. The test injected simulated gamepad snapshots into the actual host. The output was real SendInput, and the target was an edit window in a separate process. It was verified that an ordinary focus change alone does not bring up the keyboard, that a gamepad click does bring it up, and that the target received q and 你好. |
| Controls and focus loss | Password metadata recognition, read-only edit field exclusion, dismissal when switching controls in the same window, manual invocation, dismissal on gamepad disconnect, and reconnection passed. The real GetAsyncKeyState confirmed that simulated RT held the left mouse button down and that it was released after disconnect. |
| Mode and pause | The matched test process entered native mode. Holding LB+RB toggled between modes in both directions. The global Ctrl+Alt+F12 actually paused it. And the desktop was restored after the matched process was closed. |
| Windows Search | The main Windows 11 Search window was brought to the foreground, UIA detected editable focus, and a simulated gamepad click brought up the keyboard automatically. On a cold start, event registration had delayed the first sample. After the fix, the test passed twice in succession. |
| File Explorer | A window was opened at the test directory, the address bar was targeted, and UIA, automatic appearance, and dismissal with A were confirmed. No address or file was modified in that test. |
| Microsoft Edge | An isolated browser profile directory was used, and a local HTML page was opened. The keyboard appeared automatically for both the address bar and the page input, and the page received q typed on the on-screen keyboard. |
| Electron | The installed VS Code used a separate test configuration and files. The Find box brought up the keyboard automatically. The current Monaco editor focus reported as editable. Focus in the Quick Open panel may be exposed as a list item. The manual RB fallback passed. |
| Settings and display | The Device, Key Mapping, Virtual Keyboard, and Application Rules pages were opened in the actual application. Grid rows were verified. And the settings page and candidate keyboard were rendered. Geometry checks covered 1080p, 1440p, 4K, negative coordinates, and avoidance. Physical acceptance is still required for a physical 4K TV, hot-plugging across different DPI settings, and couch readability. |
| Release and resources | The x64 WinExe, asInvoker, and system .NET Framework builds passed. Single-instance behavior, tray startup, and normal exit were tested. In a short 8-second sample with no device connected and the keyboard closed, the CPU-time difference was below the timing resolution. The working set was approximately 34.3 MiB and private memory approximately 24.1 MiB. This sample does not represent resource usage or latency during active input. |

Windows tests send characters only while the intended test window has focus. The test may stop when the window is covered by another program. The test program briefly uses the mouse and attempts to restore the mouse position and foreground window when finished. The product does not include these test entry points.

## 0.9.12 Popup notifications

69 core checks and six Windows popup checks passed. The status cue preserved the foreground window and hid on timeout. An owned settings error kept its complete text and closed on acknowledgement. An unowned startup prompt opened successfully and kept long details scrollable. Switch, lock, pause, and error previews were checked in light mode.

## 0.9.11 Startup mode

69 core checks and eight Windows startup checks passed. Existing profiles and first launches default to desktop mode. Native startup stays native until the controller changes mode with the shoulder-button chord. A normal exit saves the active mode, and a new Host reads it from the profile.

## 0.9.5 Browser focus and mode feedback

65 core checks and 37 Windows integration checks passed. In isolated Chrome and Edge profiles, clicking the new-tab button or using a binding for Ctrl + T left the keyboard closed when the address bar gained focus. Clicking the address bar or a webpage text field still opened the keyboard. Windows Search and File Explorer edit-field tests also passed.

The release prompt appeared at two seconds while preserving the foreground window and current mode. Releasing switched modes and displayed the confirmation. Three-second locking, unlocking, and full-screen switching remained covered.

## 0.9.4 Continuous Pinyin and Keyboard Styles

62 core checks and 34 Windows integration checks passed. Coverage included continuous composition of complete Pinyin, segmented candidates retaining the remaining Pinyin, the syllable separator, candidate paging, and the existing single-character path. In a separate Windows edit box, a simulated gamepad was used to spell nihaoshijie. Pressing Y committed “你好世界” at once, and the original target retained focus. The actual rendering of the Pinyin and English layouts was checked, and the Pinyin preview displayed “你好欢迎使用” in full.

## 0.9.3 Mode Gesture

51 core checks and 34 Windows integration checks passed. With an actual foreground test window covering the entire display, a simulated gamepad completed two-way switching between desktop and native modes. Holding for 3 seconds kept the current mode and locked it. Once locked, it rejected the 2-second gesture and the custom Mode button. After a 3-second hold unlocked it, switching could continue. The mouse-release test in the title-bar message loop was also rerun. The full-screen integration scenario used a borderless Windows test window.

## 0.9.2 Title-Bar Minimize Regression

The old version's failure to emit a left-button release while tracking buttons over the window title bar was reproduced. Using the same regression case that retrieves only mouse messages, 0.9.1 could not release it, whereas 0.9.2 could within the native message loop. The actual title-bar test after the fix also verified the left-button release corresponding to B, window minimization, and pointer movement with the stick after minimization. 41 core checks passed. The full Windows integration results for the old version appear in the preceding table. When the full integration suite was rerun for 0.9.2 in this round, the test window could not obtain the foreground, so the run stopped at the test-preparation stage.

A physical retest of the local release build recorded B release and Settings-window minimization in the same operation. After minimization, 1777 pixels of mouse movement were measured during left-stick activity, and physical B button presses and releases could still be read.

`Tests.exe --title-loop` can be used to independently retest polled release. `Tests.exe --minimize` drives the test process with a simulated gamepad on the actual desktop and requires the test window to obtain the foreground and remain unobstructed.

## Physical BETOP Device

- All four slots in the initial scan returned ERROR_DEVICE_NOT_CONNECTED (1167). Slot index 0 (XInput 1 in the UI) then connected successfully. Windows PnP showed “XBOX 360 Controller for Windows,” and its USB bus property explicitly identified **BTP-KP20 XINPUT**, VID 20BC, PID 5128.
- During an active sampling record of about 309 seconds, none of the 14,951 successful reads captured any button press, trigger movement, or stick movement away from neutral. A subsequent scan again showed it as disconnected. Device enumeration, connection, and state reads have physical-device evidence. The first round captured no input, so a physical-button test was then started.
- Multi-gamepad primary-controller isolation, release on disconnect, and game-mode silence have simulated and Windows integration evidence. Two-player gaming, native rumble, and cross-reconnection of two physical gamepads remain for manual verification.

### Supplementary Acceptance After BETOP Connection

The BETOP Kunpeng 20 was read on XInput 1. The PnP bus name was BTP-KP20 XINPUT. Across two sampling records, 22,254 reads succeeded in total. The buttons actually pressed and released were A, B, LB, LS, Left, RB, and Right. The left stick X / Y ranges were -32759…32767 / -32768…32767, and the right stick ranges were -5982…31520 / -32768…32767. Both LT and RT reached 255, and recentering after input was captured.

The longest simultaneous LB + RB hold was 1.45 seconds. During two round-trip switches, the actual tray process displayed “Native Gamepad” and “Desktop Keyboard/Mouse” in sequence. During this round's hands-on procedure, the dedicated edit box received left-button press and release, and the virtual keyboard was observed to appear and dismiss automatically. The feel of RT dragging, the actual wireless link, and in-game rumble still require separate acceptance.

Physical testing found that after the keyboard was dismissed, pressing A again sent Esc, which triggered automatic invocation in the original edit box. 0.9.1 fixed that path. The 41 core checks were rerun with the physical device connected. Another 28 Windows integration checks were run, covering dismissal with A, no appearance when A was pressed again, and the keyboard still appearing on a B click. The automatic-invocation retest in Windows Search passed.

0.9.1 retested the complete input path with the same BETOP device. The gamepad selected the dedicated edit box, the keyboard appeared, and the physical directional buttons were used to navigate and B was used to enter text. The target received six test characters and subsequently also received a deletion. After dismissal with A, the target received three subsequent Esc presses, and the window records showed no further appearance. The acceptance window recorded only the test text length and control events.

## Compatibility and Unverified Items

Steam is running on this machine, and its installation directory supplies an Xbox desktop layout template. At inspection time, no explicitly named BETOP or Flydigi mapping process was found. No evidence was obtained about which physical button Steam currently maps to the mouse, and no Steam or vendor software settings were modified. Duplicate input can be attributed to its source using the “pause, then press B” procedure in the README. The filename of the Playnite full-screen executable was checked on this machine. Its native-mode rule was tested through the same mechanism, but the game was not actually launched for acceptance.

The system IME was tested with a real input method in a dedicated Windows edit box. The test thread selected an installed Chinese input configuration. The on-screen keyboard model sent nihao and the digit 1 through real virtual keys. And the target control received “你好”. The thread's original input language was restored after the test. Candidate behavior in other input methods and applications still requires separate verification. Committing Chinese via offline Pinyin has a separate cross-process test. A sign-in startup item is written to HKCU only after the user checks it. No actual user startup item was written for testing, and no sign-out/sign-in was performed. UAC, the lock screen, game anti-cheat, a physical 4K TV, and the Flydigi extension protocol have not been verified on real machines.

## Rerunning Automation

Open PowerShell in the source directory. Exit any existing tray instance first so that the tests can have exclusive use of the hotkeys used by this program. The following interactive tests require an unlocked desktop. Run them in order.

```powershell
.\build.ps1 -Test
.\bin\Tests.exe --windows
.\bin\Tests.exe --ime
.\bin\Tests.exe --search
.\bin\Tests.exe --explorer .\bin\scratch-shell
.\bin\Tests.exe --browser .\bin\scratch-edge
.\bin\Tests.exe --preview .\bin\screens
```

The Electron compatibility test can be run with `Tests.exe --electron "<full path to Code.exe>" "<test working directory>"`. The Edge test uses the common Windows Edge installation path. Apart from the core build, no separate test framework needs to be installed.

## BETOP Gamepad Verification

1. Connect in PC/XInput mode. On the “Device” page, press A/B/X/Y, the D-pad, the shoulder buttons, Start/Back, and the stick buttons in sequence. Observe the name and the values of both sticks and triggers. After release they should return to near zero.
2. Close Settings. Move with the left stick, use LT for precise positioning, B for single- and double-clicks, and RT for dragging. Turn off the gamepad during a drag and observe the mouse release. After reconnecting, recenter first.
3. Press LB and RB individually, then use both shoulder buttons to try holding for less than 2 seconds, holding for about 2.3 seconds and releasing, and holding continuously for 3 seconds. Confirm that the three actions are, in order, cancel, switch, and lock. After locking, hold for a full 3 seconds again to unlock. This can be tested in windowed mode, full-screen mode, and with the keyboard open.
4. Use B to select Windows Search, the File Explorer address bar, and browser input fields, and enter English, digits, and Chinese. In the Pinyin layout, type nihao and select “你好”. Then try long-press deletion, selection, copy/paste, dismissal with A, and completion with RT.
5. Add the game EXE to the native rules. Enter the game and test native buttons and rumble. After exiting, move the desktop pointer. Connect a second gamepad and confirm that only the primary controller moves the pointer and that both can be used in the game.
6. Move the target to the TV and observe the keyboard's position and readability at the actual scaling ratio. Using the virtual keyboard's system IME layout, select Windows Microsoft Pinyin and confirm spelling, numeric candidate selection, and paging. Record only the failing application, scenario, and button. No input content is needed.
