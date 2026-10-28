# 柔性机加工车间仿真基线备份

> 基线日期：2026-10-28  
> Unity 版本：2022.3.62f3c1 LTS  
> 主场景：`UnityProject/Assets/Scenes/FactoryMachiningTwin.unity`

这是 Blender 模型正式接入前的可运行基线，保留了三条产线、共享 ASRS 立体仓库、六台轨道机器人、机床、输送机、货运配送载板、工件生命周期和运行时完整性监控。

## 备份内容

- `UnityProject/`：可用 Unity Hub 直接打开的源码工程。
- `Documentation/`：整体运行框架、流程图和 Blender 接入约束。
- `ReferenceDocs/`：原项目执行与修改记录。
- `Verification/`：冷编译和 240 秒运行烟雾测试日志。
- `RuntimeReference/`：已验证的 Windows x86_64 可执行对照版本。
- `SourceArchive/`：生成后的源码 ZIP 快照及 SHA-256 校验值。

## 恢复方法

1. 安装 Unity/Tuanjie `2022.3.62f3c1`。
2. 用 Unity Hub 打开 `UnityProject` 目录。
3. 等待 Unity 自动重建 `Library` 并完成脚本编译。
4. 打开 `Assets/Scenes/FactoryMachiningTwin.unity`。
5. 点击 Play，HUD 显示 `OFFLINE DEMO` 属于当前基线的正常状态。

`Library`、`Temp`、`Logs`、`UserSettings` 属于本机缓存或个人设置，不是可恢复源码的必要组成部分，因此没有进入 GitHub 源码版本。

## 已验证基线

- Unity 冷编译：返回码 0，无 C# 编译错误。
- Windows 64 位播放器 240 秒烟雾测试：`completedJobs=3`、`agvs=6`、`result=PASS`。
- 运行时未发现 `Factory Flow Integrity` 违规。

## 核心原则

Blender 接入时优先替换“可视模型子节点”，不要直接改动状态机、资源锁、工件归属、夹爪锚点和配送交接节点。详细边界见 `Documentation/BLENDER_INTEGRATION_GUARDRAILS.md`。

