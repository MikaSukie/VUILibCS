# VUILibCS
**Vectimate UserInterface Lib (Simple scaffolding UI library for C#)**  
Lightweight retained-mode GUI framework built for OpenTK / SkiaSharp–based rendering.  
Provides immediate usability with buttons, sliders, textboxes, checkboxes, notifications, modals, and scrollable containers.

---
## 🧩 Quick Start (including custom cursor. NOT REQUIRED)
dotnet add package opentk  <br>
dotnet add package skiasharp  <br>

**Example (Program.cs):**

```csharp
using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL4;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Vectimate
{
    public class Program
    {
        static int cursorTexture;
        static int cursorVao, cursorVbo;
        static int shaderProgram;
        static int cursorWidth, cursorHeight;

        public static void Main()
        {
            var nativeSettings = new NativeWindowSettings()
            {
                ClientSize = new Vector2i(800, 600),
                Title = "VectUserInterfaceLib Demo",
                APIVersion = new Version(4, 5),
                WindowBorder = WindowBorder.Resizable
            };

            using var window = new GameWindow(GameWindowSettings.Default, nativeSettings);
            VectUserInterfaceLib? ui = null;

            window.Load += () =>
            {
                GL.ClearColor(0.1f, 0.1f, 0.15f, 1f);

                ui = new VectUserInterfaceLib();
                ui.SetViewport(window.Size.X, window.Size.Y, window.FramebufferSize.X, window.FramebufferSize.Y);

                // Hide system cursor
                window.CursorState = CursorState.Hidden;

                // Load cursor texture (optional)
                string cursorPath = Path.Combine("Cursors", "cursor.png");
                if (File.Exists(cursorPath))
                {
                    using var image = Image.Load<Rgba32>(cursorPath);
                    cursorWidth = image.Width;
                    cursorHeight = image.Height;

                    cursorTexture = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, cursorTexture);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

                    var pixels = new byte[4 * cursorWidth * cursorHeight];
                    image.CopyPixelDataTo(pixels);

                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                        cursorWidth, cursorHeight, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
                }

                // Setup cursor VAO/VBO
                cursorVao = GL.GenVertexArray();
                cursorVbo = GL.GenBuffer();
                GL.BindVertexArray(cursorVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, cursorVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, 6 * 4 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
                GL.BindVertexArray(0);

                // Simple shader for cursor
                int vertexShader = GL.CreateShader(ShaderType.VertexShader);
                GL.ShaderSource(vertexShader, @"
                    #version 330 core
                    layout(location = 0) in vec2 aPos;
                    layout(location = 1) in vec2 aTex;
                    out vec2 TexCoord;
                    void main() {
                        gl_Position = vec4(aPos, 0.0, 1.0);
                        TexCoord = aTex;
                    }");
                GL.CompileShader(vertexShader);

                int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                GL.ShaderSource(fragmentShader, @"
                    #version 330 core
                    in vec2 TexCoord;
                    out vec4 FragColor;
                    uniform sampler2D texture0;
                    void main() {
                        FragColor = texture(texture0, TexCoord);
                    }");
                GL.CompileShader(fragmentShader);

                shaderProgram = GL.CreateProgram();
                GL.AttachShader(shaderProgram, vertexShader);
                GL.AttachShader(shaderProgram, fragmentShader);
                GL.LinkProgram(shaderProgram);
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);

                // --- Demo UI elements ---
                var textBox = new VectUserInterfaceLib.UITextBox(new Vector2(20, 80), new Vector2(200, 40));
                ui.AddTextBox(textBox);

                var button = new VectUserInterfaceLib.UIButton(new Vector2(20, 20), new Vector2(140, 40), "Print Text")
                {
                    OnClick = () => Console.WriteLine($"Textbox value: \"{textBox.Text}\"")
                };
                ui.AddButton(button);

                var checkbox = new VectUserInterfaceLib.UICheckbox(new Vector2(180, 26), new Vector2(20, 20), false)
                {
                    OnToggle = (state) => Console.WriteLine($"Checkbox: {state}")
                };
                ui.AddElement(checkbox);

                var slider = new VectUserInterfaceLib.UISlider(new Vector2(20, 140), new Vector2(200, 24))
                {
                    Value = 0.5f
                };
                ui.AddSlider(slider);
            };

            window.Resize += (e) =>
                ui?.SetViewport(window.Size.X, window.Size.Y, window.FramebufferSize.X, window.FramebufferSize.Y);

            window.TextInput += (e) => ui?.HandleTextInput(e.AsString);
            window.KeyDown += (e) => ui?.HandleKeyDown(e);

            window.RenderFrame += (e) =>
            {
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                var mouse = window.MousePosition;
                bool left = window.IsMouseButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left);
                bool right = window.IsMouseButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right);

                ui?.ProcessMouse(mouse, left, right);
                ui?.Update((float)e.Time);
                ui?.Render(window.Size.X, window.Size.Y);

                // Draw cursor
                if (cursorTexture != 0)
                {
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                    GL.UseProgram(shaderProgram);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, cursorTexture);
                    GL.Uniform1(GL.GetUniformLocation(shaderProgram, "texture0"), 0);
                    GL.BindVertexArray(cursorVao);

                    float x = (float)mouse.X;
                    float y = window.Size.Y - (float)mouse.Y;
                    float l = 2 * x / window.Size.X - 1f;
                    float r = 2 * (x + cursorWidth) / window.Size.X - 1f;
                    float t = 2 * y / window.Size.Y - 1f;
                    float b = 2 * (y - cursorHeight) / window.Size.Y - 1f;

                    float[] verts = {
                        l, t, 0f, 0f,
                        r, t, 1f, 0f,
                        r, b, 1f, 1f,
                        l, t, 0f, 0f,
                        r, b, 1f, 1f,
                        l, b, 0f, 1f
                    };

                    GL.BindBuffer(BufferTarget.ArrayBuffer, cursorVbo);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, verts.Length * sizeof(float), verts);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                    GL.Disable(EnableCap.Blend);
                }

                window.SwapBuffers();
            };

            window.Run();
        }
    }
}
```
