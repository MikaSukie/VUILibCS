# ⚠️ Documentation currently written by AI — will be rewritten by MikaSukie

# VUILibCS
**Vectimate UserInterface Lib (Simple scaffolding UI library for C#)**  
Lightweight retained-mode GUI framework built for OpenTK / SkiaSharp–based rendering.  
Provides immediate usability with buttons, sliders, textboxes, checkboxes, notifications, modals, and scrollable containers.

---

## 🧩 Overview

`VectUserInterfaceLib` is a modular, OpenGL-friendly UI toolkit designed to give you a quick, no-dependency interface layer for your apps or tools.  
It handles:
- Input forwarding (mouse, keyboard, text)
- Simple layout/positioning
- Interactive elements (buttons, sliders, etc.)
- Notifications and modals
- Text caching for performance
- Theming through a shared `DSTheme`

This library is **self-contained** — no external GUI frameworks required.

---

## 🚀 Quick Start

**Example entry point (Program.cs):**

```csharp
using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;
using Vectimate;

public static class Program
{
    public static void Main()
    {
        var window = new GameWindow(GameWindowSettings.Default, new NativeWindowSettings()
        {
            Title = "VUILibCS Demo",
            Size = new Vector2i(1024, 720)
        });

        VectUserInterfaceLib ui = null!;

        window.Load += () =>
        {
            ui = new VectUserInterfaceLib();
            ui.SetViewport(window.Size.X, window.Size.Y, window.FramebufferSize.X, window.FramebufferSize.Y);

            var btn = new VectUserInterfaceLib.UIButton(new Vector2(20, 60), new Vector2(200, 40), "Click me!");
            btn.OnClick = () => ui.ShowNotification("Button clicked!", "tr");
            ui.AddButton(btn);
        };

        window.TextInput += (e) => ui.HandleTextInput(e.AsString);
        window.KeyDown += (e) => ui.HandleKeyDown(e);
        window.RenderFrame += (args) =>
        {
            ui.ProcessMouse(window.MousePosition, 
                window.IsMouseButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left),
                window.IsMouseButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right));

            ui.Update((float)args.Time);
            ui.Render(window.Size.X, window.Size.Y);
            window.SwapBuffers();
        };

        window.Run();
    }
}
