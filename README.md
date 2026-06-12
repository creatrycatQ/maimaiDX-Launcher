# maimaiDX Launcher

一个 maimai DX 游戏启动器，集成 Sinmai-Assist 模组，提供更好的游戏体验。

## 项目结构

```
maimaiDX-Launcher/
├── GameLauncher/          # 游戏启动器（C# WinForms + WebView2）
│   ├── Form1.cs           # 主窗口逻辑
│   ├── Program.cs         # 入口点
│   ├── ProgressOverlay.cs # 进度覆盖层
│   └── GameLauncher.csproj
└── Sinmai-Assist/         # 游戏模组（C# MelonLoader Mod）
    ├── Cheat/             # 作弊功能
    ├── Common/            # 通用功能
    ├── Fix/               # 修复功能
    ├── GUI/               # 图形界面
    └── Sinmai-Assist.sln  # 解决方案文件
```

## 功能概述

### GameLauncher（启动器）
- 基于 .NET 8 + WinForms + WebView2 的游戏启动器
- 支持自定义背景视频
- 游戏路径管理
- 英文字幕切换

### Sinmai-Assist（模组）
- **Cheat**: Auto Play, Fast Skip, Unlock Music/Master/Event 等
- **Common**: Dummy Login, Quick Boot, Show FPS, Single Player Mode 等
- **Fix**: Disable Encryption, Skip Version Check, Fix Check Auth 等

更多模组详情见 [Sinmai-Assist/README.md](Sinmai-Assist/README.md)

## 开发环境

### GameLauncher
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或 VS Code

### Sinmai-Assist
- [.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net472)
- [MelonLoader](https://melonwiki.xyz/) v0.6.4 或更低版本
- SDGB 版本游戏库

## 构建

### GameLauncher
```bash
cd GameLauncher/GameLauncher
dotnet build
```

### Sinmai-Assist
1. 在 Visual Studio 中打开 `Sinmai-Assist/Sinmai-Assist.sln`
2. 配置库引用（需使用 SDGB 版本库）
3. 生成解决方案
4. 将 `Sinmai-Assist.dll` 复制到游戏 `Mods` 文件夹

## 免责声明

本项目仅供学习研究使用，请勿用于非法用途。使用本软件产生的任何后果由使用者自行承担。
