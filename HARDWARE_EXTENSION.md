# 新增手柄与设备能力

[文档首页](README.md) · **简体中文** · [English](HARDWARE_EXTENSION.en.md)

当前发布版仅注册 XInputControllerProvider。北通和其他兼容设备共享同一实现。飞智黑武士 5 Pro 标准版的厂商报告、扩展键、体感、连接差异与电量语义均待实物验证。

## 接入 Provider

1. 实现 IControllerProvider，让 Devices 返回当前的设备快照。持续连接期间复用同一 IControllerDevice 对象。新增和移除会在下一次设备扫描时反映到设置界面。扫描约每 850 ms 执行一次。重连时可返回同一 ID 的新对象，程序会释放旧输入并保留这个 ID 的配置。
2. 每个对象提供 Id、PhysicalKey、Name、Capabilities 和 Read()。Id 是设置关联键，应带后端命名空间。PhysicalKey 是跨后端去重依据。Windows XInput 的 xinput:0 至 xinput:3 是运行时槽位，不能作为通用硬件序列号。将可信的 PnP 容器信息或用户显式关联映射到共同键，再按注册顺序选择一个读取者。无法建立可靠关联时，避免同时为同一设备开启两个读取者。
3. Read() 返回瞬时 ControllerState，断开时返回 Connected=false。按钮集合使用稳定符号名，左右摇杆归一化到 −1…1，右及上为正，扳机到 0…1。读取应有界且快速；阻塞式 HID 读取放在后端线程，向主循环提供最新快照。重连后先返回真实状态，映射引擎会等待回中。
4. 将新 Provider 传给 Host 的附加 Provider 参数。设备层返回状态，由映射层决定如何输出键鼠事件。tests/Tests.cs 中的 SimProvider、SimDevice 和 tests/MappingTests.cs 的 Capture 可用于硬件无关测试。

## 独立扩展按键

Capabilities.Controls 同时驱动设置表和默认补齐逻辑。以未来硬件验证后确认存在的某个按键为例，可声明 `Paddle1`，其按下期间将同名 ID 放入 Buttons。设置界面自动增加一行。可以在 Capabilities.Labels 为稳定 ID 提供显示名称。配置文件仍存储 ID，因此改显示名称也能读回旧设置。未映射的新控件默认 None。把桌面动作设置为 Keyboard 或 Mode 即可单键呼出键盘或切换模式；键盘布局内需要切换模式时，也可将其键盘动作设为 Mode。通用的双键长按仍可使用。

命名约定中，LeftStick、RightStick 与 Axis. 前缀表示二维轴，@ 前缀供引擎内部导航使用。新数字按钮应避开这些名称以及作为组合分隔符的加号。每台设备可有独立的原始 ID 配置，也可通过 AssignedProfiles 关联命名配置。优先保存用户对槽位的显式选择。

## 体感及其他可选数据

ControllerCapabilities 有 HasGyroscope、HasAccelerometer、HasBattery。ControllerState 的 Gyroscope、Accelerometer、BatteryLevel 使用可空值表达未知。XInput 不提供这两种运动数据。电量目前表示 0…3 离散等级，只在系统报告支持的无线电池类型时显示。

有实物后，先在设备层确定报告版本、轴方向、单位和采样间隔。建议面向业务统一约定角速度为 rad/s、加速度为 m/s²，并记录坐标系与校准方式。对于可选的体感指针，在设备适配器中处理偏置、静止漂移、方向变换和速度限幅，然后把归一化二维速度放入 ExtraAxes 的 `Axis.Motion` 项，并把该名称加入 Controls。用户可以将它绑定到 Cursor、Scroll 或 None，键盘布局中也可绑定 Navigate。现有两根物理摇杆仍可独立使用。回中阈值要与校准后的噪声兼容。模拟测试已覆盖供应商自定义轴经统一映射产生指针移动。

## 实物验收顺序

- 分别在 USB、接收器和蓝牙方式下记录 Windows 枚举与实际可用能力。对所有能力选择“可读取、仅板载映射或未知”。同一个扩展键如果只被设备转成普通 A 键，软件不能据此恢复独立按键身份。
- 先核对标准按键及轴，再验证独立扩展键按下/松开、并发按键、断开、重连与待机手柄隔离。两台手柄互换槽位时核对命名配置。最后验证两人游戏和震动。
- 拿到厂商报告证据后再实现 HidControllerProvider 或 FlydigiControllerProvider。仅在用户启用时接入体感，先验证静止漂移和抬手回中，再进行沙发距离下的速度测试。发布前复核参考代码及许可证。

以下为未来审查入口，本版未引入这两个仓库的代码或协议。[vader5pro-hid-tools](https://github.com/DrProton824/vader5pro-hid-tools) · [vader5-pro-mapper](https://github.com/bohewu/vader5-pro-mapper)