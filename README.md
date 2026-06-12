# maimaiDX Launcher

一个 maimai DX 游戏启动器，带配置 GUI 编辑器。

## 项目结构

```
maimaiDX-Launcher/
├── GameLauncher/               # 游戏启动器（C# WinForms + WebView2）
│   ├── Form1.cs                # 主窗口逻辑
│   ├── Program.cs              # 入口点
│   ├── ProgressOverlay.cs      # 进度覆盖层
│   └── GameLauncher.csproj
└── Sinmai-Assist/              # 配置编辑器（Python GUI）
    ├── 启动配置编辑器.bat        # 启动脚本
    ├── config_editor_gui.py    # Tkinter GUI 编辑器
    └── Config - zh_CN.yml      # 配置文件
```

## 功能概述

### GameLauncher（启动器）
- 基于 .NET 8 + WinForms + WebView2 的游戏启动器
- 支持自定义背景视频
- 游戏路径管理
- 英文字幕切换

### Sinmai-Assist（配置编辑器）
- 基于 Python + Tkinter 的配置文件 GUI 编辑器
- 编辑 `Config - zh_CN.yml` 配置
- 自动备份与校验

## 开发环境

### GameLauncher
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或 VS Code

### 配置编辑器
- Python 3.x + `pyyaml`、`tkinter`

## 构建

### GameLauncher
```bash
cd GameLauncher/GameLauncher
dotnet build
```

### 配置编辑器
双击 `Sinmai-Assist/启动配置编辑器.bat` 即可运行。

## 免责声明

本项目仅供学习研究使用，请勿用于非法用途。使用本软件产生的任何后果由使用者自行承担。
