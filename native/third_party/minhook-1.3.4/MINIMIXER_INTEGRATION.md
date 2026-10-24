# MinHook v1.3.4 – versionierte x64-Buildquellen

Diese unveränderten Dateien wurden aus dem zuvor verifizierten MinHook-v1.3.4-Archiv übernommen. Sie gehören **nicht** zu GOverlay. Der x64-native Hook-Build nutzt nur die hier enthaltenen Header-/Source-Dateien und funktioniert ohne Netzwerk oder `.tmp`-Quelldateien.

- Upstream: MinHook von Tsuda Kageyu, Version 1.3.4
- Lizenz: `LICENSE.txt` (enthält Copyright- und BSD-artige Bedingungen einschließlich HDE-Komponenten).
- Verwendete Dateien: `include/MinHook.h`, `src/{buffer,hook,trampoline}.c`, `src/hde/hde64.c` und die zugehörigen Header/Tabellen.
- x86-Unterstützung ist nicht Teil dieser x64-Preview und wird nicht behauptet.

Ein vollständiger unabhängiger Neuaufbau des fertigen, gamefähigen Installers benötigt zusätzlich einen sicheren eigenen Injector und einen geprüften Attach-/Detach-Lebenszyklus. Dessen Freigabe ist durch die reine Versionierung dieser Bibliothek nicht vorweggenommen.
