# Adding Gamepads and Device Capabilities

[Documentation home](README.en.md) · [简体中文](HARDWARE_EXTENSION.md) · **English**

The current release registers only XInputControllerProvider. BETOP and other compatible devices share the same implementation. The vendor reports, extension buttons, motion sensing, connection differences, and battery semantics of the Flydigi Vader 5 Pro Standard Edition all await physical verification.

## Integrating a Provider

1. Implement IControllerProvider in a separate file or project, with its Devices returning stable IControllerDevice objects. The current DeviceManager registers objects at construction and is suitable for slot-based backends. HID devices should retain a stable proxy slot and handle arrival and removal inside the proxy. If the actual device set must grow dynamically, enumeration refresh can be extended in the device management layer. Mapping, keyboard, and mode logic can keep the existing interfaces.
2. Each object provides Id, PhysicalKey, Name, Capabilities, and Read(). Id is the settings association key and should carry a backend namespace. PhysicalKey is the basis for cross-backend deduplication. Windows XInput's xinput:0 through xinput:3 are runtime slots and cannot serve as universal hardware serial numbers. Map trusted PnP container information or explicit user associations to a common key, then select one reader according to registration order. When a reliable association cannot be established, avoid enabling two readers for the same device at the same time.
3. Read() returns an instantaneous ControllerState and returns Connected=false when disconnected. The button set uses stable symbolic names. The left and right sticks are normalized to −1…1 with right and up positive, and triggers to 0…1. The input worker reads the primary device and the UI thread scans other devices. Calls to the same device remain serialized. Reuse unchanged snapshots if convenient, and leave each returned snapshot and its collections immutable. Keep reads bounded and fast. Blocking HID reads belong on a backend thread. After reconnection, return the real state first so the mapping engine can wait for recentering.
4. Compose the new Provider at the device-manager wiring point in the Host constructor, or add an aggregating Provider. The new device layer only returns state and does not send Win32 input. SimProvider and SimDevice in tests/Tests.cs, and Capture in tests/MappingTests.cs, can be used to develop hardware-independent tests.

## Independent Extension Buttons

Capabilities.Controls drives both the settings table and the default-filling logic. For example, for a button that future hardware verification confirms exists, declare `Paddle1`. While it is pressed, place the same-named ID in Buttons. The settings UI automatically adds a row. Unmapped new controls default to None. Set the desktop action to Keyboard or Mode to bring up the keyboard or switch modes with a single button. When mode switching is needed inside the keyboard layout, its keyboard action can also be set to Mode. The generic two-key long press remains available.

In the naming convention, LeftStick, RightStick, and the Axis. prefix denote two-dimensional axes, and the @ prefix is reserved for engine-internal navigation. New digital buttons should avoid these names and the plus sign used as the chord separator. Each device can have an independent raw-ID configuration or be associated with a named profile through AssignedProfiles. Prefer saving the user's explicit slot selection.

## Motion Sensing and Other Optional Data

ControllerCapabilities has HasGyroscope, HasAccelerometer, and HasBattery. Gyroscope, Accelerometer, and BatteryLevel in ControllerState use nullable values to represent unknown. XInput does not provide these two types of motion data. Battery currently represents a discrete 0…3 level and is displayed only when the system reports a supported wireless battery type.

Once physical hardware is available, first determine the report version, axis directions, units, and sampling interval in the device layer. It is recommended to standardize angular velocity in rad/s and acceleration in m/s², and to record the coordinate system and calibration method. For an optional motion pointer, handle bias, rest drift, orientation transform, and speed limiting in the device adapter, then place the normalized two-dimensional velocity into the `Axis.Motion` entry of ExtraAxes and add that name to Controls. Users can bind it to Cursor, Scroll, or None, and it can also be bound to Navigate in the keyboard layout. The two existing physical sticks remain independently usable. The recentering threshold must be compatible with the calibrated noise. Simulated tests have covered pointer movement produced by vendor-custom axes through the unified mapping.

## Hardware Verification Checklist

- Record Windows enumeration and the capabilities actually available in USB, receiver, and Bluetooth modes, respectively. For each capability, choose "readable, onboard mapping only, or unknown." If an extension button is only converted by the device into an ordinary A button, the software cannot recover its independent button identity from that.
- First verify the standard buttons and axes, then verify independent extension-button press/release, simultaneous buttons, disconnect, reconnection, and isolation of an idle gamepad. When two gamepads swap slots, verify the named profiles. Finally verify two-player games and rumble.
- Implement HidControllerProvider or FlydigiControllerProvider only after obtaining vendor report evidence. Enable motion sensing only when the user enables it. First verify rest drift and lift-to-recenter, then perform speed tests at couch distance. Review reference code and licenses before release.

These repositories are references for future Flydigi hardware testing. [vader5pro-hid-tools](https://github.com/DrProton824/vader5pro-hid-tools) · [vader5-pro-mapper](https://github.com/bohewu/vader5-pro-mapper)
