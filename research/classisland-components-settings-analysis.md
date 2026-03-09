# ClassIsland「设置 → 组件」页面实现学习笔记

> 目标：梳理 ClassIsland 在组件配置页面中“拖动组件排布顺序”“添加组件”“调整行内/跨行组件”“容器组件子级编辑”的核心实现路径，便于在本项目复用。

## 1. 页面结构（UI）

ClassIsland 的组件设置页面核心由三层组成：

1. **主界面行列表**（每一行代表 MainWindow 的一行）。
2. **每行中的横向组件列表**（支持拖拽排序、右键操作）。
3. **容器组件子组件列表**（进入容器后编辑 children）。

对应文件：

- `ClassIsland/Views/SettingPages/ComponentsSettingsPage.axaml`
- `ClassIsland/Views/SettingPages/ComponentsSettingsPage.axaml.cs`
- `ClassIsland/ViewModels/SettingsPages/ComponentsSettingsViewModel.cs`

## 2. 拖动排序的关键机制

### 2.1 数据模型设计

拖动统一围绕 `ComponentSettings` 集合进行：

- 主界面每一行：`MainWindowLineSettings.Children : ObservableCollection<ComponentSettings>`
- 容器组件子项：`IComponentContainerSettings.Children : ObservableCollection<ComponentSettings>`

这样“行内排序”和“容器内排序”都能走同一套 DropHandler 逻辑。

### 2.2 拖拽上下文（Drag Context）

页面给每个 `ListBoxItem` 配置了 `AdvancedManagedContextDragBehavior`，并通过 `MultiBinding` 构造拖拽上下文对象 `EditableComponentsListBoxDragData`：

- 当前被拖动的 `ComponentSettings`
- 源列表 `SourceList`

这使 Drop 端能区分：

- 组件库拖入（`ComponentInfo`，Copy）
- 现有组件重排（`EditableComponentsListBoxDragData`，Move）

### 2.3 DropHandler 算法要点

`ComponentsSettingsPageDropHandler` 实现要点：

1. **目标索引计算**：根据指针在目标 item 左半/右半决定插入前后。
2. **同列表移动**：调用 `MoveItem`，并根据源索引和目标索引修正最终位置。
3. **跨列表移动**：从 `sourceList` 移除后插入目标 `components`。
4. **组件库拖入**：新建 `ComponentSettings`，插入后调用 `LoadComponentSettings` 初始化默认设置。

### 2.4 防止非法拖动

针对容器编辑场景，代码专门阻止“把根容器拖到自己的 children 中”导致循环结构：

- `Validate` 阶段拦截。
- `Drop` 阶段二次保护。

这是容器嵌套编辑中非常关键的稳定性策略。

## 3. 添加组件与调整行内组件

### 3.1 从组件库添加

`AddSelectedComponentToMainLines(ComponentInfo info)`：

- 目标优先是当前选中的行 `SelectedMainWindowLineSettings.Children`
- 未选中时回退到第一行
- 新建 `ComponentSettings(Id=info.Guid)` 并初始化默认设置

### 3.2 行内顺序调整

#### 方式 A：直接拖拽

同一 `Children` 集合内拖放即完成重排。

#### 方式 B：右键菜单命令

页面对组件项提供：

- 向上移动一行（`MoveComponentToPreviousLine`）
- 向下移动一行（`MoveComponentToNextLine`）

如果没有上一行/下一行，会自动创建新行并插入组件。

### 3.3 跨层级移动

- `MoveToCurrentContainerComponent`：把主行组件移动到当前打开的容器 children。
- `MoveComponentsToMainLines`：把容器组件移回主行。

这套“主行 ↔ 容器 children”双向迁移命令，是“调整行内组件与层级”的关键 UX。

## 4. 容器组件编辑（子视图）

核心状态在 ViewModel：

- `IsComponentChildrenViewOpen`
- `SelectedComponentContainerChildren`
- `SelectedRootComponent`
- `ChildrenComponentSettingsNavigationStack`

`SetCurrentSelectedComponentContainer` 会：

1. 校验目标是否容器组件。
2. 懒加载 settings（`JsonElement` → 强类型容器设置）。
3. 将容器 children 绑定到右侧子组件列表。
4. 维护导航栈，支持“返回上一层容器”。

因此 ClassIsland 支持多层容器嵌套的可视化编辑。

## 5. 可复用到 SystemTools 的实现建议

如果在 SystemTools 做类似“组件行编辑器”，建议最小落地方案：

1. 统一组件集合类型为 `ObservableCollection<ComponentSettingsLike>`，主行与容器子项复用。
2. 封装一个 DropHandler，统一处理：
   - `ComponentInfoLike`（新增）
   - `DragData(sourceList + item)`（移动）
3. 在 `Validate + Drop` 双重拦截自引用拖动。
4. 提供右键命令作为拖拽的补充（上移/下移/进容器/出容器/复制）。
5. 增加“自动创建上一行/下一行”逻辑，减少用户手工操作。

## 6. 参考源码定位（ClassIsland）

- 页面 UI 与交互绑定：
  - `ClassIsland/Views/SettingPages/ComponentsSettingsPage.axaml`
- 事件处理与命令：
  - `ClassIsland/Views/SettingPages/ComponentsSettingsPage.axaml.cs`
- 拖放处理：
  - `ClassIsland/Views/SettingPages/ComponentsSettingsPageDropHandler.cs`
- 页面状态与 DropHandler 注入：
  - `ClassIsland/ViewModels/SettingsPages/ComponentsSettingsViewModel.cs`
- 拖拽上下文结构：
  - `ClassIsland/Controls/EditMode/EditableComponentsListBoxDragData.cs`

---

如果你愿意，我下一步可以直接按这个方案在 SystemTools 内搭一个最小可运行的“组件行编辑器”原型（含拖拽排序 + 添加组件 + 容器内外移动）。
