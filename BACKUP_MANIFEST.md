# 备份清单

- 创建日期：2026-10-28（Asia/Shanghai）
- Unity 工程文件数：162
- 本地完整备份文件数：313（包含运行版与验证材料）
- 源码压缩包：`SourceArchive/FactoryTwin_Source_Baseline_2026-10-28.zip`
- 源码压缩包大小：0.65 MB
- SHA-256：`010483021E3C2175F5955ABF2D1BEC5C73BE6611E2BD87942126EF5CD7AEF1A5`

## 源码范围

- `UnityProject/Assets`
- `UnityProject/Packages`
- `UnityProject/ProjectSettings`
- `Documentation`
- `ReferenceDocs`
- 根目录 `README.md` 与 `.gitignore`

## 本地附加对照材料

- `RuntimeReference/FactoryTwin-Windows-x86_64`
- `Verification/compile-debug-20261026.log`
- `Verification/player-smoke-240s-20261026.log`

## 校验命令

```powershell
Get-FileHash '.\SourceArchive\FactoryTwin_Source_Baseline_2026-10-28.zip' -Algorithm SHA256
```

输出应与本清单记录的 SHA-256 完全一致。

