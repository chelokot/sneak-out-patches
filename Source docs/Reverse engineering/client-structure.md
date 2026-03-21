# Client Structure

## Основные файлы

### `GameAssembly.dll`

Где:

- основной нативный IL2CPP код клиента

Что там искать:

- state machine матча
- host flow
- matchmaking
- UI flow
- gameplay logic, которую нельзя увидеть в asset-ах

### `Sneak Out_Data/resources.assets`

Где:

- prefab-ы и часть сериализованных данных

Что там искать:

- `UnityPlayer` prefab
- MonoBehaviour serialized slots
- связи между компонентами на prefab-ах

### `Sneak Out_Data/level0`

Что важно:

- сцена лобби
- старые UI view вроде selector-а режима могут физически лежать здесь, даже если не подключены к текущему flow

### `Player.log`

Что это:

- главный runtime log клиента

Зачем нужен:

- подтверждать реальный `game_mode`
- подтверждать `scene_type`
- ловить падения state machine
- различать “не тот режим пришёл” и “режим пришёл, но потом сломался”

## Практический рабочий процесс

1. сначала смотреть `Player.log`
2. потом решать, это баг UI, session props, state machine или prefab wiring
3. если падение позднее и связано с компонентом игрока, проверять не только `GameAssembly.dll`, но и prefab wiring в `resources.assets`

## Полезные артефакты

Временные дампы IL2CPP могут лежать в `/tmp`, но на них нельзя полагаться как на долговременное хранилище. Итоговые выводы нужно переносить в эту репу.

