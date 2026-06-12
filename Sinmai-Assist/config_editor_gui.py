#!/usr/bin/env python3
"""
Sinmai-Assist 配置文件 GUI 编辑器
用于编辑 Config - zh_CN.yml 配置文件的本地GUI工具
"""

import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import yaml
import os
import copy
from pathlib import Path

# ============================================================
# 配置文件路径
# ============================================================
SCRIPT_DIR = Path(__file__).parent
CONFIG_PATH = SCRIPT_DIR / "Config - zh_CN.yml"
BACKUP_EXT = ".bak"


# ============================================================
# YAML 读取/写入（保留注释）
# ============================================================
def load_config(path: Path) -> dict:
    """加载 YAML 配置文件"""
    with open(path, "r", encoding="utf-8-sig") as f:
        data = yaml.safe_load(f)
    return data if data else {}


# ============================================================
# 配置校验
# ============================================================
# 期望的顶级 section 及其合法子键（用于结构校验）
EXPECTED_SECTIONS = {
    "common", "cheat", "fix", "modSetting",
}

ALLOWED_LEAF_TYPES = (bool, int, float, str)


def validate_config(data, file_path: Path = None) -> list:
    """校验配置文件的合法性。

    返回一个错误消息列表；列表为空表示校验通过。
    校验规则：
      1. 顶层必须是 dict/mapping
      2. 至少包含一个已知 section
      3. 每个 section 必须是 dict
      4. 叶子节点值只能是 bool / int / float / str
      5. 报告未知的顶层 key（警告级别，不阻断）
    """
    errors = []

    # 规则 1：顶层必须是 dict
    if not isinstance(data, dict):
        errors.append("配置文件顶层必须是键值对（mapping），不能是列表或纯量值。")
        return errors

    if len(data) == 0:
        errors.append("配置文件内容为空，没有任何配置项。")
        return errors

    # 规则 2：至少包含一个已知 section
    known_sections = [s for s in EXPECTED_SECTIONS if s in data]
    if not known_sections:
        errors.append(
            f"未找到任何已知配置段。\n"
            f"期望至少包含以下之一: {', '.join(sorted(EXPECTED_SECTIONS))}"
        )
        return errors

    # 规则 3 & 4 & 5：遍历每个 section 检查
    for section_name, section_value in data.items():
        # 规则 5：未知顶层 key（警告）
        if section_name not in EXPECTED_SECTIONS:
            errors.append(
                f"[警告] 未知的顶层配置段 \"{section_name}\"，"
                f"将保留但无法通过界面编辑。"
            )
            continue

        # 规则 3：每个 section 必须是 dict
        if not isinstance(section_value, dict):
            errors.append(
                f"配置段 \"{section_name}\" 必须是键值对（mapping），"
                f"当前类型为 {type(section_value).__name__}。"
            )
            continue

        # 规则 4：递归检查叶子节点类型
        _validate_section_leaves(section_name, section_value, [], errors)

    return errors


def _validate_section_leaves(section: str, node, path: list, errors: list):
    """递归检查 section 内的叶子节点类型"""
    if isinstance(node, dict):
        for key, value in node.items():
            _validate_section_leaves(section, value, path + [key], errors)
    elif isinstance(node, list):
        errors.append(
            f"[{section}] {'.'.join(path)}: 不支持的列表类型，"
            f"配置值不能是数组。"
        )
    elif node is None:
        errors.append(
            f"[{section}] {'.'.join(path)}: 值为空（null），请设置有效值。"
        )
    elif not isinstance(node, ALLOWED_LEAF_TYPES):
        errors.append(
            f"[{section}] {'.'.join(path)}: 不支持的类型 "
            f"{type(node).__name__}，仅允许 bool/int/float/str。"
        )


def save_config(path: Path, data: dict):
    """保存 YAML 配置文件，保留格式"""
    # 先备份原文件
    backup_path = path.with_suffix(path.suffix + BACKUP_EXT)
    with open(path, "r", encoding="utf-8-sig") as f:
        original_lines = f.readlines()

    # 重建文件，保持注释和格式
    new_lines = rebuild_yaml_lines(original_lines, data)

    # 写回文件
    with open(path, "w", encoding="utf-8-sig") as f:
        f.writelines(new_lines)

    print(f"配置已保存到: {path}")


