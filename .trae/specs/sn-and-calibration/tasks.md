# SN管理与热电偶标定功能 - 实现计划

## [x] Task 1: 创建SN管理服务 (SnService.cs)
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 创建SN管理服务类，实现SN读写功能
  - 实现分段写入逻辑（最多31字符）
  - 实现分段读取逻辑
  - 复用FlashingService中的会话管理逻辑
- **Acceptance Criteria Addressed**: AC-1, AC-2
- **Test Requirements**:
  - `programmatic` TR-1.1: SN写入命令正确生成（CMD 0x16）
  - `programmatic` TR-1.2: SN读取命令正确生成（CMD 0x17）
  - `programmatic` TR-1.3: 分段写入超过5字节的SN正确处理
- **Notes**: SN最大长度31字符，写入后需调用TC_CALIB_SAVE(0x13)才能保存到Flash

## [x] Task 2: 创建热电偶标定服务 (CalibrationService.cs)
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 创建热电偶标定服务类
  - 实现TC_CALIB_WRITE(0x12): 写k、b、vref到RAM
  - 实现TC_CALIB_READ(0x15): 读RAM中的k、b、vref
  - 实现TC_CALIB_SAVE(0x13): 保存RAM到Flash
  - 实现TC_CALIB_DEFAULTS(0x14): 恢复默认值
  - 实现TC_SAMPLE_READ(0x18): 读取采集电压
- **Acceptance Criteria Addressed**: AC-3, AC-4, AC-5, AC-6
- **Test Requirements**:
  - `programmatic` TR-2.1: 标定参数写入命令正确生成（CMD 0x12）
  - `programmatic` TR-2.2: 标定参数读取命令正确生成（CMD 0x15）
  - `programmatic` TR-2.3: 采集电压读取命令正确生成（CMD 0x18）
  - `programmatic` TR-2.4: float32大端序转换正确
- **Notes**: 支持3片AFE，每片8通道，需要处理大端序float转换

## [x] Task 3: 更新ViewModel添加新功能
- **Priority**: P1
- **Depends On**: Task 1, Task 2
- **Description**: 
  - 在MainWindowViewModel中集成SnService和CalibrationService
  - 添加SN读写相关的属性和命令
  - 添加热电偶标定相关的属性和命令
  - 添加会话管理命令（解锁/退出）
- **Acceptance Criteria Addressed**: 所有AC
- **Test Requirements**:
  - `human-judgement` TR-3.1: ViewModel正确引用新服务
  - `human-judgement` TR-3.2: 命令绑定正确设置

## [x] Task 4: 更新UI添加新标签页
- **Priority**: P1
- **Depends On**: Task 3
- **Description**: 
  - 在MainWindow.xaml中添加SN管理标签页
  - 添加热电偶标定标签页
  - 添加会话管理控件
  - 添加参数输入控件和按钮
- **Acceptance Criteria Addressed**: 所有AC
- **Test Requirements**:
  - `human-judgement` TR-4.1: UI布局合理，符合现有风格
  - `human-judgement` TR-4.2: 控件绑定正确

## [x] Task 5: 编译测试与验证
- **Priority**: P2
- **Depends On**: Task 1, Task 2, Task 3, Task 4
- **Description**: 
  - 编译整个项目
  - 检查是否有编译错误
  - 验证功能完整性
- **Acceptance Criteria Addressed**: 所有AC
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目编译成功
  - `human-judgement` TR-5.2: UI显示正常