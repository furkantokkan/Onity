# Mobile IL2CPP build verification — 2026-09-23

- Unity Editor: 2022.3.62f3. Android Build Support, SDK/NDK, OpenJDK, and iOS Build Support installed.
- Source generator: constructor selection now prefers `[Inject]` and registers the exact constructor signature. The packaged `Onity.SourceGen.dll` was rebuilt from that source.
- Source generator and analyzer tests: 27/27 passed (`dotnet test tools/Onity.Analyzers/Tests/Onity.Analyzers.Tests.csproj --no-restore`).
- Android: ARM64 IL2CPP Unity build succeeded (exit 0). APK size: 28,239,266 bytes. APK contains `lib/arm64-v8a/libil2cpp.so` and `libunity.so`; APK signature verification passed.
- Android APK SHA-256: `E27165C975C555F0ADA23A7AA9D474129809608815D4FC06EB4C798CCCFDC62F`.
- iOS: IL2CPP Unity build succeeded (exit 0) and generated a `Unity-iPhone.xcodeproj`, `Info.plist`, and Onity IL2CPP C++ output.
- The builds used an isolated worktree with the current Onity package changes and a copy of the original project's configured `SampleScene.unity`. The source project's open Editor, scenes, and project settings were not modified.
- Runtime device testing remains open: no Android device was connected, and Xcode compilation, signing, and iOS device execution require a macOS/Xcode environment.
- The host project emits Android and iOS Localization `App Info` warnings because platform app-name metadata is not configured. This is outside the Onity package and did not fail either build.

Generated artifacts at verification time:

- Android APK: `C:/Users/e-fur/.codex/worktrees/onity-mobile-verification/Onity/Temp/OnityMobileValidation/Onity.apk`
- iOS Xcode project: `C:/Users/e-fur/.codex/worktrees/onity-mobile-verification/Onity/Temp/OnityMobileValidation/iOS-Xcode`
- iOS Unity build log: `C:/Users/e-fur/.codex/worktrees/onity-mobile-verification/Onity/Temp/OnityMobileValidation/ios-build.log`
