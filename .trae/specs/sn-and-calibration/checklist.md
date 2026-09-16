# SN管理与热电偶标定功能 - 验证检查清单

- [x] 检查SN服务已创建 (Services/SnService.cs)
- [x] 检查SN_WRITE命令(0x16)实现正确
- [x] 检查SN_READ命令(0x17)实现正确
- [x] 检查标定服务已创建 (Services/CalibrationService.cs)
- [x] 检查TC_CALIB_WRITE命令(0x12)实现正确
- [x] 检查TC_CALIB_READ命令(0x15)实现正确
- [x] 检查TC_CALIB_SAVE命令(0x13)实现正确
- [x] 检查TC_CALIB_DEFAULTS命令(0x14)实现正确
- [x] 检查TC_SAMPLE_READ命令(0x18)实现正确
- [x] 检查float32大端序转换实现正确
- [x] 检查ViewModel已集成新服务
- [x] 检查SN读写命令已添加到ViewModel
- [x] 检查标定命令已添加到ViewModel
- [x] 检查UI已添加SN管理标签页
- [x] 检查UI已添加热电偶标定标签页
- [x] 检查项目编译成功
- [x] 检查UI显示正常