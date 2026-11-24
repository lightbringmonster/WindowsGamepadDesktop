# Third-Party Dependencies and Licenses

[Documentation home](README.en.md) · [简体中文](THIRD_PARTY.md) · **English**

## Data Distributed with the Package

| Content | Source and processing | License materials |
|---|---|---|
| data/luna_pinyin.dict.yaml | The Luna Pinyin dictionary from Rime Developers, pinned to commit 84685a7e7793e43278ca29368977b79d74d37a03. The repository checkout file and the author and etymology notes in the data header are retained. At runtime, Pinyin is parsed, duplicates are merged, and Windows Simplified Chinese mapping is used to generate display candidates. | data/RIME-AUTHORS.txt, data/LGPL-3.0.txt, data/GPL-3.0.txt |
| data/common.tsv | Common Simplified Chinese candidates and preferred-word ordering provided by this project. | [MIT](LICENSE). |

The Rime repository distributes the LGPL v3 text, and its author notes list upstream sources including Chewing, OpenCC, Android Pinyin IME, and Moedict. The dictionary is a standalone file in the sibling data directory and can be replaced directly. The package includes the corresponding readable dictionary source file, author notes, and license texts. [Pinned dictionary version](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/luna_pinyin.dict.yaml) · [Author notes](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/AUTHORS) · [Upstream license](https://github.com/rime/rime-luna-pinyin/blob/84685a7e7793e43278ca29368977b79d74d37a03/LICENSE)

## System Components

Windows provides .NET Framework, WinForms, GDI+, UIAutomationClient, UIAutomationTypes, WindowsBase, System.Web.Extensions, XInput, User32, Kernel32, and WTS. The project loads these components from the system. The portable package does not bundle Microsoft runtimes, drivers, or input methods. The test tools additionally use the system Microsoft.CSharp and Shell automation interfaces, and the browser and Electron tests invoke the installed target applications.

## Reference Project

Desktop-Gamepad is pinned to commit e428606bea52104ad0b8484e5170fbc06fdf0ded, is under the MIT license, and carries the copyright notice © 2026 Tommy Theobald. This project was implemented independently after a review of the reference source and dependencies. The bundled icon, settings, and keyboard interface were created for this project. Its WebView2, React frontend, and button images are not distributed. [Reference project license](https://github.com/DoobDewd/Desktop-Gamepad/blob/e428606bea52104ad0b8484e5170fbc06fdf0ded/LICENSE)

The two Flydigi-related reference repositories serve only as review entry points for when future devices arrive. This version introduces no related code, device protocol, or firmware.
