# CHUB

Windows 11 外设电量工具，使用 .NET 10 / WPF，支持设备总览、系统托盘、搜索筛选与低电量提醒。

## 下载与运行

在 [GitHub Releases](https://github.com/Railgun19998/usbHub/releases/latest) 下载 **CHUB ver1.3.1** 自包含发行包，解压后双击 `CHUB.exe`，无需另装运行库。

右键点击托盘图标查看设备电量；面板中可打开主窗口、刷新或退出。设置在当前 Windows 用户的本地应用数据目录保存。程序默认不开机自启、不安装服务、不上传设备数据。

## 电量支持

| 设备 / 接口 | 显示与限制 |
| --- | --- |
| 通用 XInput | 全部 4 个槽位，不限制品牌或设备子类型；显示空、低、中、满，有线或未知状态 |
| Sony DualSense / DualSense Edge（DS5） | USB / 蓝牙电量报告、充电状态与蓝牙 CRC 校验；显示真实 10% 区间，例如 30–39%；协议和模拟检查已完成，实机仍待验证 |
| Bluetooth BLE GATT | 已连接设备公开的 Battery Service 电量 |
| 通用 HID | 描述符公开的标准电量字段 |
| Logitech HID++ | 已适配 C547 接收器的电量、名称与充电信息 |
| ASUS ROG PELTA | 已适配厂商电量与充电报告 |
| NuPhy Air75 V3 | 支持型号识别，2.4 GHz 电量协议暂未适配 |

XInput 只提供电量等级。唯一手柄可关联其实际 HID 名称，多手柄按槽位区分，不猜测身份；无电量的兼容 HID 副本不重复展示。DualSense 蓝牙简化报告不含电量，程序不改变设备模式，该情况下电量可能未知。设备被其他软件独占时也可能无法读取。

主动读取每 30 分钟最多一轮，手动刷新、插拔、恢复和重启共用同一计划。缓存、未知、断开和充电状态不触发低电量提醒；区间电量按上界判断提醒阈值。

## 从源码构建

安装 .NET 10 SDK，在项目目录运行：

```powershell
.\scripts\Build.ps1 -SelfContained
```

输出 `artifacts/CHUB-ver1.3.1-win-x64.zip`，仅包含 `CHUB.exe` 与简短使用说明。不带 `-SelfContained` 时生成需要 .NET 10 Desktop Runtime x64 的精简包。

仓库只保留程序源码、构建必需文件与本说明；发行包在 Releases 下载。

协议参考：[Microsoft XInput](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation)、[Sony 维护的 Linux HID 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c)。

MIT License。
