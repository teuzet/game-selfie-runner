# Окружение машины фабрики

Что стоит на машине, где работают исполнители и диспетчер. Файл правят фичи окружения (F-006) и техлид; исполнители читают его, чтобы не гадать о путях.

## .NET
- SDK с поддержкой file-based apps (`dotnet run file.cs`), тесты бегут на последнем установленном рантайме (`RollForward=Major` в `Game.Core.Tests`).
- Вывод сборки — `bin~/` и `obj~/` в корне репозитория (`Directory.Build.props`), чтобы Unity не импортировал DLL и сгенерированные `.cs`.

## Unity
- Версия проекта: **6000.6.4f1** — установленная на машине (решение Nick, 2026-11-09, вместо 6000.3 LTS). Проект не открывают другими версиями, чтобы `ProjectSettings` и `packages-lock.json` не прыгали.
- Путь: `C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Unity.exe`, задан в `factory.yaml` фабрики (`unity.exe`). Его же гоняет гейт диспетчера; воркерам путь приходит в `UNITY_PATH`.
- Исполнитель запускает редактор только тулом `unity` (`factory tool run unity --set mode=... --out .factory/unity`). Сменить версию — одна строка в `factory.yaml` и решение Nick.

## Песочница агентов
- Файлы читаются и ищутся только внутри `C:/factory/home/game-selfie-runner` (плюс Read/Glob по абсолютному пути); `factory` CLI недоступен, `features.json` проверяется `dotnet run .factory/validate.cs -- features.json`.
