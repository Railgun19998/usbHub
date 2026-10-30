# NuPhy Air75 V3 适配状态

## 已完成

- 本机发现 `19F5:2620` Air75 V3 2.4 GHz 接收器。
- 按 VID/PID 识别并显示专用型号，不绑定实例路径尾部或 USB 端口。
- 已读取其 HID 描述符，标准 HID 电量字段不可用。
- 提供明确的兼容状态，不用其他型号的电量值或猜测命令替代。

## 当前未完成

**Air75 V3 经 2.4 GHz 接收器读取实时电量尚未实现。蓝牙模式是否在本机公开 Battery Service 仍需连接该模式后验证。**

截至 2026-10-30，所检查的官方 NuPhyIO 客户端能力模块 `85130` 中，`isSupportBatteryShow()` 使用的集合只列出 WH80 / WH80ByDongle；Air75 V3 在另一型号集合中。这说明官方当前界面没有为该型号开放相同的电量显示路径，**不能据此证明固件内部绝对不存在其他可用协议**。

- 官方站点：<https://www.nuphy.io/>
- 配置工具：<https://drive.nuphy.io/>
- 本次检查的公开脚本：`https://drive.nuphy.io/static/js/main.eff2c3e9.js`
- 脚本 SHA-256：`B247CC2093D77CF5D5F6FB7C76C8D7D2F4792E525F5CCC797A8B60274F95C6AC`
- 能力集合与方法均通过静态文本分析确认；未执行官方客户端、未登录账户、未上传设备数据。

[Air75 V3 协议研究](https://github.com/pda/kbvu/blob/main/docs/keyboard-protocol.md) 证实不同型号、USB 配置接口与接收器路径必须分开核对。[Air100 V3 的固件研究](https://github.com/gig3m/nuphykit/blob/main/PROTOCOL.md) 记录该型号电量通过 BLE Battery Service 上报、未经过 2.4 GHz/raw HID；这是相关型号的线索，不能当成 Air75 V3 的实机结论。

后续实现需要 Air75 V3 对应固件/接收器的可靠电量响应证据，或厂商接口说明。当前版本不会扫描未知 opcode、替换会话密钥、刷固件、写入灯光/按键或修改配对来尝试获取电量。
