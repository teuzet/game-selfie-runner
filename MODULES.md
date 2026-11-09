# Карта модулей ядра

Кто чем владеет и кто от кого может зависеть. Модуль — подсистема целиком: своя папка, своё состояние (структуры с `Init`/`Hash`), свои данные (`Data/*.json` и класс `*Data`), своя логика. Хаб (`Simulation.cs`, `RaceState.cs`, `GameData.cs`) только собирает модули: порядок вызовов в `Step`, композиция состояний и хэшей, композиция данных. Логики в хабе нет.

Правила:
- Модуль пишет только своё состояние. Чужое состояние читает; менять его можно только через функцию модуля-владельца (пример: `EventScan` начисляет скачки через `EventBonuses.Add`, `Phases` замораживает счёт места через функцию `Economy`).
- Зависимость разрешена, только если она есть в колонке «Может зависеть от». Новая связь между модулями — только с одобрения техлида: вопрос в `result.json`, после одобрения строка здесь и в `decisions.md`.
- Глобальной шины событий нет. `RaceEvents` — выход тика наружу (Unity, Game.Net, Game.Sim), модули по нему друг с другом не общаются.
- Состояние на место — поле структуры модуля внутри `RunnerState` (движение — `Runners`, `Toss` — `Bull`, `Viewing` — `Economy`), а не параллельный массив в `RaceState`. Отдельные массивы допустимы для служебных трекеров, которые не часть игры места (`Ev`, `Frame`, `Trackers`, `Bots`).
- Числа модуля — в его JSON с комментарием-источником; констант в коде нет, кроме структурных (`SeatCount`, `TicksPerSecond`, размеры буферов).

| Модуль | Папка / файлы | Состояние (в `RaceState`) | Данные | Логика | Может зависеть от |
|---|---|---|---|---|---|
| Math | `Math/` | — | — | `Fixed`, `DetRandom` | — |
| Data | `Data/Json.cs`, `Data/DataFiles.cs`, `GameData.cs` | — | все `Data/*.json` как набор `DataFiles` («имя файла → текст») | парсер JSON, сборка `GameData` из набора (каждый модуль объявляет `*Data.FileName` и парсит себя) | Math |
| Street | `Street/` | `Dyn[]` (подвижные препятствия), `Hit[]` (кто с чем уже столкнулся) | `street.json` | генератор по сиду, запросы `WidthAt/SlopeAt/BlockIndexAt`, движение препятствий | Math, Data |
| Runners | `Runners/` | `RunnerState`: движение, `Collision`, `Action`, `Stability` | `runners.json`, `characters.json`, `actions.json` | движение и темп, расталкивание, столкновения, толчок и подножка, персонажи | Street, Math, Data |
| Bull | `Bull/` | `Bull`, `Charge`, `RunnerState.Toss` | `bull.json` | загон и срыв, резинка, рывок, выбор цели, подброс и «нет сигнала» (переносит `S` цели — это и есть подброс) | Runners (позиции, персонаж), Street, Math, Data, Economy (новая связь, чтение `Viewing.FinishTick` — «финишировавшего не подбрасывать», одобрена техлидом, F-071) |
| Economy | `Economy/` | `RunnerState.Viewing`, `Frame[]` | `economy.json` | зоны дистанции, пассивный доход, потолок/пол, скачки за события (`EventBonuses.Add`), «влез в кадр», заморозка счёта на финише | Runners (факты и позиции), Bull (позиция), Math, Data |
| Events | `Events/` | `Ev[]` (что сканер уже видел) | — | счётчики фактов → `RaceEvents` тика; скачки начисляет через функцию Economy | Runners, Bull, Economy |
| Race | `Race/` | `Phase`, `PhaseTicks`, `Best`, `Trackers[]` | `bull.json` (`countdownSec`, `finishSec`) | отсчёт, вход на арену, таймер финиша, итоги и лучшие моменты | Street, Economy (заморозка счёта), Bull (данные таймеров), Events (выход) |
| Hints (F-021) | `Hints/` | `RunnerState.Hints` (`HintState`) | `hints.json` | подсказка из кадра бегущего впереди с опережением и шумом; реализует `IBotHintSource` | Street, Runners, Events (выход) |
| Bots | `Bots/` | `Bots[]` (`BotMemory`) | `bots.json` | наблюдения `BotView`, мозги характеров, решение по тику | Runners, Bull, Street, Race (фаза), Hints (только через `IBotHintSource`) |
| Quests (F-022) | `Quests/` | состояние квестов | `quests.json` | выдача, условия, награды через функцию Economy | Events, Economy, Runners, Bull |
| Хаб | `Simulation.cs`, `RaceState.cs`, `RaceConfig.cs`, `TickInputs.cs`, `InputLog.cs` | композиция | композиция (`GameData`) | порядок шагов, `Clone`/`CopyFrom`/`StateHash` как сумма хэшей модулей | все модули |
| `Game.Net` | `Game.Net/` | — | — | rollback-протокол поверх `Simulation` | Хаб (публичный API), Math |
| `Game.Sim` | `Game.Sim/` | — | — | прогон ботов, метрики и хроника из `RaceEvents` и публичных запросов модулей | Хаб, Events, Economy, Race, Bull (только публичные функции чтения) |

