# СР02 Аудит PharmacyStock

Чугаєв Олексій Михайлович, група 471, варіант 19. Джерело А — пакет викладача andreichenko256/legacy-code/V19-PharmacyStock, точна ревізія в SOURCE.md. Це окрема робота, а не попередній семестровий проєкт.

## Запуск

Потрібен SDK .NET 8 (global.json: 8.0.427, latestPatch).

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src -c Release
dotnet run --project tools/Metrics -c Release -- src
dotnet test -c Release --collect:"XPlat Code Coverage"
```

42 тести. Максимальна CC 21→8; Dispense 21→7; вкладеність 5→3; 2 родини клонів→0. Покриття PrescriptionService на сітці 109/111=98.20%, після 109/110=99.09%. Тести у Legacy.Tests не змінювалися після safety-net. Після зміни ставки 0.5→0.6 один тест падає, після повернення всі зелені. CSV повністю відтворюються інструментом.

## Перевірка кінців

baseline — незмінені байти пакета, safety-net — ті ж src з 42 тестами, after-refactoring — 12 різних refactor-комітів. Тести на справжньому baseline перевірено в незалежній копії: з safety-net взято лише Legacy.Tests, Legacy.sln і global.json, а src — точні байти baseline. Обидва запуски мають 42 Passed. На ПК для захисту можна перейти на safety-net — production-код там байт-у-байт baseline.

Таблиці: metrics-before.csv, metrics-after.csv; реєстр defects.md; план plan.md; 6 рішень decisions.md; 6 пунктів боргу debt.md. docs/out-before.txt та docs/out-after.txt байт-у-байт однакові. Фіксовані ціни/дані навчальні, не медичні рекомендації. Дати сценарію відносні до локального Today; час і культура можуть впливати на інші дні запуску, TD-04.

Локальні журнали використовують перевірений кеш пакетів; для звичайного ПК restore звертається до nuget.org. Глобальний SDK10 не застосовано: інструмент використовує Roslyn із SDK8. Автоматичний хронометраж у docs/steps.json — секунди інструментів; оцінки PERT у debt.md — прогноз людино-годин, не виданий за відпрацьований час.