def rebuild_yaml_lines(original_lines: list, data: dict) -> list:
    """
    基于原始文本行重建YAML，保留注释和空行，
    只更新值部分。
    """
    new_lines = []
    i = 0
    path_stack = []  # 当前路径栈

    while i < len(original_lines):
        line = original_lines[i]
        stripped = line.strip()

        # 空行
        if stripped == "":
            new_lines.append(line)
            i += 1
            continue

        # 纯注释行（没有前置键）
        if stripped.startswith("#") and not any(
            kw in line.split("#")[0] for kw in [": ", ":\n"]
        ):
            # 检查是否是独立注释行
            indent = len(line) - len(line.lstrip())
            if indent == 0 or (i > 0 and original_lines[i - 1].strip() == ""):
                new_lines.append(line)
                i += 1
                continue
            # 检查该行是否只包含注释
            before_hash = line.split("#")[0]
            if before_hash.strip() == "":
                new_lines.append(line)
                i += 1
                continue

        # 解析当前行的缩进层级
        indent = len(line) - len(line.lstrip())
        level = indent // 2

        # 尝试解析 "key: value # comment" 格式
        # 找到冒号分隔符
        colon_idx = find_colon_idx(stripped)

        if colon_idx == -1:
            # 没有冒号，保持原样
            new_lines.append(line)
            i += 1
            continue

        key = stripped[:colon_idx].strip()
        after_colon = stripped[colon_idx + 1 :].strip()

        # 分离值和注释
        value_str, comment = split_value_comment(after_colon)

        # 更新路径栈
        while len(path_stack) > level:
            path_stack.pop()

        if value_str == "":
            # 这是一个嵌套键，进入下一级
            path_stack.append(key)
            new_lines.append(line)
            i += 1
            continue

        # 这是一个叶子节点，尝试从data中获取新值
        full_path = path_stack + [key]

        new_value = get_value_by_path(data, full_path)
        if new_value is not None:
            # 替换值
            new_value_str = format_yaml_value(new_value)
            if comment:
                new_content = " " * indent + key + ": " + new_value_str + " #" + comment + "\n"
            else:
                new_content = " " * indent + key + ": " + new_value_str + "\n"
            new_lines.append(new_content)
        else:
            # 值未找到，但有可能是嵌套的空对象
            # 检查data中是否有这个嵌套路径
            parent_data = get_nested(data, path_stack)
            if isinstance(parent_data, dict) and key in parent_data:
                child = parent_data[key]
                if isinstance(child, dict):
                    # 这是一个嵌套对象，不是叶子
                    path_stack.append(key)
                    new_lines.append(line)
                else:
                    new_lines.append(line)
            else:
                new_lines.append(line)

        i += 1

    return new_lines


def find_colon_idx(line: str) -> int:
    """找到YAML键值分隔的冒号位置（排除注释中的冒号）"""
    hash_idx = line.find("#")
    search_range = hash_idx if hash_idx != -1 else len(line)
    for i, ch in enumerate(line[:search_range]):
        if ch == ":":
            return i
    return -1


def split_value_comment(after_colon: str) -> tuple:
    """从冒号后的内容中分离值和注释"""
    hash_idx = after_colon.find("#")
    if hash_idx == -1:
        return after_colon.strip(), ""
    value = after_colon[:hash_idx].strip()
    comment = after_colon[hash_idx + 1 :].strip()
    return value, comment


def get_value_by_path(data: dict, path: list) -> any:
    """根据路径列表获取嵌套字典中的值"""
    current = data
    for key in path:
        if isinstance(current, dict) and key in current:
            current = current[key]
        else:
            return None
    if isinstance(current, dict):
        return None  # 不是叶子节点
    return current


def get_nested(data: dict, path: list) -> any:
    """根据路径列表获取嵌套字典"""
    current = data
    for key in path:
        if isinstance(current, dict) and key in current:
            current = current[key]
        else:
            return None
    return current


def format_yaml_value(value) -> str:
    """格式化Python值为YAML字符串"""
    if isinstance(value, bool):
        return "true" if value else "false"
    elif isinstance(value, float):
        return str(value)
    elif isinstance(value, int):
        return str(value)
    elif isinstance(value, str):
        if value == "":
            return '""'
        return value
    else:
        return str(value)


