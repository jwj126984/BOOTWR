# CAN连接闪退问题修复计划

## 问题分析

经过代码审查，发现以下几个可能导致软件闪退的关键问题：

### 问题1: 异步接收循环缺乏完整异常处理
**位置**: `CanCommunicationService.StartReceiveLoop()` (第207-265行)

虽然代码中有 `try-catch` 块，但存在以下缺陷：
- 异常仅被捕获后简单 `break`，没有日志记录
- 事件触发时（`MessageReceived?.Invoke()`）若订阅者抛出异常，会导致崩溃
- 线程中断异常可能未被正确处理

### 问题2: 事件触发时缺少空检查和异常隔离
**位置**: `CanCommunicationService.StartReceiveLoop()` (第243行)

当 `MessageReceived` 事件触发时，如果订阅者（如 `MainWindowViewModel`）的事件处理程序抛出异常，该异常会直接传播到接收循环线程，导致整个应用崩溃。

### 问题3: UI线程调度可能在应用未就绪时执行
**位置**: `MainWindowViewModel.MessageManager_MessageAdded()` (第138-144行)

如果在应用初始化完成前触发事件，`App.Current` 可能为 `null`，导致空引用异常。

### 问题4: 并发访问风险
`_isConnected` 标志的检查和使用之间存在竞态条件，可能导致在已断开连接的状态下调用CAN API。

## 修复方案

### 1. 增强接收循环的异常处理
- 在 `StartReceiveLoop()` 中添加更完整的异常处理
- 使用 `try-catch` 包装事件触发，防止订阅者异常传播
- 添加日志记录以便追踪问题

### 2. 在事件触发前检查订阅者
- 使用局部变量缓存事件委托，防止空引用
- 将事件触发包装在独立的 `try-catch` 块中

### 3. 保护UI线程调度
- 在访问 `App.Current.Dispatcher` 前添加空检查
- 使用 `Dispatcher.BeginInvoke` 替代 `Invoke` 以避免死锁

### 4. 添加连接状态检查保护
- 在调用CAN API前再次验证连接状态
- 使用更严格的锁保护

## 文件修改

| 文件 | 修改内容 |
|------|----------|
| `Services/CanCommunicationService.cs` | 增强 `StartReceiveLoop()` 的异常处理；添加事件触发保护 |
| `ViewModels/MainWindowViewModel.cs` | 在 `MessageManager_MessageAdded` 和 `LogService_LogAdded` 中添加空检查 |

## 风险评估

- **低风险**: 所有修改都是防御性的，不会改变现有功能逻辑
- **兼容性**: 修改保持向后兼容，不影响现有API
- **测试建议**: 需要测试连接、断开连接、消息接收等场景

---

**计划创建时间**: 2026-05-25