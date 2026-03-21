# Patch History

## Что не сработало

### Простое включение старого UI mode selector

Попытка:

- включать старые `GameModeView` объекты в сцене
- редиректить портал на старый экран выбора режима

Итог:

- давало чёрный экран или ломало раннюю инициализацию клиента
- старый UI остался в билде, но не был безопасно встроен в текущий flow

### Глобальный форс `GameState.get_GameMode() = Berek`

Попытка:

- жёстко возвращать `Berek` из runtime getter-а

Итог:

- ломало старт клиента ещё до нормального лобби
- по логам сыпались ранние DI/Autofac ошибки

### Форс `CharacterType -> victim_penguin`

Попытка:

- подменять тип выбранного игрока на пингвина слишком рано

Итог:

- получалось состояние `два пингвина без короны`
- это был неверный слой фикса

## Что сработало

### Сетевой режим и карта

Рабочая часть:

- принудительно записывать `Berek` в host flow
- принудительно вести host map selection в berek-ветку
- переводить `BeforeSelectionState` в `BerekSelectionState`

Результат:

- `game_mode=Berek`
- `scene_type` уходит на berek-карту

### Berek component wiring

Рабочая часть:

- не продолжать ломать state machine
- вместо этого исправить реальную причину падения в `InitializeBerekComponents()`
- привязать `EntityBerekComponent` к `SpookedNetworkPlayer` через prefab asset

Результат:

- berek матч проходит от начала до конца

## Логи, которые были ключевыми

Главный runtime log:

- `Player.log` в `compatdata/2410490/.../LocalLow/Kinguin Studios/Sneak Out/Player.log`

Наиболее полезные признаки:

- `game_mode=Berek`
- `scene_type=Map05_TagGame` или другая berek-карта
- `Chosen seeker`
- `NullReferenceException` в `GameStartController.InitializeBerekComponents()`

