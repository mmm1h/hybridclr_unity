# Unity 7 维护分支

状态：原生适配未完成，不能用于发布。

本分支以官方 `v9.0.3` 为基线，目标是 Unity `7000.0.0a12`。配套源码是 [hybridclr](https://github.com/mmm1h/hybridclr/tree/codex/unity7-support) 和 [il2cpp_plus](https://github.com/mmm1h/il2cpp_plus/tree/codex/unity7-support) 的同名分支。

Unity 7 的 `ScriptCompilationOptions.DevelopmentBuild` 已成为编译错误。工具改用 `None`，遵循项目当前的 `PlayerSettings.GetManagedCodeVariant`，并记录实际变体。旧的 `developmentBuild` 参数在 Unity 7 上不改变变体，也不再生成 `DEVELOPMENT_BUILD`；其他受支持 Unity 版本保留原行为。依据见 [Unity 官方编译接口源码](https://github.com/Unity-Technologies/UnityCsReference/blob/7000.0/Modules/ContentBuild/Editor/Managed/PlayerBuildInterface.bindings.cs)。

本分支不在 Unity 7 的 CoreCLR 编辑器中自动安装未经验证的方法补丁。启用 HybridCLR 构建时，会在修改脚本后端和 IL2CPP 环境路径之前报告原生适配尚未完成。Installer 保持拒绝安装 Unity 6 原生库，不增加指向旧版本的 Unity 7 映射。

编辑器程序集已使用 Unity 7 的真实引用和 Roslyn 编译通过。该结果仅验证 C# 编译，尚未验证本 fork 在编辑器中的重载、IL2CPP Player 和安卓或 iOS 的热更新。原生泛型上下文、类型加载异常和基础运行时补丁的验证范围见配套源码说明。
