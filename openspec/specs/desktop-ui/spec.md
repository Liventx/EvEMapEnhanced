# desktop-ui

Top-level menus, dialogs and other shell-level UI affordances in the Avalonia desktop app.

## Requirement: Help menu in the top menu bar
The main window SHALL expose a top-level «Помощь» menu with entries to check for application
updates and open an «О программе» dialog.

#### Scenario: Help menu is available after the visible map menu
- GIVEN the main window is open
- WHEN the user looks at the top menu bar
- THEN a top-level «Помощь» menu appears after «Карта»
- AND it contains «Проверка обновлений» and «О программе»

#### Scenario: Check for updates reports the current release state
- GIVEN the app can reach GitHub's latest-release API
- WHEN the user chooses «Проверка обновлений»
- THEN a short-lived bottom-left toast explains whether a newer release exists or the installed
  version is already current
- AND when a newer release exists the releases page opens in the default browser

#### Scenario: About dialog shows product details and a GitHub link
- GIVEN the main window is open
- WHEN the user chooses «О программе»
- THEN a modal dialog shows the product name, the installed application version, a short
  description of the app's purpose, the author name «Livent», and a clickable GitHub link to
  the project repository

## Requirement: Debug tools hidden from the production menu
The top-level «Отладка» menu SHALL be hidden from the production main window menu bar while
preserving its existing developer-tool wiring in the UI tree.

#### Scenario: Debug menu is not visible
- GIVEN the main window is open
- WHEN the user looks at the top menu bar
- THEN the «Отладка» menu is not visible
- AND the normal user-facing menus remain available
