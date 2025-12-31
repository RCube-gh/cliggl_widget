# Focus HUD Widget 🎯

A translucent, modern desktop widget designed to help you focus on your daily tasks. It sits unobtrusively on your screen, syncing tasks from **ClickUp** and tracking time via **Toggl Track**.

![Focus HUD Screenshot](https://via.placeholder.com/800x400?text=Focus+HUD+Preview)
*(Note: Replace with actual screenshot)*

## ✨ Features

- **Modern Glassmorphism UI**:
  - iOS-like frosted glass blur effect (Acrylic Blur).
  - Smooth expansion/collapse animations.
  - Non-intrusive "Pill" shape Design.
- **ClickUp Integration**:
  - Fetches your daily tasks directly from a specified ClickUp List.
  - "Shimmer" loading effect during data fetch.
  - Caches task lists to minimize API usage (15-min expiry).
- **Toggl Track Sync**:
  - One-click timer to start/stop tracking time on Toggl.
  - Automatically uses the selected task name for the time entry.
- **Performance**:
  - Built with **.NET / C# WPF** for native performance.
  - Low memory footprint compared to web-based widgets.

## 🛠 Prerequisites

- Windows 10 (1803+) or Windows 11.
- [.NET SDK](https://dotnet.microsoft.com/download) (Version 8.0 or later recommended).
- **Toggl Track** Account & API Token.
- **ClickUp** Account & API Token (and a List ID to pull tasks from).

## 🚀 Getting Started

1.  **Clone the repository**
    ```bash
    git clone https://github.com/your-username/focus-hud.git
    cd focus-hud
    ```

2.  **Configure API Tokens**
    - Rename `appsettings.template.json` to `appsettings.json`.
    - Open `appsettings.json` and enter your credentials:
    ```json
    {
        "Toggl": {
            "ApiToken": "YOUR_TOGGL_API_TOKEN"
        },
        "ClickUp": {
            "ApiToken": "YOUR_CLICKUP_API_TOKEN",
            "ListId": "YOUR_CLICKUP_LIST_ID"
        }
    }
    ```

3.  **Run the Application**
    ```bash
    dotnet run
    ```

## 🎮 How to Use

- **Launch**: The widget appears on your desktop.
- **Select Task**: Click the task name text. The window expands to show your ClickUp tasks. Click a task to select it.
- **Refresh Tasks**: Press **`R`** while the widget is active to force a refresh from ClickUp.
- **Start Timer**: Click the **Play (▶)** button. This starts a timer in Toggl with the selected task name.
- **Stop Timer**: Click **Pause (⏸)** to stop the timer.

## 📦 Build for Distribution

To create a standalone `.exe` file:
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## 📝 License

MIT

---
*Created for personal productivity enhancement.*