## Порядок шага (`Simulation.Step`)
Одна строка — один вызов модуля; порядок фиксирован и меняется только фичей с правкой этого раздела:
1. Race: отсчёт (бегуны стоят, бык ведёт таймер срыва) — пока отсчёт не кончился, остальное не выполняется.
2. Bots: входы мест-ботов по наблюдениям; Runners: вход лежащего («нет сигнала») глушится.
3. Runners: движение, расталкивание.
4. Street: подвижные препятствия.
5. Runners: столкновения, затем действия (толчок, подножка).
6. Bull: движение (резинка), затем рывок и подброс.
7. Economy: зоны и доход, затем «влез в кадр».
8. Events: сканер фактов → события тика и скачки.
9. Race: фазы, финиш, лучшие моменты.
10. Хаб: общий ГСЧ продвигается, `Tick++`.

Hints (F-021): `ChatHints.Step` стоит между 1 и 2 (подсказки тика нужны ботам того же тика); Quests (F-022) — после 8.

## Хэш состояния (`RaceState.StateHash`)
Композиция хэшей модулей, порядок фиксирован (`Hashing.Mix` — `Math/Hashing.cs`; у каждой структуры состояния свой `Hash(ulong h)` рядом с ней): хаб (`Tick`, `Rng`) → места `RunnerState.Hash` (движение и `Stability` — Runners, затем `Collision`, `Action` — Runners, `Toss` — Bull, `Viewing` — Economy) → Street (`MovingObstacles.Hash`: `Dyn`, `Hit`) → Bull (`BullState`, `BullChargeState`) → Events (`EventState`) → Economy (`FrameState`) → Race (`Phases.Hash`: фаза, таймер, лучшие моменты, трекеры) → Bots (`BotMemory`). Hints (F-021) — `RunnerState.Hints` (`HintState.Hash`) хэшируется в конце хэша места. Новая структура состояния — `Hash` рядом и одна строка в `StateHash`; `StateLayoutTests.EveryRunnerFieldChangesStateHash` ловит поле `RunnerState`, не попавшее в хэш.

## История связей
- 2026-11-09 · F-070 · код приведён к карте: отступлений от неё нет, новых связей нет. Подмена входов ботов — `BotBrains.FillInputs`, глушение входа лежащего — `RunnerMovement.MuteNoSignal`, движение мест — `RunnerMovement.StepAll`, бык за тик — `BullCharge.StepBull`; отсчёт — в `Simulation.StepCountdown` двумя вызовами (`BullMovement.Step`, `Phases.StepCountdown`), чтобы Race не вызывал движение Bull. Хэш забега переснят (порядок композиции).
- 2026-11-09 · F-071 · новая связь Bull → Economy (чтение FinishTick), одобрена техлидом: защищённых определяет `BullCharge.Shielded` (читает `Viewing.FinishTick`); Bull зовёт `RunnerMovement.Rise` (функцию владельца Runners) при подъёме подброшенного.
- 2026-11-09 · техлид · карта составлена при закрытии итерации 1 по фактическому коду; расхождения с кодом (хэш в `RaceState` по полям чужих структур, 7 позиционных аргументов `GameData.Parse`, логика подмены входов в `Simulation.Step`) исправляет F-070.
