# 代码库改进审查（SystemTools）

本轮审查基于静态阅读，重点关注了稳定性、可维护性、性能与安全边界。

## P0 / 高优先级

1. **摄像头帧对象生命周期风险（潜在内存泄漏）**
   - 位置：`Services/CameraCaptureService.cs`
   - 现状：`FrameCaptured` 事件通过 `frame.Clone()` 向外分发 `Mat`，但当前代码没有统一约束订阅方释放该对象；若订阅方未 `Dispose`，会持续占用非托管内存。
   - 建议：
     - 将事件参数改为托管可序列化对象（如 `byte[]`/`Bitmap`），或
     - 定义明确的资源所有权协议，并在文档中强制订阅方释放，或
     - 由服务层做帧池化管理，避免无界分配。

2. **阻塞式采集循环影响停机一致性**
   - 位置：`Services/CameraCaptureService.cs`
   - 现状：采集循环使用 `Thread.Sleep(33)`，`Stop()` 只 `Wait(500)`；在设备异常/卡住时，存在任务未完全退出的窗口。
   - 建议：改为 `await Task.Delay(33, token)` 的可取消等待，并在 `Stop()` 中更严格处理超时和异常（记录日志、避免静默失败）。

3. **模型解压后目录名硬编码（国际化/可移植性脆弱）**
   - 位置：`SettingsPage/SystemToolsSettingsViewModel.cs`
   - 现状：整理目录时写死了 `"新建文件夹"` 作为来源目录名，依赖压缩包内部结构和中文目录名称。
   - 建议：解压后按“预期文件名模式”查找目标（例如检测 `.dat` 文件），避免依赖固定父目录名。

## P1 / 中优先级

4. **重复创建 `HttpClient`，可复用性不足**
   - 位置：`SettingsPage/SystemToolsSettingsViewModel.cs`
   - 现状：两个下载方法内均 `new HttpClient()`。
   - 建议：使用 `IHttpClientFactory` 或共享静态 `HttpClient`（带合理超时），便于连接复用与统一策略（重试、代理、证书）。

5. **`async void` 事件处理器过多，异常可观测性弱**
   - 位置：`Triggers/UsbDeviceTrigger.cs`、`SettingsPage/SystemToolsSettingsPage.axaml.cs` 等
   - 现状：存在多个 `async void`；在非 UI 框架托管场景中，异常可能直接升级为未处理异常或仅丢日志。
   - 建议：
     - 对必须 `async void` 的 UI 事件统一包装异常处理与日志；
     - 业务层尽量改为返回 `Task` 的异步链路。

6. **外部进程调用分散，错误处理风格不一致**
   - 位置：`Actions/*Action.cs` 多处
   - 现状：大量 `Process.Start` 直接调用，部分检查退出码、部分不检查，异常文案与日志粒度不统一。
   - 建议：抽象统一的 `IProcessRunner`（超时、退出码策略、标准输出/错误、结构化日志），减少重复并提升可测试性。

## P2 / 低优先级

7. **吞异常场景可增加最小日志**
   - 位置：`Services/CameraCaptureService.cs`
   - 现状：`catch { }` 直接吞掉异常（虽然注释说明了原因）。
   - 建议：至少记录 debug 级别日志，便于排查偶发硬件问题。

8. **重复文件操作逻辑可下沉复用**
   - 位置：`Actions/CopyAction.cs`、`Actions/MoveAction.cs`、`Actions/DeleteAction.cs`
   - 现状：路径校验、异常转换、日志模板在多文件重复。
   - 建议：提取共享工具层（路径规范化、重试策略、统一错误码），降低维护成本。

---

## 建议的下一步落地顺序

1. 先做 `CameraCaptureService` 的取消与资源管理重构（P0）。
2. 修复模型解压目录硬编码（P0）。
3. 引入统一进程执行器并迁移高频动作（P1）。
4. 最后做 `HttpClient` 复用和文件操作抽象（P1/P2）。
