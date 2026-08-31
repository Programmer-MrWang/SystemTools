<div align="center">

<img src="https://github.com/user-attachments/assets/d81127fb-1b17-412f-90ac-a3b008c11a5c" alt="SystemTools Logo" height="100">

# SystemTools

<img width="6111" height="1547" alt="SystemTools - Hoshimi Miyabi" src="https://github.com/user-attachments/assets/a08b0c8d-72ee-4e48-a564-eba3765750d7" />

[![GitHub](https://img.shields.io/badge/GitHub-%23121011.svg?logo=github&logoColor=white)](https://github.com/Programmer-MrWang/SystemTools)
[![Gitee](https://img.shields.io/badge/Gitee-FC6D26?logo=gitee&logoColor=fff)](https://gitee.com/Programmer_Wang/SystemTools)

![GitHub Forks](https://img.shields.io/github/forks/Programmer-MrWang/SystemTools)
![GitHub Watchers](https://img.shields.io/github/watchers/Programmer-MrWang/SystemTools)
![GitHub Repo Stars](https://img.shields.io/github/stars/Programmer-MrWang/SystemTools)

**为 ClassIsland 提供多彩而丰富的组件、行动、规则集、触发器、实用工具与 AI 功能！**

![RepoBeats](https://repobeats.axiom.co/api/embed/d7ed2cf283c8ab3457f5a01ec214c458d0e34190.svg)

![GitHub License](https://img.shields.io/github/license/Programmer-MrWang/SystemTools)
![GitHub top language](https://img.shields.io/github/languages/top/Programmer-MrWang/SystemTools)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/Programmer-MrWang/SystemTools/total)

![GitHub Release Date](https://img.shields.io/github/release-date-pre/Programmer-MrWang/SystemTools)
![GitHub Release](https://img.shields.io/github/v/release/Programmer-MrWang/SystemTools?include_prereleases)
![GitHub Repo Size](https://img.shields.io/github/repo-size/Programmer-MrWang/SystemTools)
[![Visitors](https://api.visitorbadge.io/api/visitors?path=https%3A%2F%2Fgithub.com%2FProgrammer-MrWang%2FSystemTools&countColor=%23263759&style=flat)](https://visitorbadge.io/status?path=https%3A%2F%2Fgithub.com%2FProgrammer-MrWang%2FSystemTools)
![GitHub Commit Activity](https://img.shields.io/github/commit-activity/t/Programmer-MrWang/SystemTools)
![GitHub Commits Since Latest Release](https://img.shields.io/github/commits-since/Programmer-MrWang/SystemTools/latest)
![GitHub Created At](https://img.shields.io/github/created-at/Programmer-MrWang/SystemTools)

</div>

- 本仓库已开启 Discussions 板块，欢迎提出疑问、展示使用方式或提交功能建议。

> [!WARNING]
> - 此插件仅适用于 Windows x64 平台。
> - 当前版本为 **SystemTools 3.0.0.0**，插件清单要求 **ClassIsland 插件 API 2.2.0.0**。使用前请确认 ClassIsland 版本满足该 API 要求。

> [!NOTE]
> - 由于不同 Windows 版本、硬件和驱动环境存在差异，亮度调整、显示器切换、摄像头、设备控制、Windows Hello、语音识别等功能可能无法在所有设备上正常工作。如遇问题，请提交 issue，并附上日志、诊断信息、Windows 版本和硬件环境。
> - 在主设置中更改实验性功能、悬浮窗、AI 服务、依赖功能或组件/行动/触发器/规则集的启用状态后，需要点击 **“应用并重启”** 才能完成注册或卸载。

> [!IMPORTANT]
> - 行动 **[禁用鼠标]、[启用鼠标]** 仅在 *[SystemTools 设置 > 主设置 > 实验性功能]* 启用后可用。
> - 行动 **[摄像头抓拍]** 需要先在 *[SystemTools 设置 > 主设置 > 启用扩展功能]* 下载 FFmpeg 依赖，并启用需要 FFmpeg 的功能。
> - **人脸识别验证器** 需要下载人脸识别模型及运行时；**Windows Hello 验证器** 需要 Windows 11（build 22000 及以上），并提前为当前 Windows 用户配置 Windows Hello。Windows 安全界面可能回退到指纹或 PIN。
> - **关键词触发器、AI 语音输入和语音唤醒** 依赖可用的麦克风与语音识别环境。AI 语音功能还需要下载 VoskWorker 和一个经 SystemTools 认证的语音识别模型。
> - **AI 功能** 需要用户自行配置兼容 OpenAI API 格式的服务地址、API Key 与模型。内容由 AI 生成，请仔细甄别；涉及 ClassIsland 配置或行动执行的操作会在界面中请求确认。

## 目前实现的功能

<details open>
<summary>设置页面：</summary>

- **主设置**
  - 按组件、行动、触发器和规则集分别启用或禁用功能，并通过“应用并重启”应用设置
  - 启用实验性功能、悬浮窗功能和更多功能选项
  - 配置兼容 OpenAI 格式的 AI 服务：供应商名称、API Key、API 地址和模型
  - 配置 AI 唤醒词、语音唤醒开关，以及 AI 对话窗口的磨砂/液态玻璃外观
  - 下载和管理 FFmpeg、人脸识别模型及运行时、VoskWorker 与语音识别模型
  - 启用人脸识别验证器和 Windows Hello 验证器
- **更多功能选项**
  - 根据主界面背后画面亮度自动切换 ClassIsland 明暗主题
  - 检测主界面覆盖区域文字，并在遮挡文字时自动隐藏主界面
  - ClassIsland 内存超过 500 MB 时自动执行垃圾回收与工作集修剪
  - 以管理员权限按内存占用阈值自动清理系统内存，或手动执行一键清理
  - U 盘插入后自动打开驱动器
  - 虚拟放学事件功能，并在指定时长内覆盖 ClassIsland 课程状态
- **AI 对话**
  - 管理多段对话历史，支持新建、重命名和删除会话
  - 支持流式对话、语音输入、文件附件与拖放添加附件
  - 可将 AI 回复共享到 ClassIsland 通知
  - 可在用户确认后读取或修改部分 ClassIsland 档案设置，以及执行受支持的 ClassIsland 行动
- **悬浮窗编辑**
  - 创建、切换和删除多套悬浮窗配置方案
  - 通过拖拽调整按钮顺序、跨行布局、显示状态和按钮行
  - 设置经典或液态玻璃外观，以及缩放、图标大小、文字大小、透明度、阴影和拖动把手
  - 选择跟随 ClassIsland、明亮、黑暗或自适应背景主题
  - 设置悬浮窗置顶/置底、层级刷新频率，并按规则集自动隐藏
- **关于**
  - 查看帮助、插件介绍、更新日志和 Lyricify Lite 适配说明
  - 提供问题反馈、功能建议、调研问卷和插件社群入口
- **插件调试**
  - 细调液态玻璃的折射、模糊、色彩、渐进效果、自适应亮度、高光与阴影参数
  - 调试 AI 语音唤醒和操作确认界面

</details>

<details open>
<summary>主题：</summary>

- **Card-type Component**
  - 提供更高的主界面组件布局，并为主界面预留 20 px 垂直安全区域

</details>

<details open>
<summary>组件：</summary>

- **网络延迟检测**：支持 Ping 与 HTTP 检测模式，可设置目标网址和显示前缀
- **音乐软件歌词显示**：适配网易云音乐、QQ 音乐、酷狗音乐、汽水音乐和 Lyricify Lite；支持歌词缩放、刷新率及部分歌词窗口强制置底功能
- **显示剪切板内容**：在 ClassIsland 主界面实时显示剪切板文本
- **本地一言**：从 txt 文件读取内容，支持顺序/随机轮播、翻页动画、轮播进度记忆与进度条
- **下节课是**：显示下一节课程、课程时间段和任教老师
- **更好的轮播容器**：支持为每个组件分别设置显示时长、顺序策略、切换动画、进度条位置、低频刷新和手动切换按钮
- **LED 文本仿真显示框**：以 LED 跑马灯样式滚动显示自定义文本，可设置宽度、滚动速度和相关显示选项

</details>

<details open>
<summary>触发器：</summary>

- **从悬浮窗触发**

  > 可自定义按钮图标、文字与按钮 ID，并将多个按钮按行编排到悬浮窗中。
  >
  > 启用恢复时，可再次点击按钮执行恢复；按钮处于恢复状态时，可右键退出恢复状态而不触发恢复。
  >
  > 悬浮窗支持多配置方案、经典/液态玻璃外观、主题与层级设置、规则隐藏，以及托盘菜单显示/隐藏。
- **USB设备插入时**：支持限定为 U 盘设备
- **按下自定义热键时**：支持 `Ctrl` / `Alt` / `Shift` / `Win` 组合键或单独按键；相同热键可触发多个自动化
- **行动进行时**：与 **[触发指定触发器]** 行动联动使用
- **长时间未操作电脑时**：指定时间内无操作时触发；启用恢复后，可在再次操作电脑时恢复
- **关键词触发**：持续识别指定关键词，可调整识别灵敏度；需要 Windows 中文语音识别支持
- **点击主界面时**：左键点击任意可见的 ClassIsland 主界面行区域时触发，保留穿透点击，不支持恢复

</details>

<details open>
<summary>规则集：</summary>

- **程序正在运行**：判断指定进程是否正在运行
- **正在使用某课程表**：判断当前是否使用指定课程表
- **正在使用某时间表**：判断当前是否使用指定时间表
- **是否在某时间段**：判断当前时间是否处于指定区间
- **正在播放媒体音乐**：通过系统媒体传输控制信息判断是否正在播放媒体音乐

</details>

<details open>
<summary>行动：</summary>

**模拟操作…**

- 常用模拟键
  - **按下 Alt+F4**
  - **按下 Alt+Tab**
  - **按下 Ctrl+Z**
  - **按下 Enter 键**
  - **按下 Esc 键**
  - **按下 F11 键**
- 键入内容
- 模拟鼠标：左键、右键、拖动和滚轮，支持运行期间禁用鼠标输入
- 模拟组合键：支持录入 2~5 个按键并同时按下，未录入的行会自动跳过
- 模拟键盘：支持录制后手动编辑按键内容
- 窗口操作：最大化、最小化、向下还原或关闭窗口

**显示设置…**

- 复制屏幕
- 扩展屏幕
- 仅电脑屏幕
- 仅第二屏幕
- **黑屏html**
- 显示桌面
- 调整屏幕亮度

**电源选项…**

- 计时关机
- 高级计时关机：显示独立倒计时界面，支持立即关机、延迟、取消计划和已阅
- 取消关机计划
- 锁定屏幕
- 立即重启
- 立即关机
- 睡眠

> 高级计时关机的外观设计灵感来源：[GitHub 项目“waity”](https://github.com/Xwei1645/waity)

**文件操作…**（支持文件和文件夹）

- 复制
- 移动
- 删除

**系统个性化…**

- 切换壁纸：支持图片和纯色壁纸，以及多种图片填充方式
- 切换主题色

**实用工具…**

- 退出进程
- 屏幕截图
- **拉起自定义Windows通知**
- 禁用硬件设备
- 启用硬件设备

**悬浮窗设置…**

- **显示悬浮窗**：显示或隐藏悬浮窗，支持恢复
- **切换悬浮窗层级**：切换或设置为置顶/置底，支持恢复
- **切换悬浮窗配置方案**：切换到下一套或指定方案，支持恢复
- **切换悬浮窗主题**：切换到下一种或指定主题，支持恢复

**媒体工具…**

- 后台播放音频
- 设置系统音量
- 摄像头抓拍（需要启用 FFmpeg 扩展功能并安装依赖）

**更多功能选项…**

- 自动切换 ClassIsland 主题：通过行动启用或关闭该功能
- 遮挡文字时隐藏主界面：通过行动启用或关闭该功能
- 自动播放：通过行动启用或关闭 U 盘插入后自动打开功能

**高级自动化工具…**

- 行动流执行确认：执行到此行动时可选择立即执行、延迟执行或停止整个行动流
- 触发指定触发器：与 **[行动进行时]** 触发器联动使用
- 开关自动化：启用、禁用或切换指定自动化，支持恢复

**AI 功能…**

- **启用语音唤醒 AI**：启用或关闭语音唤醒
- **唤醒语音对话 AI**
- **显示AI对话框**

**其他工具…**

- 沉浸式时钟

**ClassIsland…**

- 清除全部提醒
- 重启应用为管理员身份
- 加载临时课表（支持恢复）
- 打开应用设置
- 打开档案编辑
- 打开换课窗口

**实验性功能…**

- 禁用鼠标
- 启用鼠标

</details>

## 计划实现的功能

### 欢迎根据班级电脑的实际使用情况提出更多功能请求

---

## 声明

### 贡献者

**感谢以下人员对本仓库做出的贡献：**

<a href="https://github.com/Programmer-MrWang/SystemTools/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Programmer-MrWang/SystemTools" alt="SystemTools Contributors" />
</a>

- 该插件仅适用于 Windows x64。
- 当前插件版本为 **3.0.0.0**，插件清单 API 版本为 **2.2.0.0**。
- 本项目采用 [GNU GPLv3](./LICENSE) 许可证。
- “沉浸式时钟”服务由 QQHKX 提供。
- 液态玻璃效果包含并使用 `LiquidGlassAvaloniaUI` 相关代码，详见 [`ThirdParty/LiquidGlassAvaloniaUI/LICENSE`](./ThirdParty/LiquidGlassAvaloniaUI/LICENSE)。

> [“沉浸式时钟”网址](https://clock.qqhkx.com/) |
> [“沉浸式时钟”项目仓库](https://github.com/QQHKX/immersive-clock)
