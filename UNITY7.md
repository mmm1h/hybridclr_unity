# Unity 7 维护分支

本分支以官方 `v9.0.3` 为基线，维护 Unity `7000.0.0a12` 的适配。配套解释器、运行时补丁和原版 Unity 源码哈希锁定在 [unity7_runtime.json](Data~/unity7_runtime.json)。Windows、macOS 编辑器安装和代码生成，以及 Win64 Player、Android ARM64 真机和 iOS ARM64 模拟器均已验证。

Installer 从精确匹配的 Unity 7 原版源码准备工程本地 SDK，校验完整源码树，再取锁定的维护提交并应用差异补丁。安装会保留许可和版本凭据，版本不匹配时要求重装。它拒绝直接复制其他 libil2cpp 树，不修改 Unity 安装或共享钩子。自定义 IL2CPP 路径使用编辑器预期的工具目录布局，不在 CoreCLR 中安装旧的方法补丁。

安装同版本目标模块后，通过 `HybridCLR/Installer` 安装，再配置热更程序集并运行 `HybridCLR/Generate/All`。工具支持 macOS 的 `Resources/Scripting` 和 ARM64 部署工具布局。移动端编译不依赖独立播放器模块。Unity 7 的裁剪输出按架构存放，生成 AOT 元数据时应选择单一架构。

运行时的 BCL 以实际 Player 裁剪输出为准，不能仅凭 API 兼容级别判断。Win64 的默认 `.NET Standard 2.1` 与 `.NET` 配置均使用 .NET 10 BCL；iOS 模拟器的默认配置也使用 .NET 10 BCL。默认 Android Player 使用 `unityaot-linux` 的 Mono BCL。外部热更 DLL 必须引用对应目标的程序集。旧的 `developmentBuild` 参数在 Unity 7 上不再改变托管变体，编译遵循项目设置并记录实际变体。

适配同时处理了裁剪输出中的架构目录，以及 .NET 接口静态虚方法的桥接生成。仅实例接口方法生成调整桩，静态方法保留相应调用签名。

三个 Player 目标均通过两版外部热更 DLL，原生二进制保持相同哈希。检查包含 AOT 补充元数据、热更结构体泛型集合、反射、接口、数组 RVA、委托与 GC、原子操作、模块初始化、特性、混合浮点与整数参数、结构体返回值和异常传播。编辑器使用 `CurrentAssemblies.LoadFromBytes`，TEngine 消费工程已验证 Play 往返、示例 UI 和重新打开。该入口将程序集纳入 Unity 管理的加载上下文。

`Tests~/Unity7/build_bridge_host.py` 为配套解释器的独立宿主生成桥接。Player 夹具使用独立工程：将 `PlayerBootstrap.cs` 放入 Assets，将 `BuildPlayerFixture.cs` 放入 Assets/Editor，把 `HotPayload.dll` 放入 ExternalHotUpdate 和 Assets/StreamingAssets，后者命名为 `HotPayload.dll.bytes`。外部 DLL 源码位于解释器仓的 `tests/unity7/HotPayload.cs`；Mono BCL 配置需要 `LEGACY_BCL` 和 `LegacyModuleInitializer.cs`，第二版增加 `PAYLOAD_V2`。用 `HYBRIDCLR_FIXTURE_BCL` 指定对应 BCL 目录，调用 `BuildDefaultApi`、`BuildAndroid` 或 `BuildIosSimulator` 构建。后者导出 SimulatorSDK、ARM64 工程，再用 Xcode 编译。

夹具在 `persistentDataPath` 写入 `hybridclr-result.json`，记录加载 DLL、AOT 元数据哈希及检查结果。在同一路径放入第二版 `HotPayload.dll.bytes` 和内容为 `{"version":2}` 的 `payload-config.json`，重启同一个应用即可验证热更。Android 使用 UnityWebRequest 读取 APK 内的 StreamingAssets。

源码保留精确修订的编译防护，匹配的安装和代码生成会写入 `HYBRIDCLR_UNITY7_EXPERIMENTAL`。升级这个 Alpha 编辑器时需要更新源码锁并重新验证。
