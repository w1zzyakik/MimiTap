# AGENTS.md

Инструкции для ИИ-агентов (OpenCode, Copilot, Cursor и др.), работающих в этом репозитории.

## Что это

**MimiTap** — Windows-приложение (WPF, .NET 10): прозрачный оверлей-персонаж поверх всех окон.
В покое показывается `1.png` (Мими замахивается), пока зажат «взведённый» ввод — `2.png`
(Мими нажала); по умолчанию это левая кнопка мыши, но в настройках можно задать до 10 своих
привязок (клавиши и кнопки мыши) или включить «реагировать на все нажатия». Кадров может быть
несколько наборов — **пресеты** (в поставке два: основной и дополнительный). Персонаж — **Мими**,
OC художницы **Typh**. Помимо приложения в репозитории есть свой WPF-установщик и лендинг
на GitHub Pages.

Репозиторий публичный: <https://github.com/w1zzyakik/MimiTap>

## Персонаж (про кого проект)

Проект целиком сделан **по персонажу Мими** — это не абстрактная «картинка с котиком»,
а конкретная OC. Не забывай про неё в текстах, атрибуции и при добавлении новых материалов.

- **Мими** — антропоморфная собачка, OC (original character) и маскот художницы **Typh**.
- Страница персонажа: <https://animation-meme.fandom.com/wiki/Typh/Mimi>
- Известна по видео-мему **«Kiss Me»**.
- Смысл кадров: `1.png` — Мими замахивается на кнопку, `2.png` — Мими уже нажала.
  Отсюда и позиционирование «твой клик — её нажатие».

**Как писать имя:**

- По-русски — **«Мими»** (женский род: «Мими нажала», «её нажатие», «она»). Не «Мима».
- Латиницей — `Mimi` (например, в URL и имени файла вики).

**Атрибуция обязательна:** в README, на сайте (футер) и в релизе указано, что Мими — OC Typh.
Не удаляй эти упоминания. Права на персонажа и арты принадлежат художнице, лицензия MIT их
не покрывает (см. «Подводные камни» → п. 4).

## Стек

- **.NET 10** (`net10.0-windows`), **WPF** + WinForms (иконка в трее), C#.
- `Nullable=enable`, `ImplicitUsings=disable` — пиши `using` явно (см. `MimiTap.csproj`).
- Сторонних NuGet-зависимостей нет — и не нужны. Всё на BCL + Win32 через P/Invoke.
- Сайт — один статический `docs/index.html` (HTML + CSS + ванильный JS), хостинг GitHub Pages.

## Структура

| Путь | Назначение |
|---|---|
| `MimiTap.csproj` | основное приложение (WinExe). Подпапка `installer\` из него исключена. |
| `App.xaml(.cs)` | точка входа, единый экземпляр, тема, лог ошибок |
| `AppController.cs` | трей, глобальный хук мыши, горячие клавиши, применение настроек |
| `OverlayWindow.xaml(.cs)` | прозрачное окно поверх всех, альфа-маска, перетаскивание, размер/позиция |
| `SettingsWindow.xaml(.cs)` | окно настроек (закрывается в трей) |
| `AppSettings.cs` | модель настроек + `SettingsStore`: JSON (UTF-8 BOM), распаковка встроенных ресурсов |
| `SoundService.cs` | звук нажатия (обрезка до 1 с), автозапуск с Windows |
| `NativeMethods.cs` | Win32: ex-стили окна, глобальные хуки мыши и клавиатуры, хоткеи |
| `Inputs.cs` | привязки ввода: токены `MouseLeft` / `Key.Space`, 10 слотов, `IsDown`/`VirtualKey` |
| `SelfTest.cs` | режим `--selftest` |
| `Localization\Localization.cs` | движок локализации `Loc` (`T`/`Format`/`SetLanguage`) + разметка `{loc:Tr Ключ}` |
| `Localization\Strings\*.json` | строки интерфейса по языкам: `ru`, `en`, `es`, `pt-BR`, `uk`, `pl` |
| `TutorialWindow.xaml(.cs)` | мини-туториал (выноски вокруг оверлея) при первом запуске |
| `Assets\1.png`, `Assets\2.png`, `Assets\p2_1.png`, `Assets\p2_2.png`, `Assets\MimiTap.ico` | встроенные ресурсы (**канонические**); `p2_*` — второй пресет |
| `installer\MimiTapSetup\` | отдельный проект установщика (свой `.csproj`) |
| `docs\` | сайт; публикуется GitHub Pages из ветки `main`, папка `/docs` |
| `LICENSE` | MIT (код). Арты Мими — исключение, см. ниже |

## Сборка и запуск

Все команды — из корня репозитория, PowerShell.

```powershell
# обычная сборка
dotnet build .\MimiTap.csproj -c Release

