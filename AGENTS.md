# AGENTS.md

Инструкции для ИИ-агентов (OpenCode, Copilot, Cursor и др.), работающих в этом репозитории.

## Что это

**MimiTap** — Windows-приложение (WPF, .NET 10): прозрачный оверлей-персонаж поверх всех окон.
В покое показывается `1.png` (Мими замахивается), пока зажата левая кнопка мыши — `2.png`
(Мими нажала). Персонаж — **Мими**, OC художницы **Typh**. Помимо приложения в репозитории есть
свой WPF-установщик и лендинг на GitHub Pages.

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
| `NativeMethods.cs` | Win32: ex-стили окна, глобальный хук мыши, хоткеи |
| `SelfTest.cs` | режим `--selftest` |
| `Assets\1.png`, `Assets\2.png`, `Assets\MimiTap.ico` | встроенные ресурсы (**канонические**) |
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

## Проверка

Автотестов в репозитории **нет**. Проверяй так:

1. `dotnet build .\MimiTap.csproj -c Release` — сборка без ошибок.
2. `.\publish\MimiTap.exe --selftest` — прогони, проверь `%AppData%\MimiTap\selftest.log`.
3. Поведение вручную: прозрачность/Topmost, прохождение кликов насквозь, переключение
   `1.png ⇄ 2.png` (три режима клика), звук, область срабатывания, перетаскивание, трей, хоткей.

## Соглашения по коду

- Отступ — 4 пробела; стиль — как в соседних файлах.
- Комментарии и все тексты интерфейса — **по-русски**. В UI пишут «ЛКМ», «ПКМ», «1.png/2.png».
- Ресурсы — только через `pack://application:,,,/Assets/...`.
- Не добавляй внешние пакеты и не тяни тяжёлые фреймворки.
- Оверлей обязан остаться: `Topmost`, без перехвата фокуса, с прозрачными для кликов участками.

## Подводные камни (важно)

1. **`installer\` — отдельный проект.** В `MimiTap.csproj` он исключён:
   `<Compile/Page/ApplicationDefinition/None/Resource/EmbeddedResource Remove="installer\**" />`.
   Без этих исключений SDK-глоб затянет файлы установщика в основное приложение и сборка
   упадёт с `CS0102`/`CS0111` (дубли `MainWindow`, `InitializeComponent`). Не удаляй их.
2. **Порядок сборки:** publish → установщик. Установщик берёт `..\..\publish\MimiTap.exe`.
3. **.NET 10 обязателен.** Целевой рантайм — .NET 10 Desktop Runtime; SDK у разработчика — 10.x.
4. **Арты ≠ MIT.** Код под MIT (`LICENSE`), но `Assets\*.png` и `Assets\MimiTap.ico` —
   работы художницы **Typh** (персонаж Мими). Не перелицензируй их и не удаляй атрибуцию.

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
  Текущий — `v1.0.0`: <https://github.com/w1zzyakik/MimiTap/releases/tag/v1.0.0>

## На заметку (не трогать без причины)

- Корневые `1.png`, `2.png`, `icon.ico` **не используются** кодом — это дубли `Assets\...`,
  попавшие в историю. Канонические ресурсы — в `Assets\`.
- Запущенные `publish\`, `dist\`, `bin\`, `obj\` — артефакты сборки (в `.gitignore`).
- В UI установщика (`installer\MimiTapSetup\MainWindow.xaml`) и настройках
  (`SettingsWindow.xaml`) ещё осталась старая формулировка «картинка поверх всех окон»;
  на сайте и в README уже новый текст «твой клик — её нажатие». Можно унифицировать.
