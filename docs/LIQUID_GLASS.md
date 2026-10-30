# ver1.2.2 视觉与验收

主窗口与托盘已统一为薄荷、天蓝、藕紫柔光背景、半透明白色卡片、墨青文字和青绿主色。主窗口默认 1440×900，侧栏宽 236px，六台设备按三列两行展示；更多设备通过底部页码访问。设置页沿用同一套控件、卡片和颜色。

ver1.2.2 将托盘从 372px 缩至 300px，缩小图标、标题、行距与按钮，移除图标底座及主按钮强调色。六台设备约 350px 高，最大高度为 420px，并受当前屏幕工作区与 DPI 限制；空间不足时滚动设备列表，固定底部操作按钮。离屏验收还覆盖 240px 高度下的 18 台设备，确认末项可达、底部按钮不裁切；网页预览覆盖 390×260 的受限视口。

## 材质来源与平台差异

视觉实现参考 [Kyant0/AndroidLiquidGlass](https://github.com/Kyant0/AndroidLiquidGlass) 及其 [Backdrop effects 文档](https://kyant.gitbook.io/backdrop/api/backdrop-effects)，采用背景采样、增强色彩、模糊、凸透镜边缘和交互高光的思路。未引入或复制 Compose 库代码。

- WPF：Desktop Acrylic 提供系统背景材质；卡片单独采样背景兄弟节点，使用四层边缘采样带近似折射，并叠加渐变描边、顶部内高光、柔和阴影和随指针移动的亮斑。原生倾斜使用轻微二维变换近似立体感。托盘宽度为 300px，保留完整扫描列表和三项操作。
- 网页预览：`backdrop-filter: blur(24px) saturate(190%)`；支持 SVG backdrop filter 的浏览器追加圆角边缘位移图，文字与插画不参与位移。卡片使用 CSS perspective 和 rotateX/rotateY 呈现三维倾斜；不支持位移滤镜时保留基础玻璃效果。
- 网页预览中的参考设备均为示例，设置仅保存于浏览器，不读取硬件；Windows 程序继续使用原有设备模型和扫描调度。

减少动画设置会关闭漂移、倾斜和电量动画；桌面端关闭玻璃时停止背景漂移。系统透明效果关闭时采用不透明回退。真实桌面的 Acrylic 效果取决于 Windows 合成器，离屏 PNG 主要核对内部绘制和布局。

## 验收方式

```powershell
.\scripts\Test.ps1
node .\scripts\Test-WebPreview.mjs
```

网页测试使用本机 Edge 的独立 headless profile，提供临时本地 HTTP 服务并在结束时关闭。测试不会操作用户现有窗口。截图前注入 `*{transition:none!important;animation:none!important}`，以终态核对电量条。输出位于 `artifacts/liquid-glass-web/`，包含主界面、空搜索、设置和两种尺寸的托盘截图，以及 `verification.json`。

WPF `--preview` 在屏幕外、隔离数据目录中检查两页、搜索和全部筛选、13 台设备分页、六卡布局、设置读写、隐藏恢复、托盘状态和操作回调。动效关闭以渲染终态。未请求真实托盘交互，也未发起硬件查询。

此次构建环境无法访问 NuGet 漏洞数据源；验证时仅通过命令行属性 `NuGetAudit=false` 使用已有依赖缓存，项目的审计配置没有修改。核心 47 项检查、WPF 冒烟和自包含 ZIP 解压启动均通过。发布 ZIP 仍只包含 EXE 与使用说明，网页预览和测试材料不入包。
