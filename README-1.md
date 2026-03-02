## SystemTools

### 为 ClassIsland 提供多彩而丰富的更多 组件/行动/触发器/实用工具！

> **注意：*部分功能需要在 SystemTools 设置中启用相关选项或下载依赖才能使用*。**
>
>  - 行动 **[禁用鼠标]、[启用鼠标]** 需在 *[ SystemTools 设置 > 主设置 ]* 中启用 **[实验性功能]** 才能使用
>
>  - 行动 **[摄像头抓拍]** 需在 *[ SystemTools 设置 > 主设置 ]* 中点击下载依赖并启用 **[启用需要添加依赖的功能]** 才能使用
>
>  - 组件 **[音乐软件歌词显示]** 使用前请确保音乐软件已启用桌面歌词显示（横排/单行/关闭翻译）,且不贴近屏幕上侧边缘

---

### 目前实现的功能

<details  open>
<summary>设置页面：</summary>

- SystemTools 设置
    - 主设置
        - 实验性功能（启用后可使用“实验性功能”行动组中的行动）
        - 启用需要添加依赖的功能（启用后可使用需要添加依赖的行动，如“摄像头抓拍”）
    - 关于
        - 帮助（还没写完）
        - 插件介绍（点击查看插件介绍）
        - 更新日志（点击查看最近一次更新日志）
</details>

<details  open>
<summary>组件：</summary>

- 网络延迟检测（检测连接到指定网址的延迟）
- 音乐软件歌词显示（目前已适配网易云音乐/QQ音乐/酷狗音乐/汽水音乐/Lyricify Lite，支持设置歌词缩放和刷新率，部分提供桌面歌词强制置底功能 *实验性* ）
</details>

<details  open>
<summary>触发器：</summary>

- USB设备插入时（支持检查是否是U盘设备）
- 按下自定义热键时（可自定义热键组合，支持 *[Ctrl/Alt/Shift/Win+按键]* 或除功能键外 *[任何单独按键]*
  ，可在多个自动化中使用相同的热键触发器，触发时会执行所有包含相应热键触发器的自动化）
- 行动进行时（与“触发指定触发器”行动联动使用）
</details>

<details  open>
<summary>行动：</summary>

- 触发指定触发器（与”行动进行时“触发器联动使用） **（该行动在ClassIsland全局中只能使用一次）**
- 重启应用为管理员身份

**模拟操作…**

- 常用模拟键
    - 按下 Alt+F4
    - 按下 Alt+Tab
    - 按下 Enter 键
    - 按下 Esc 键
    - 按下 F11 键
- 键入内容
- 模拟鼠标（左键/右键/拖动/滚轮）（支持运行期间禁用鼠标输入）
- 模拟键盘
- 窗口操作（最大化/最小化/向下还原/关闭窗口）

**显示设置…**

- 复制屏幕
- 扩展屏幕
- 仅电脑屏幕
- 仅第二屏幕
- 黑屏html

**电源选项…**

- 计时关机
- 取消关机计划
- 锁定屏幕

**文件操作…**（支持文件/文件夹）

- 复制
- 移动
- 删除

**系统个性化…**

- 切换壁纸
- 切换主题色

**实用工具…**

- 全屏时钟
- 退出进程
- 摄像头抓拍
- 屏幕截图
- 禁用硬件设备
- 启用硬件设备

**其他工具…**

- 沉浸式时钟

**实验性功能…**

- 禁用鼠标
- 启用鼠标
</details>

#### ……欢迎据班级电脑实际使用情况提出更多功能请求

---

### 声明
#### 贡献者：
**感谢以下人员对本仓库做出的贡献：**

<a href="https://github.com/Programmer-MrWang/SystemTools/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Programmer-MrWang/SystemTools" />
</a>

- 该插件仅适用于Windows。
- **这个插件适用于 ClassIsland 2.x（≥2.0.0.1）版本。**
- LGPLv3 许可。[LICENSE](./LICENSE)
- 开发者：[@Programmer-MrWang](https://github.com/Programmer-MrWang)/与Kimi AI共同编写。
- 其他工具“沉浸式时钟”服务由 QQHKX 提供。

> [“沉浸式时钟”网址](https://clock.qqhkx.com/)  |
> [“沉浸式时钟”项目仓库](https://github.com/QQHKX/immersive-clock)