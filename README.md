# maimaiDX Launcher

一个自制的 maimai DX 游戏启动器，附带配置 GUI 编辑器。

## 项目结构

```
maimaiDX-Launcher/
├── maimaiDX Launcher/          # 游戏启动器（C# WinForms + WebView2）
│   ├── Form1.cs                # 主窗口逻辑（WebView2 UI、JS 桥接、注册表读写）
│   ├── Form1.Designer.cs       # 窗体设计器（无边框 1280×720、深色背景）
│   ├── Program.cs              # 程序入口（DPI 不感知模式）
│   ├── ProgressOverlay.cs      # 进度覆盖层（淡入淡出动画、进度条）
│   ├── GameLauncher.csproj     # .NET 8 项目文件
│   └── app.ico                 # 应用图标
├── Sinmai-Assist/              # 配置编辑器（Python GUI）
│   ├── 启动配置编辑器.bat        # 一键启动脚本
│   ├── config_editor_gui.py    # Tkinter GUI 编辑器
│   └── Config - zh_CN.yml      # 游戏配置文件
└── .gitignore
```

## 功能概述

### maimaiDX Launcher（启动器）

- **技术栈**：.NET 8 + WinForms + WebView2
- **WebView2 UI**：HTML/CSS/JS 实现现代化的启动界面
- **JS ↔ C# 双向通信**：JS 调用 C# 方法执行文件浏览、注册表读写、窗口控制等操作
- **自定义背景**：支持通过虚拟主机映射加载本地视频/图片作为背景
- **游戏路径管理**：通过注册表持久化游戏路径，支持浏览选择与重置
- **副标题显示**：实时编辑并保存副标题（如 English Subtitle 开关状态）
- **进度覆盖层**：启动游戏时显示带淡入淡出动画的进度条（Win32 `AnimateWindow` API）
- **无边框窗口**：支持标题栏拖拽移动、最小化、关闭
- **一键启动配置编辑器**：直接从启动器打开 Sinmai-Assist

### Sinmai-Assist（配置编辑器）

- **技术栈**：Python + Tkinter + PyYAML
- **可视化配置编辑**：通过 GUI 编辑 `Config - zh_CN.yml` 配置文件
- **自动备份**：修改前自动创建 `.bak` 备份文件
- **字段校验**：修改时进行基本的合法性检查
- **一键启动**：双击 `启动配置编辑器.bat` 即可运行

## 环境要求

| 组件 | 说明 |
|------|------|
| .NET 8 Runtime | 运行启动器必需 |
| WebView2 Runtime | Windows 10/11 通常已内置 |
| Python 3.8+ | 运行配置编辑器（需安装依赖） |
| PyYAML | `pip install pyyaml` |

## 免责声明

本项目仅供学习研究使用，请勿用于非法用途。使用本软件产生的任何后果由使用者自行承担。
