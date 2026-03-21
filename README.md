# Sneakout

Локальная рабочая репа для reverse engineering и документирования `Sneak Out`.

Что здесь лежит:

- `tools/` — рабочие скрипты и патчеры
- `Source docs/` — структурированная документация по файлам, логике, игровым механикам и истории патчей

Быстрый старт:

```bash
python3 tools/patch_sneak_out_berek.py "/path/to/Sneak Out"
python3 tools/patch_sneak_out_berek.py --rollback "/path/to/Sneak Out"
```

Текущий проверенный результат:

- приватное лобби реально создаётся в `Berek`
- матч запускается на berek-карте
- режим полностью отыгрывается от начала до конца
- визуал самой короны пока не доведён

