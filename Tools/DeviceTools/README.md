# IPA Desktop Device Tools

Комплект **Windows 10/11 x64** для обнаружения iPhone, получения сведений и установки IPA. Содержит `idevice_id`, `ideviceinfo`, `ideviceinstaller`, `idevicepair` и необходимые DLL. Это наша сборка свободных проектов, сохраняющая их протоколы и лицензии.

Пользователю IPA Desktop достаточно установить комплект через **«Настройки» → «Проверить инструменты»**. Для USB нужны компоненты Apple Mobile Device, разблокировка iPhone и доверие компьютеру. Wi-Fi требует предварительного включения сетевой синхронизации через Apple Devices/iTunes.

## Структура

| Путь | Назначение |
|---|---|
| `Scripts/build.sh` | Загрузка закреплённых исходников и сборка в MSYS2 |
| `Scripts/package.py` | Вызов упаковщика с реальным objdump |
| `Scripts/check_updates.py` | Подготовка кандидата из стабильных upstream-релизов |
| `Scripts/Publish-TestedDraft.ps1` | Восстановление проверенного черновика |
| `device_tools/` | Модули поиска обновлений, зависимостей и упаковки |
| `Tests/` | Автономные тесты без iPhone, MSYS2 и доступа к Apple |
| `pins.json`, `stable.json` | Закреплённые SHA и известные стабильные выпуски |
| `VERSION`, `changelog.md` | Версия нашего комплекта и описание выпуска |

## Проверки и сборка

Из корня репозитория, Python 3.9+:

```bash
PYTHONPATH=Tools/DeviceTools python -m unittest discover -s Tools/DeviceTools/Tests -v
```

Сборка выполняется в **MSYS2 MINGW64** с пакетами из [workflow](https://github.com/iamdarkls/IPA-Desktop/blob/main/.github/workflows/device-tools.yml):

```bash
bash Tools/DeviceTools/Scripts/build.sh
```

Рабочие файлы находятся в `device-build/`, готовые Windows/Sources ZIP — в корне репозитория. Параллельность выбирается по числу CPU, максимум 8; переопределение — `IPA_DEVICE_JOBS`. Git загружает только закреплённый снимок без полной истории.

Упаковщик один раз индексирует каталоги DLL, обходит зависимости без циклических повторов, запрещает MSYS runtime и вычисляет SHA-256 блоками по 1 МиБ. Каждый пакет собирается в чистом временном каталоге; ошибка лицензии не заменяет предыдущий пакет, ошибка сжатия не заменяет предыдущий ZIP.

## Обновления

GitHub Actions ежедневно в **08:17 МСК** проверяет стабильные Releases шести исходных проектов. Предварительные выпуски и новые коммиты без релиза игнорируются. Новый стабильный тег должен включать закреплённую ревизию; расхождение истории останавливает обновление.

После сборки проверяются контрольные суммы, запуск EXE вне MSYS2 и используемые GUI команды, включая сетевые флаги. Затем сохраняются версии и публикуется выпуск **device-tools-v…** с бинарниками, исходниками и чейнджлогом. Публичные архивы не перезаписываются. Восстановление черновика требует совпадения всего дерева входных файлов и ранее пройденных Windows-проверок. Обнаружение и установку на реальный iPhone проверяют отдельно.

## Источники и лицензии

Исходные проекты: [libplist](https://github.com/libimobiledevice/libplist), [libimobiledevice-glue](https://github.com/libimobiledevice/libimobiledevice-glue), [libusbmuxd](https://github.com/libimobiledevice/libusbmuxd), [libtatsu](https://github.com/libimobiledevice/libtatsu), [libimobiledevice](https://github.com/libimobiledevice/libimobiledevice), [ideviceinstaller](https://github.com/libimobiledevice/ideviceinstaller).

В Windows ZIP находятся `sources.json` с ревизиями, `LICENSES`, `msys2-packages.txt` и манифест SHA-256. Рядом публикуется Sources ZIP с исходниками шести проектов. Сторонние компоненты сохраняют LGPL/GPL и другие лицензии; MIT интерфейса на них не распространяется.

Зависимости и компилятор — [MSYS2](https://www.msys2.org/). [Рецепты сборки](https://github.com/msys2/MINGW-packages), [каталог пакетов](https://packages.msys2.org/), [исходные пакеты](https://repo.msys2.org/mingw/sources/).
