# Working Berek Patch

Финальный рабочий набор оказался минимальнее, чем промежуточные попытки.

## Что в итоге патчится

`GameAssembly.dll`

- перевод комнаты и старта матча в `Berek`
- перевод state flow в `BerekSelectionState`
- форс berek-карты в host flow

`Sneak Out_Data/resources.assets`

- предустановка `SpookedNetworkPlayer.EntityBerekComponent` на prefab `UnityPlayer`

## Почему понадобился патч resources.assets

Критический поздний баг был не в самом matchmaking, а в `InitializeBerekComponents()`.

Что удалось подтвердить:

- `UnityPlayer` prefab уже содержит `EntityBerekComponent`
- `SpookedNetworkPlayer` в runtime ожидал ссылку на этот компонент в поле `EntityBerekComponent`
- `AssignComponents()` заполнял много player-компонентов, но не заполнял этот slot
- в результате berek flow доходил до `HandleBerekModeStart`, а затем падал на `NullReferenceException`

Практический фикс:

- в `resources.assets` у `SpookedNetworkPlayer` был нулевой serialized slot
- этот slot был привязан к `EntityBerekComponent` prefab-а
- после этого berek матч стал проходить до конца

## Скрипт

Рабочий патчер:

- `tools/patch_sneak_out_berek.py`

Свойства:

- принимает путь к корню игры
- строго проверяет `SHA-256`
- делает локальные `.codex-berek.bak`
- умеет `--rollback`
- идемпотентен

## Текущее состояние

Подтверждено:

- матч создаётся как `game_mode=Berek`
- карта уходит в berek map
- режим реально отыгрывается от начала до конца

Оставшийся известный нюанс:

- визуал короны пока не доведён

