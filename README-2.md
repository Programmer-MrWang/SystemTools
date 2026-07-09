## SystemTools v2.5.1.0 - 2026.6.22更新

### 🚀 新增：

- **[组件]**: 新增 **LED 文本仿真显示框** 组件
  >一个仿LED风格的跑马灯文本滚动组件

- **[组件]**: **更好的轮播容器** 组件新增动画样式、新增可设置进度条位置

- **[组件]**: **本地一言** 组件新增随机轮播模式、提升最大轮播时长上限、显示轮播进度条、记忆轮播进度功能 https://github.com/Programmer-MrWang/SystemTools/issues/83 https://github.com/Programmer-MrWang/SystemTools/issues/69 https://github.com/Programmer-MrWang/SystemTools/issues/53 https://github.com/Programmer-MrWang/SystemTools/issues/52

- **[规则集]**: 新增 **正在使用某课程表** 、 **正在使用某时间表** 、 **是否在某时间段** 、 **正在播放媒体音乐** 规则集
  > 可选择特定课程表/时间表并检测当前是否为该规则
  > 可设置特定时间区间并检测是否处于该时间段内
  > 通过检查SMTC信息检测是否有媒体音频正在播放

- **[行动]**: 新增 **调整屏幕亮度** 行动 https://github.com/Programmer-MrWang/SystemTools/issues/84
  > 此功能使用WMI调用，需要显示器支持亮度调整。台式机外接显示器通常不支持此功能。在某些设备上可能需要管理员权限。
  >
  > 位于 *[ SystemTools 行动 > 显示设置… > 调整屏幕亮度 ]*

- **[行动]**: 新增 **显示桌面** 行动 https://github.com/Programmer-MrWang/SystemTools/issues/80
  > 触发后可直接显示桌面
  >
  > 位于 *[ SystemTools 行动 > 显示设置… > 显示桌面 ]*

- **[行动]**: 为“模拟鼠标”和"模拟键盘"添加 **手动编辑功能** https://github.com/Programmer-MrWang/SystemTools/issues/75
  > 支持在录制完成行动后手动编辑修改内容

- **[行动]**: **切换壁纸** 行动添加纯色显示功能、新增图片填充方式

- **[行动/功能]**: **高级计时关机** 行动/功能 美化显示UI https://github.com/Programmer-MrWang/SystemTools/pull/51

- **[行动]**: 新增 **后台播放音乐** 行动
  > 位于 *[ SystemTools 行动 > 媒体工具… > 后台播放音乐 ]*

- **[行动]**: 新增 **加载临时课表** 、 **打开应用设置** 、 **打开档案编辑** 、 **打开换课窗口** 行动
  > “加载临时课表”行动支持启用恢复
  >
  > 位于 *[ SystemTools 行动 > ClassIsland… >  ]*

- **[行动]**: 新增 **开关自动化** 行动 https://github.com/Programmer-MrWang/SystemTools/pull/81
  > 可控制其他自动化条目的开关状态、支持恢复状态
  >
  > 位于 *[ SystemTools 行动 > ClassIsland… > 开关自动化 ]*

- **[行动]**: **显示悬浮窗** 行动支持恢复

- **[功能]**: 在 ClassIsland托盘菜单 > 更多选项… 添加 **显示/隐藏悬浮窗** 选项

- **[功能]**: 新增 **自动播放** 功能
  > 开启后可在U盘设备插入时自动打开U盘
  >
  > 位于 *[ 主设置 > 更多功能选项… > 自动播放 ]*

- **[功能]**: 新增 **自动清理 Classlsland 内存** 功能
  > 当软件内存占用超过500MB时自动执行垃圾回收与工作集修剪
  >
  > 位于 *[ 主设置 > 更多功能选项… > 自动清理 Classlsland 内存 ]*

### 🐛 Bug 修复：

- **[行动/功能]**: 修复高级计时关机的关机计划取消逻辑问题
- **[行动/功能]**: 修复高级计时关机悬浮窗UI显示不全问题 https://github.com/Programmer-MrWang/SystemTools/issues/43
- **[组件]**: 修复 **下节课是** 组件的下一节课判断逻辑问题
- **[组件]**: 修复 **网络延迟检测** 组件“无网络”、“超时”抖动问题 https://github.com/Programmer-MrWang/SystemTools/pull/62
- **[触发器]**: 修复复制触发器“从悬浮窗启动”的自动化后，会立刻导致报错崩溃问题 https://github.com/Programmer-MrWang/SystemTools/issues/78

### ◀️ 调整/优化：

- **[行动]**: 调整 **调整系统音量** 、 **摄像头抓拍** 行动位置到 **媒体工具…** 下

---
### 贡献者：
- 感谢以下贡献者的贡献：
  @Programmer-MrWang  @arcwolf1  @diann34 @ywydog @RainNing117 