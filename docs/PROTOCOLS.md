# 协议和平台资料

本项目独立实现设备读取；以下是接口和报文字段的核对来源，不将第三方完整驱动或配置工具源码打包到产品。

## Windows / HID / Bluetooth

- [Microsoft HID usages](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-usages)
- [Microsoft HID report interpretation](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/interpreting-hid-reports)
- [Microsoft SDK HID parser definitions](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/hidpi.h)
- [Microsoft Bluetooth battery profiles and services](https://learn.microsoft.com/en-us/windows-hardware/design/accessory-guidelines/bluetooth-accessory-guidelines/bluetooth-accessory-guidelines-battery-profiles-services)
- [Microsoft GATT client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
- [Microsoft XInputGetBatteryInformation](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation)
- [Microsoft desktop window backdrop values](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)

Windows Bluetooth 的 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2` 属性是可选系统驱动属性，不视作跨设备公开协议，返回值始终标为缓存。实际蓝牙连接状态由 Windows Runtime Association Endpoint 提供，BLE 百分比使用 Battery Level uncached 读取。

## Logitech

- [Logitech 官方 HID++ 2.0 结构](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)
- [Logitech IRoot 功能发现](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/features/0x0000-IRoot.rst)
- [Solaar HID++ 电量字段](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)
- [Linux Logitech HID++ 实现](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)

实现功能表查询、电量功能 `1004` / `1000`、名称功能 `0005`。每次请求使用 software ID，校验设备槽位、feature、function 和 software ID，拒绝其他程序的响应。只适配当前已验证的 C547，不能将其视作全系列支持。`1001` 电压到百分比没有加入，以免在缺少标定时展示估算为真值。

## ASUS ROG PELTA

- [G-Helper Pelta 型号声明](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Headset/Models/Pelta.cs)
- [G-Helper headset protocol fields](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Headset/AsusHeadset.cs)

仅对 `0B05:1B84` 厂商控制集合查询 `CC 12 07` 和 `CC 12 08`。分别校验电量与充电响应，拒绝 `FF AA` 及不匹配命令，未实现或调用灯光、EQ、按键、复位或升级功能。

## NuPhy

见 [NUPHY.md](NUPHY.md)。