# ============================================================
# 配置数据模型：扁平化字段描述
# ============================================================
def build_field_list(data: dict) -> list:
    """
    将嵌套的配置字典展平为字段列表。
    每个字段是一个dict: {section, path, display_name, value, type, comment}
    """
    fields = []
    # 手动定义顺序和显示名称（中文）
    _walk_config(data, [], "", fields)
    return fields


def _walk_config(data: dict, path: list, section: str, fields: list):
    """递归遍历配置"""
    for key, value in data.items():
        current_path = path + [key]
        if isinstance(value, dict):
            # 嵌套对象
            sec = section if section else key
            _walk_config(value, current_path, sec, fields)
        else:
            # 叶子节点
            field_info = {
                "section": section if section else key,
                "path": ".".join(current_path),
                "key": key,
                "full_path": current_path,
                "value": value,
                "type": type(value).__name__,
            }
            fields.append(field_info)


# ============================================================
# GUI 应用程序
# ============================================================
class ConfigEditorApp:
    def __init__(self, root: tk.Tk, initial_file: Path = None):
        self.root = root
        self.root.title("Sinmai-Assist 配置编辑器")
        self.root.geometry("960x700")
        self.root.minsize(800, 600)

        # 当前编辑的文件路径（None 表示未加载有效文件）
        self.current_file = None
        self.config_data = {}
        self.original_data = {}
        self.fields = []

        # 存储控件引用
        self.widgets = {}  # path -> tk.Variable

        # 先构建基础界面（工具栏、状态栏），再尝试加载文件
        self._build_ui()

        # 尝试加载初始文件
        if initial_file is not None:
            self._try_load_file(initial_file)
        else:
            self.status_var.set("⚠️ 未加载配置文件 — 请点击「打开文件」选择一个有效的配置文件")

        # 绑定快捷键
        self.root.bind("<Control-s>", lambda e: self._save())
        self.root.bind("<Control-r>", lambda e: self._reload())
        self.root.bind("<Control-o>", lambda e: self._open_file())

        # 窗口关闭时检查
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

    # --------------------------------------------------------
    # 界面构建
    # --------------------------------------------------------
    def _build_ui(self):
        # 顶部工具栏
        toolbar = ttk.Frame(self.root, padding=5)
        toolbar.pack(side=tk.TOP, fill=tk.X)

        ttk.Label(
            toolbar,
            text="Sinmai-Assist 配置文件编辑器",
            font=("Microsoft YaHei", 14, "bold"),
        ).pack(side=tk.LEFT, padx=(5, 20))

        self._button(toolbar, "📂 打开文件 (Ctrl+O)", self._open_file, "orange").pack(
            side=tk.LEFT, padx=3
        )
        # 当前文件路径显示
        self.file_label_var = tk.StringVar(value="")
        ttk.Label(
            toolbar,
            textvariable=self.file_label_var,
            foreground="#555",
            font=("Microsoft YaHei", 8),
        ).pack(side=tk.LEFT, padx=10)

        self._button(toolbar, "💾 保存 (Ctrl+S)", self._save, "green").pack(
            side=tk.RIGHT, padx=3
        )
        self._button(toolbar, "🔄 重新加载 (Ctrl+R)", self._reload, "blue").pack(
            side=tk.RIGHT, padx=3
        )
        self._button(toolbar, "↩ 撤销修改", self._undo, "gray").pack(
            side=tk.RIGHT, padx=3
        )

        # 状态栏
        self.status_var = tk.StringVar(value="就绪 - 请修改配置后点击保存")
        status_bar = ttk.Label(
            self.root, textvariable=self.status_var, relief=tk.SUNKEN, anchor=tk.W
        )
        status_bar.pack(side=tk.BOTTOM, fill=tk.X)

        # 主内容区域 - Notebook 分页
        notebook = ttk.Notebook(self.root, padding=5)
        notebook.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        # 按 section 分组
        sections_order = ["common", "cheat", "fix", "modSetting"]
        section_labels = {
            "common": "📋 通用设置 (Common)",
            "cheat": "🎮 作弊设置 (Cheat)",
            "fix": "🔧 修复设置 (Fix)",
            "modSetting": "⚙️ Mod 设置",
        }

        for section in sections_order:
            section_fields = [f for f in self.fields if f["section"] == section]
            if section_fields:
                tab = ttk.Frame(notebook)
                notebook.add(tab, text=section_labels.get(section, section))
                self._build_section_tab(tab, section_fields, section)

    # --------------------------------------------------------
    # 文件加载与切换
    # --------------------------------------------------------
    def _try_load_file(self, file_path: Path):
        """尝试加载并校验一个配置文件。
        校验通过则设为当前文件并刷新界面；
        校验失败则弹窗提示并保持当前状态。
        返回 True 表示加载成功。
        """
        try:
            data = load_config(file_path)
        except yaml.YAMLError as e:
            messagebox.showerror(
                "YAML 解析错误",
                f"文件不是有效的 YAML 格式:\n{file_path}\n\n{str(e)}"
            )
            self.status_var.set(f"❌ YAML 解析失败: {file_path.name}")
            return False
        except Exception as e:
            messagebox.showerror(
                "文件读取错误",
                f"无法读取文件:\n{file_path}\n\n{str(e)}"
            )
            self.status_var.set(f"❌ 读取失败: {file_path.name}")
            return False

        errors = validate_config(data, file_path)
        if errors:
            error_msg = (
                f"文件校验未通过，不能编辑此文件:\n"
                f"{file_path}\n\n"
                + "\n".join(errors)
            )
            messagebox.showerror("配置文件校验失败", error_msg)
            self.status_var.set(f"❌ 校验失败: {file_path.name}")
            return False

        # 校验通过，加载
        self.current_file = file_path
        self.config_data = data
        self.original_data = copy.deepcopy(self.config_data)
        self.fields = build_field_list(self.config_data)
        self._refresh_ui()
        self.file_label_var.set(f"📄 {file_path.name}")
        self.status_var.set(f"✅ 已加载: {file_path}")
        return True

    def _open_file(self):
        """打开文件对话框，让用户选择一个配置文件"""
        if not self._check_unsaved():
            return
        file_path_str = filedialog.askopenfilename(
            title="选择 Sinmai-Assist 配置文件",
            filetypes=[
                ("YAML 文件", "*.yml *.yaml"),
                ("所有文件", "*.*"),
            ],
            initialdir=str(self.current_file.parent) if self.current_file else str(SCRIPT_DIR),
        )
        if not file_path_str:
            return  # 用户取消
        self._try_load_file(Path(file_path_str))

    def _build_section_tab(self, parent: ttk.Frame, fields: list, section: str):
        """在一个Tab内构建该section的所有字段"""
        # 滚动区域
        canvas = tk.Canvas(parent, highlightthickness=0)
        scrollbar = ttk.Scrollbar(parent, orient=tk.VERTICAL, command=canvas.yview)
        scroll_frame = ttk.Frame(canvas)

        scroll_frame.bind(
            "<Configure>",
            lambda e: canvas.configure(scrollregion=canvas.bbox("all")),
        )

        canvas.create_window((0, 0), window=scroll_frame, anchor=tk.NW)
        canvas.configure(yscrollcommand=scrollbar.set)

        # 鼠标滚轮支持（只在鼠标悬停时生效，避免事件泄漏）
        def _on_mousewheel(event):
            canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

        def _bind_scroll(event):
            canvas.bind_all("<MouseWheel>", _on_mousewheel)

        def _unbind_scroll(event):
            canvas.unbind_all("<MouseWheel>")

        canvas.bind("<Enter>", _bind_scroll)
        canvas.bind("<Leave>", _unbind_scroll)

        canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

        # 当前子分组标签
        current_subgroup = None
        subgroup_frame = None
        row = 0

        for field in fields:
            # 检测子分组（如 unityLogger, dummyLogin 等嵌套对象的键）
            path_parts = field["full_path"]
            if len(path_parts) >= 2:
                subgroup = path_parts[-2]  # 父键名
            else:
                subgroup = None

            # 如果子分组发生了变化，添加一个分隔标签
            if subgroup != current_subgroup:
                if subgroup_frame:
                    # 添加分隔线
                    ttk.Separator(scroll_frame, orient=tk.HORIZONTAL).grid(
                        row=row, column=0, columnspan=3, sticky=tk.EW, pady=(8, 4)
                    )
                    row += 1

                current_subgroup = subgroup
                subgroup_frame = scroll_frame

                # 子分组标题
                if subgroup and subgroup != section:
                    display_name = self._get_subgroup_display_name(subgroup, field)
                    label = ttk.Label(
                        scroll_frame,
                        text=f"▸ {display_name}",
                        font=("Microsoft YaHei", 10, "bold"),
                        foreground="#555",
                    )
                    label.grid(
                        row=row, column=0, columnspan=3, sticky=tk.W, pady=(10, 2), padx=10
                    )
                    row += 1

            # 构建行
            self._build_field_row(scroll_frame, field, row)
            row += 1

    def _get_subgroup_display_name(self, subgroup: str, field: dict) -> str:
        """获取子分组的中文显示名称"""
        # 从原始YAML注释中提取的友好名称
        name_map = {
            "unityLogger": "Unity 日志",
            "networkLogger": "网络日志",
            "customVersionText": "自定义版本文本",
            "dummyLogin": "虚拟登录",
            "customCameraId": "自定义摄像头ID",
            "changeGameSettings": "覆盖游戏设置",
            "singlePlayer": "单人模式",
            "unlockMusic": "解锁全部歌曲",
            "unlockMaster": "解锁Master/ReMaster难度",
            "unlockUtage": "解锁宴会场",
            "rewriteLoginBonusStamp": "重写每日登录奖励",
            "rewriteNoteJudgeTiming": "重写音符判定",
        }
        return name_map.get(subgroup, subgroup)

    def _build_field_row(self, parent: ttk.Frame, field: dict, row: int):
        """构建单个字段的编辑行"""
        path = field["path"]
        key = field["key"]
        value = field["value"]
        value_type = field["type"]

        # 缩进根据路径深度
        depth = len(field["full_path"])
        indent = "    " * (depth - 1) if depth > 1 else ""

        # 标签
        display_text = self._get_field_display_name(key, field)
        label = ttk.Label(parent, text=f"{indent}{display_text}", anchor=tk.W, width=35)
        label.grid(row=row, column=0, sticky=tk.W, padx=(10, 5), pady=2)

        # 输入控件
        if value_type == "bool":
            var = tk.BooleanVar(value=value)
            widget = ttk.Checkbutton(parent, variable=var)
            widget.grid(row=row, column=1, sticky=tk.W, pady=2)

        elif value_type == "int":
            var = tk.StringVar(value=str(value))
            widget = ttk.Entry(parent, textvariable=var, width=12)
            widget.grid(row=row, column=1, sticky=tk.W, pady=2)
            # 验证
            var.trace_add("write", lambda *a, v=var: self._validate_int(v))

        elif value_type == "float":
            var = tk.StringVar(value=str(value))
            widget = ttk.Entry(parent, textvariable=var, width=12)
            widget.grid(row=row, column=1, sticky=tk.W, pady=2)
            var.trace_add("write", lambda *a, v=var: self._validate_float(v))

        elif value_type == "str":
            var = tk.StringVar(value=str(value))
            widget = ttk.Entry(parent, textvariable=var, width=30)
            widget.grid(row=row, column=1, sticky=tk.W, pady=2)

        else:
            var = tk.StringVar(value=str(value))
            widget = ttk.Entry(parent, textvariable=var, width=20)
            widget.grid(row=row, column=1, sticky=tk.W, pady=2)

        self.widgets[path] = {
            "var": var,
            "field": field,
        }

        # 类型标签
        type_label = ttk.Label(
            parent,
            text=f"({value_type})",
            foreground="#aaa",
            font=("Microsoft YaHei", 8),
        )
        type_label.grid(row=row, column=2, sticky=tk.W, padx=5, pady=2)

    def _get_field_display_name(self, key: str, field: dict) -> str:
        """获取字段的中文显示名"""
        # 手动映射常见字段
        name_map = {
            "enable": "启用",
            "printToConsole": "输出到控制台",
            "autoBackupData": "自动备份玩家数据",
            "infinityTimer": "无限计时器",
            "infinityTimerLegacy": "无限计时器(传统方案)",
            "disableMask": "禁用遮罩",
            "disableBackground": "禁用背景",
            "showFPS": "显示FPS",
            "hideSubMonitor": "隐藏副屏",
            "forceQuickRetry": "强制快速重试(Freedom)",
            "forwardATouchRegionToButton": "触摸区域转发到按钮",
            "skipFade": "跳过过场动画",
            "skipWarningScreen": "跳过警告界面",
            "quickBoot": "快速启动",
            "blockCoin": "禁用点数消耗",
            "ignoreAnyGameInformation": "忽略游戏公告",
            "changeDefaultOption": "覆写游客默认设置",
            "changeFadeStyle": "切换过场动画样式",
            "versionText": "版本文本",
            "defaultUserId": "默认用户ID",
            "chimeCameraId": "Chime摄像头ID",
            "leftQrCameraId": "左侧二维码摄像头ID",
            "rightQrCameraId": "右侧二维码摄像头ID",
            "photoCameraId": "玩家摄像头ID",
            "codeRead": "二维码识别(DX Pass)",
            "iconPhoto": "拍摄头像",
            "uploadPhoto": "纪念照上传",
            "charaSelect": "角色选择界面",
            "autoPlay": "自动游玩",
            "fastSkip": "快速跳过",
            "chartController": "铺面控制器",
            "allCollection": "解锁全部收藏品",
            "unlockEvent": "解锁全部活动",
            "saveToUserData": "保存到用户存档",
            "unlockDoublePlayerMusic": "解锁双人模式歌曲",
            "resetLoginBonusRecord": "重置登录奖励记录",
            "forceCurrentIsBest": "强制当前为最佳成绩",
            "setAllCharacterAsSameAndLock": "设置相同旅行伙伴",
            "point": "登录奖励点数",
            "disableEnvironmentCheck": "禁用运行环境检查",
            "disableEncryption": "禁用加密",
            "disableReboot": "禁用自动重启",
            "disableIniClear": "禁止清除ini配置",
            "fixDebugInput": "修复DebugInput",
            "fixCheckAuth": "修复CheckAuth",
            "forceAsServer": "强制服务器模式",
            "skipCakeHashCheck": "跳过Cake Hash检查",
            "skipSpecialNumCheck": "跳过特殊数字检查",
            "skipVersionCheck": "跳过版本检查",
            "restoreCertificateValidation": "恢复证书验证",
            "adjustTiming": "音频延迟(ms)",
            "judgeTiming": "判定延迟(帧)",
            "safeMode": "安全模式",
            "showInfo": "显示信息",
            "showPanel": "显示面板",
            "maskTitleServerUrl": "遮挡标题服务器Url",
        }
        return name_map.get(key, key)

    def _button(self, parent, text, command, color):
        """创建样式化按钮"""
        style_name = f"{color}.TButton"
        ttk.Style().configure(
            style_name,
            foreground="white",
            background=color,
            font=("Microsoft YaHei", 9),
        )
        btn = ttk.Button(parent, text=text, command=command)
        return btn

    # --------------------------------------------------------
    # 验证
    # --------------------------------------------------------
    def _validate_int(self, var: tk.StringVar):
        """验证整数输入"""
        val = var.get()
        if val == "" or val == "-":
            return
        try:
            int(val)
        except ValueError:
            var.set("0")

    def _validate_float(self, var: tk.StringVar):
        """验证浮点数输入"""
        val = var.get()
        if val == "" or val == "-" or val == ".":
            return
        try:
            float(val)
        except ValueError:
            var.set("0.0")

    # --------------------------------------------------------
    # 数据读写
    # --------------------------------------------------------
    def _gather_values(self) -> dict:
        """从控件收集所有值，更新 config_data"""
        for path, info in self.widgets.items():
            var = info["var"]
            field = info["field"]
            full_path = field["full_path"]
            value_type = field["type"]

            raw_value = var.get()

            # 类型转换
            if value_type == "bool":
                new_value = bool(raw_value)
            elif value_type == "int":
                try:
                    new_value = int(raw_value)
                except ValueError:
                    new_value = 0
            elif value_type == "float":
                try:
                    new_value = float(raw_value)
                except ValueError:
                    new_value = 0.0
            else:
                new_value = str(raw_value)

            # 设置到嵌套字典
            self._set_nested(self.config_data, full_path, new_value)

    def _set_nested(self, data: dict, path: list, value):
        """设置嵌套字典中的值"""
        current = data
        for key in path[:-1]:
            if key not in current:
                current[key] = {}
            current = current[key]
        current[path[-1]] = value

    def _save(self):
        """保存配置到文件（保存前校验）"""
        if self.current_file is None:
            messagebox.showwarning("无法保存", "尚未加载有效的配置文件。\n请先点击「打开文件」选择一个配置文件。")
            return
        try:
            self._gather_values()
            # 保存前校验
            errors = validate_config(self.config_data, self.current_file)
            if errors:
                error_msg = "配置校验未通过，不能保存:\n\n" + "\n".join(errors)
                messagebox.showerror("校验失败", error_msg)
                self.status_var.set("❌ 校验失败，未保存")
                return
            save_config(self.current_file, self.config_data)
            self.original_data = copy.deepcopy(self.config_data)
            self.status_var.set("✅ 配置已保存!")
            messagebox.showinfo("保存成功", f"配置已保存到:\n{self.current_file}")
        except Exception as e:
            messagebox.showerror("保存失败", f"保存时发生错误:\n{str(e)}")
            self.status_var.set(f"❌ 保存失败: {e}")

    def _reload(self):
        """重新加载配置文件"""
        if self.current_file is None:
            messagebox.showwarning("无法重载", "尚未加载配置文件。\n请先点击「打开文件」选择一个配置文件。")
            return
        if not self._check_unsaved():
            return
        try:
            data = load_config(self.current_file)
            errors = validate_config(data, self.current_file)
            if errors:
                error_msg = "重新加载的配置文件校验未通过:\n\n" + "\n".join(errors)
                messagebox.showerror("校验失败", error_msg)
                return
            self.config_data = data
            self.original_data = copy.deepcopy(self.config_data)
            self.fields = build_field_list(self.config_data)
            self._refresh_ui()
            self.status_var.set("🔄 配置已重新加载")
        except Exception as e:
            messagebox.showerror("加载失败", f"重新加载时发生错误:\n{str(e)}")
            self.status_var.set(f"❌ 加载失败: {e}")

    def _undo(self):
        """撤销所有修改"""
        if not self._check_unsaved():
            return
        self.config_data = copy.deepcopy(self.original_data)
        self.fields = build_field_list(self.config_data)
        self._refresh_ui()
        self.status_var.set("↩ 已撤销所有修改")

    def _check_unsaved(self) -> bool:
        """检查是否有未保存的修改"""
        self._gather_values()
        if self.config_data != self.original_data:
            result = messagebox.askyesnocancel(
                "未保存的修改",
                "你有未保存的修改。\n\n"
                "是 = 保存后继续\n"
                "否 = 放弃修改并继续\n"
                "取消 = 返回",
            )
            if result is None:  # 取消
                return False
            if result:  # 是
                self._save()
            return True
        return True

    def _on_close(self):
        """关闭窗口"""
        self._gather_values()
        if self.config_data != self.original_data:
            result = messagebox.askyesnocancel(
                "未保存的修改",
                "你有未保存的修改，要保存吗?\n\n"
                "是 = 保存后退出\n"
                "否 = 放弃修改并退出\n"
                "取消 = 返回编辑",
            )
            if result is None:  # 取消
                return
            if result:  # 是
                self._save()
        self.root.destroy()

    def _refresh_ui(self):
        """刷新整个界面"""
        # 销毁 root 的所有子控件
        for widget in list(self.root.winfo_children()):
            widget.destroy()
        self.widgets.clear()
        self._build_ui()


# ============================================================
# 主入口
# ============================================================
def main():
    # 确定初始文件路径
    initial = None
    if CONFIG_PATH.exists():
        initial = CONFIG_PATH
    else:
        alt_path = SCRIPT_DIR / "Config.yml"
        if alt_path.exists():
            initial = alt_path

    root = tk.Tk()

    # 设置样式
    style = ttk.Style()
    style.theme_use("clam")

    # 设置字体
    default_font = ("Microsoft YaHei", 9)
    root.option_add("*Font", default_font)

    app = ConfigEditorApp(root, initial_file=initial)
    root.mainloop()


if __name__ == "__main__":
    main()
