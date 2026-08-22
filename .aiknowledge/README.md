# Project AI Knowledge

本目录维护当前项目中可复用的 AI 知识，按 surface 分离：

- [`domain.md`](domain.md)：项目术语、canonical 含义、边界和稳定关系。
- [`pitfalls/index.md`](pitfalls/index.md)：失败模式、反例、正例和验证路线。
- [`codemap/index.md`](codemap/index.md)：源码入口、职责边界和下一步阅读路线。

## 使用顺序

1. 先读取本文件。
2. 根据任务目标读取对应 surface 的 index；Domain 直接读取 `domain.md`。
3. 只读取与当前任务命中的少量 entry，并回源源码、测试或 runtime 验证。
4. 同一语义只 amend 现有条目，不创建平行条目。
5. 当前知识不能确认时，将 Pitfall/Codemap 标为 stale 或保留在待重审范围，不猜测实现。

## 当前项目边界

仓库目前只维护 Native WPF/.NET 8 Windows 实现。仓库中保留的 legacy profile 文件仅作为显式、只读的一次性数据导入输入，不再对应可运行的 Python 应用；Native 负责工作区事务和全部 OCR 导航能力。

## 维护约束

Canonical body 不保存完整历史来源、旧变更列表、机器路径 scope 或机器关联图。Codemap 只负责导航，Pitfall 只记录可复用的 failure mode；任何修改仍需回到当前源码、测试和 Windows runtime 验证。

## 验证入口

Native 默认验证：

```powershell
dotnet test WutheringWavesAchievement.sln -c Release
dotnet build WutheringWavesAchievement.sln -c Release
```

OCR/发布边界验证见 `scripts/` 下的独立脚本，并要求 Windows 游戏窗口处于可见、预期分辨率和同等输入权限环境。
