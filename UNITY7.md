# Unity 7 维护分支

本分支以官方 `v9.0.3` 为基线，提供 Unity `7000.0.0a12` 的实验支持。配套解释器、运行时补丁和原版 Unity 源码哈希锁定在 [unity7_runtime.json](Data~/unity7_runtime.json)。当前验证范围是 Windows Editor 和 Win64 IL2CPP；Android、iOS 和其他编辑器平台尚未验证。

Installer 从精确匹配的 Unity 7 原版源码准备工程本地 SDK，校验完整源码树，再取锁定的维护提交并应用差异补丁。安装会保留许可和版本凭据，版本不匹配时要求重装。它拒绝直接复制其他 libil2cpp 树，不修改 Unity 安装或共享钩子。自定义 IL2CPP 路径使用编辑器预期的工具目录布局，不在 CoreCLR 中安装旧的方法补丁。

在安装同版本 Windows IL2CPP 模块后，通过 `HybridCLR/Installer` 安装，然后按正常流程配置热更程序集并运行 `HybridCLR/Generate/All`。构建支持默认 `.NET Standard 2.1` 和 `.NET` API 配置；Win64 Player 都使用 Unity 7 的 .NET 10 BCL。旧的 `developmentBuild` 参数在 Unity 7 上不再改变托管变体，编译遵循项目设置并记录实际变体。

适配同时处理了裁剪输出中的架构目录，以及 .NET 接口静态虚方法的桥接生成。仅实例接口方法生成调整桩，静态方法保留相应调用签名。

真实 Win64 Player 已验证 AOT 补充元数据、热更结构体泛型集合、反射、继承接口、数组 RVA、委托与 GC、原子操作、模块初始化和特性。相同 `GameAssembly.dll` 可执行两版外部热更 DLL。编辑器使用 Unity 7 的 `CurrentAssemblies.LoadFromBytes`，已验证 Play 往返和重新打开。该入口将程序集纳入 Unity 管理的加载上下文；直接使用非 Unity 的加载上下文会影响编辑器代码重载。

`Tests~/Unity7/build_bridge_host.py` 可从已编译的本 fork 编辑器程序集构建独立桥接生成器，供配套解释器的 `tests/unity7/run_aot.py` 使用。Player 验证需要独立工程：将 `PlayerBootstrap.cs` 放入 Assets，将 `BuildPlayerFixture.cs` 放入 Assets/Editor，把配套解释器测试生成的 `HotPayload.dll` 放入 ExternalHotUpdate 和 Assets/StreamingAssets，后者命名为 `HotPayload.dll.bytes`。通过批处理调用 `BuildPlayerFixture.BuildDefaultApi` 或 `BuildPlayerFixture.Build` 构建，通过 `BuildPlayerFixture.Play` 验证 Play 往返。

源码默认保留 Unity 7 编译防护，匹配的安装和代码生成会写入 `HYBRIDCLR_UNITY7_EXPERIMENTAL`。这是针对 Alpha 版本的维护分支，不能作为生产发布或移动端可用性的承诺。
