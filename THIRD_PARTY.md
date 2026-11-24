# 第三方依赖与许可证

## 随包分发的数据

| 内容 | 来源与处理 | 许可证材料 |
|---|---|---|
| data/luna_pinyin.dict.yaml | Rime Developers 的朙月拼音词典，固定提交 84685a7e7793e43278ca29368977b79d74d37a03。保留仓库检出文件及数据头的作者、词源说明。运行时解析拼音、合并重复项，并用 Windows 简体映射生成显示候选。 | data/RIME-AUTHORS.txt、data/LGPL-3.0.txt、data/GPL-3.0.txt |
| data/common.tsv | 本项目提供的常用简体候选和首选词顺序。 | 随本项目源码提供。 |

Rime 仓库发布 LGPL v3 文本，其作者说明列出了新酷音、OpenCC、Android Pinyin IME 与萌典等上游来源。词典是同级 data 目录中的独立文件，可直接替换。包中含对应的可阅读词典源文件、作者说明及许可文本。[词典固定版本](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/luna_pinyin.dict.yaml) · [作者说明](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/AUTHORS) · [上游许可](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/LICENSE)

## 系统组件

Windows 提供 .NET Framework、WinForms、GDI+、UIAutomationClient、UIAutomationTypes、WindowsBase、System.Web.Extensions、XInput、User32、Kernel32 与 WTS。项目从系统加载这些组件，便携包不另附微软运行时、驱动或输入法。测试工具额外使用系统 Microsoft.CSharp 和 Shell 自动化接口，浏览器与 Electron 测试调用已安装的目标应用。

## 参考项目

Desktop-Gamepad 固定提交 e428606bea52104ad0b8484e5170fbc06fdf0ded，MIT，版权声明为 © 2026 Tommy Theobald。审查涉及源码和依赖；交付程序独立实现。随包图标、设置和键盘界面为本项目绘制。未分发其 WebView2、React 前端或按钮图片。[参考项目许可证](https://github.com/DoobDewd/Desktop-Gamepad/blob/e428606bea52104ad0b8484e5170fbc06fdf0ded/LICENSE)

飞智相关两个参考仓库仅作为未来设备到货后的审查入口。本版没有引入相关代码、设备协议或固件。