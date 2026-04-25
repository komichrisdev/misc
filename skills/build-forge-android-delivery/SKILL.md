---
name: build-forge-android-delivery
description: Use Build Forge for Android project linking, APK/AAB builds, APK installs, device pushes, emulator launch, and app launch.
---

# Build Forge Android Delivery

Use this skill when the user wants Codex to route Android build, install, push, or emulator launch work through the local Build Forge instead of calling Gradle or adb directly.

## Command

Set the command once per task:

```powershell
$Forge = "C:\Users\chris\Documents\Projects\Android Build Manager\scripts\build-forge-cli.ps1"
```

Run commands with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge <action> <options>
```

## Actions

Link a project and make it the active Build Forge project:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge link -ProjectRoot "C:\path\to\project" -ProjectName "Project Name"
```

Build a release artifact:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge build -ProjectRoot "C:\path\to\project" -Kind apk
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge build -ProjectRoot "C:\path\to\project" -Kind aab
```

Build a debug APK and install it on connected Android devices:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge push -ProjectRoot "C:\path\to\project"
```

Build a debug APK, start the project emulator, install it, clear app data, and launch it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge test -ProjectRoot "C:\path\to\project"
```

Install an existing APK into the emulator:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge install-apk -ApkPath "C:\path\to\app.apk"
```

Install an existing APK on connected Android devices:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge push-apk -ApkPath "C:\path\to\app.apk"
```

Launch the emulator for a project:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge launch-emulator -ProjectRoot "C:\path\to\project"
```

Launch an already-installed app in the project emulator:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File $Forge launch-app -ProjectRoot "C:\path\to\project" -PackageName "com.example.app"
```

Add `-Tablet` when the app should launch on emulator display 1.

## Defaults

- Build Forge stores config and logs in `C:\Users\chris\AppData\Local\VoidWrite`.
- The last runner log is `C:\Users\chris\AppData\Local\VoidWrite\prod-android-build-last.log`.
- Project AVD names are managed by Build Forge, using `BuildForge_<Project_Name>_API_36`.
- The base AVD template is `Generic Emulator`; Build Forge can bootstrap it from the legacy `VoidWrite_API_36` template when present.
- `install-apk` uses the `Other` Forge project and `BuildForge_Other_API_36` unless a project or AVD override is passed.

## Rules

- Use `build -Kind aab` only to create an artifact; do not try to install an AAB.
- Use `test` or `install-apk` when the goal is to launch something in the emulator.
- Use `push` or `push-apk` when the target is a connected physical Android device.
- After any failed command, read or summarize the last runner log for the user.
