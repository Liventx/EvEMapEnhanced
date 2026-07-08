# EvE Map Enhanced

Desktop-карта вселенной EVE Online с планировщиком маршрутов, jump range и live-трекингом персонажей. Интерфейс на русском языке.

**Текущая версия:** [Alfa 1.1](https://github.com/Liventx/EvEMapEnhanced/releases/tag/alfa-1.1)

## Скачать

Windows x64 (self-contained, .NET не нужен):

- **[EvEMapEnhanced-Setup-Alfa-1.1.exe](https://github.com/Liventx/EvEMapEnhanced/releases/download/alfa-1.1/EvEMapEnhanced-Setup-Alfa-1.1.exe)** — установщик Inno Setup
- Все релизы: [Releases](https://github.com/Liventx/EvEMapEnhanced/releases)

## Возможности

### Карта

- **Schematic (Dotlan)** — схематическая карта вселенной с позициями регионов и систем, как на dotlan.evemaps.com
- **Standard** — реальные координаты систем (проекция X / −Z в световых годах)
- Dotlan-style **пластины систем** с тремя уровнями детализации (имя + NPC kills → только имя → точка)
- Раскраска по **NPC kills** (ESI) или **security status**
- Названия регионов на обзоре вселенной; при приближении — названия систем

### Маршруты и прыжки

- Построение маршрута **ОТ → ДО** через гейты и jump-дrives / jump bridges
- Подсветка **jump range** от выбранной системы (класс корабля, навыки, метод прыжка)
- **Jump Range mini-map** — отдельная панель в реальном масштабе (LY) для планирования цепочек Black Ops
- Симуляция **jump fatigue** и расчёт топлива
- Сохранение маршрутов и заметок к системам

### Live-трекинг

- Вход через **ESI SSO** (CCP OAuth)
- Маяк **«вы здесь»** на карте основного профиля
- Отдельные маяки для профилей **Cyno** (голубой) и **SC** (тёмно-синий)
- Режим **Focus** — фиксирует origin jump range при кликах по карте

### Активность и структуры

- Подсветка **PvP-активности** по zKillboard (hot / recent / NPC capital) — в jump range или по всему nullsec
- Пользовательские **структуры** (Fortizar, Keepstar, Ansiblex, cyno beacons и др.) на карте
- Открытие zKillboard для системы из контекстного меню

## Быстрый старт

1. Скачайте и установите последний релиз.
2. Запустите приложение.
3. Меню **Данные → Скачать / обновить SDE** — загрузите Static Data Export (нужен интернет, один раз).
4. *(Опционально)* **Аккаунт → Войти в EVE Online** — для навыков, локации и NPC kills.
5. Кликните систему на карте, задайте **ОТ** / **ДО**, нажмите **Построить маршрут**.

SDE и пользовательские данные хранятся локально в `%AppData%\EvEMapEnhanced\`.

## Сборка из исходников

Требования: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Windows x64. Для установщика — [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
git clone https://github.com/Liventx/EvEMapEnhanced.git
cd EvEMapEnhanced

# Запуск из исходников
dotnet run --project src/EvEMapEnhanced.Desktop

# Release + установщик
powershell -ExecutionPolicy Bypass -File installer/build-release.ps1
```

Результат сборки: `release/publish/` (portable) и `release/EvEMapEnhanced-Setup-Alfa-1.1.exe` (если установлен Inno Setup).

```powershell
dotnet test
```

## Структура решения

| Проект | Назначение |
|--------|------------|
| `EvEMapEnhanced.Core` | Доменная логика: маршрутизация, прыжки, модели |
| `EvEMapEnhanced.Data` | SDE, SQLite, ESI/zKillboard клиенты |
| `EvEMapEnhanced.Desktop` | UI (Avalonia), `MapControl`, главное окно |
| `EvEMapEnhanced.Cli` | Тонкая CLI-обёртка для скриптов |

Поведение приложения описано в [`openspec/specs/`](openspec/specs/) (living documentation).

## Alfa 1.1 — что нового

- Названия регионов рисуются **поверх всех оверлеев** на широком масштабе
- Маяки, PvP-подсветки и другие fixed-pixel маркеры **уменьшаются** при сильном отдалении на обзоре вселенной

## Дисклеймер

EvE Map Enhanced — сторонний инструмент, не аффилирован с CCP Games. EVE Online и связанные материалы — торговые марки CCP hf. Данные SDE предоставляются CCP; zKillboard и Dotlan — независимые сервисы сообщества.