# публикация: один .exe + .pdb (нужен .NET 10 Desktop Runtime)
dotnet publish .\MimiTap.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish

.\publish\MimiTap.exe              # запуск
.\publish\MimiTap.exe --settings   # сразу настройки
.\publish\MimiTap.exe --minimized  # скрыто в трей (используется автозапуском)
.\publish\MimiTap.exe --selftest   # самопроверка → %AppData%\MimiTap\selftest.log
.\publish\MimiTap.exe --diag       # журнал кликов и hit-test → diag.log
```

## Установщик

Установщик **встраивает уже готовый** `publish\MimiTap.exe`, поэтому сначала собери publish,
потом установщик:

```powershell
dotnet publish .\installer\MimiTapSetup\MimiTapSetup.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist
```

Результат: `dist\MimiTapSetup.exe`. Если `publish\MimiTap.exe` нет — сборка упадёт.

Флаги установщика: `/S` (`--silent`), `/D=<путь>`, `/NODESKTOP`, `/NOAUTOSTART`, `/NORUN`,
`/DELETEUSERDATA`, `--install`, `--uninstall`.

Язык установщика определяется по системе (6 языков). Ярлыки в «Пуск» создаются с
локализованными именами, поэтому при удалении группа `...\Programs\MimiTap` сносится
рекурсивно — иначе ярлыки на другом языке остались бы.

## Проверка

Автотестов в репозитории **нет**. Проверяй так:

1. `dotnet build .\MimiTap.csproj -c Release` — сборка без ошибок.
2. `.\publish\MimiTap.exe --selftest` — прогони, проверь `%AppData%\MimiTap\selftest.log`.
3. Поведение вручную: прозрачность/Topmost, прохождение кликов насквозь, переключение
   `1.png ⇄ 2.png` (три режима клика), звук, область срабатывания, свои привязки (захват
   по клавише/кнопке мыши, «реагировать на всё»), плавность перетаскивания, трей, хоткей.

## Соглашения по коду

- Отступ — 4 пробела; стиль — как в соседних файлах.
- Комментарии — **по-русски**. Тексты интерфейса не хардкодь: они идут через локализацию
  (см. «Локализация»). В UI пишут «ЛКМ», «ПКМ», «1.png/2.png».
- Ресурсы — только через `pack://application:,,,/Assets/...`.
- Не добавляй внешние пакеты и не тяни тяжёлые фреймворки.
- Оверлей обязан остаться: `Topmost`, без перехвата фокуса, с прозрачными для кликов участками.
- **Пресеты** — это наборы кадров (`ImagePreset` в `AppSettings.cs`). «Живые» картинки
  оверлея всегда лежат в `IdleImagePath`/`ClickImagePath`; активный пресет синхронизируется
  с ними в `Normalize()`. Новые кадры добавляй в `Assets\` и в `<Resource>` в `MimiTap.csproj`.
  Дефолтное имя пресета — пустая строка, в UI показывается локализованный `PresetDefaultName`.

## Локализация

- Языки: `ru`, `en`, `es`, `pt-BR`, `uk`, `pl`. Строки — в `Localization\Strings\<код>.json`
  (ключ → текст; ключи PascalCase **без точек** — точка ломает путь индексера в XAML-биндинге).
- Движок — `Localization\Localization.cs`: `Loc.T("Ключ")`, `Loc.Format("Ключ", args)`,
  `Loc.SetLanguage("код")`. В XAML — `{loc:Tr Ключ}` (живое обновление через индексатор `Loc`).
- Пустой язык/`auto` → язык системы, неизвестный → `en` (он же словарь-запасной).
- Наборы ключей во всех 6 словарях обязаны совпадать: добавляешь ключ — добавляй сразу во все.
- Установщик переиспользует тот же движок: `Localization.cs` слинкован, JSON встроены
  (`installer\MimiTapSetup\MimiTapSetup.csproj`), язык — по системе, ключи с префиксом `Installer`.

Имена клавиш для своих привязок — ключи `BindKey*` (`BindKeySpace`, `BindKeyEnter`, `BindKeyUp`…),
кнопки мыши — `BindMouse*`. Их тоже надо держать во всех 6 словарях; переводятся только
подписи, сами токены (`Key.Space`) в JSON настроек не зависят от языка.

## Подводные камни (важно)

1. **`installer\` — отдельный проект.** В `MimiTap.csproj` он исключён:
   `<Compile/Page/ApplicationDefinition/None/Resource/EmbeddedResource Remove="installer\**" />`.
   Без этих исключений SDK-глоб затянет файлы установщика в основное приложение и сборка
   упадёт с `CS0102`/`CS0111` (дубли `MainWindow`, `InitializeComponent`). Не удаляй их.
2. **Порядок сборки:** publish → установщик. Установщик берёт `..\..\publish\MimiTap.exe`.
3. **.NET 10 обязателен.** Целевой рантайм — .NET 10 Desktop Runtime; SDK у разработчика — 10.x.
4. **Арты ≠ MIT.** Код под MIT (`LICENSE`), но `Assets\*.png` и `Assets\MimiTap.ico` —
   работы художницы **Typh** (персонаж Мими). Не перелицензируй их и не удаляй атрибуцию.
5. **Пропажа оверлея.** Слоёное прозрачное окно может «потерять» картинку после инструментов
   захвата экрана или смены разрешения/DPI. В `OverlayWindow` на это есть `ReassertSurface()`
   (хук `WM_DISPLAYCHANGE`/`WM_DPICHANGED`/`WM_DWMCOMPOSITIONCHANGED`/`WM_SETTINGCHANGE`),
   лёгкий таймер `NudgeSurface()` и кламп позиции в `ClampToVisibleScreen()` — не удаляй.
   Плюс `AppController.IsScreenCaptureForeground()` не даёт «Ножницам» (Win+Shift+S)
   перетаскивать Мими при выделении области снимка.

## Сайт (GitHub Pages)

- Правки только в `docs\index.html` и картинках `docs\img\`.
- Оформление: фиолетово-чёрная палитра (OLED dark + violet), шрифты Russo One / Chakra Petch,
  SVG-иконки, уважение `prefers-reduced-motion`.
- Pages пересобирается автоматически после пуша в `main`. Проверка:
  <https://w1zzyakik.github.io/MimiTap/> и API `GET /repos/w1zzyakik/MimiTap/pages/builds/latest`.

## Git и релизы

- Идентичность: `Wizzy <211014699+w1zzyakik@users.noreply.github.com>`.
- Ветка `main`. Сообщения коммитов — на английском, повелительное наклонение, коротко.
- Релиз: тег `vX.Y.Z` + ассет `dist\MimiTapSetup.exe`.
  Текущий — `v1.1.0`: <https://github.com/w1zzyakik/MimiTap/releases/tag/v1.1.0>

## На заметку (не трогать без причины)

- Корневые `1.png`, `2.png`, `icon.ico` **не используются** кодом — это дубли `Assets\...`,
  попавшие в историю. Канонические ресурсы — в `Assets\`.
- Запущенные `publish\`, `dist\`, `bin\`, `obj\` — артефакты сборки (в `.gitignore`).
- Формулировка унифицирована: везде «твой клик — её нажатие» (сайт, README, установщик,
  настройки); старый текст «картинка поверх всех окон» убран.
