using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SkiaSharp;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vectimate
{
    public static class DSTheme
    {
        public static readonly Vector3 UIBase = new Vector3(0.20f, 0.22f, 0.26f);
        public static readonly Vector3 UILighter = new Vector3(0.36f, 0.38f, 0.42f);
        public static readonly Vector3 UIDarker = new Vector3(0.12f, 0.12f, 0.16f);
        public static readonly Vector3 UIHighlight = new Vector3(0.78f, 0.78f, 0.8f);
        public static readonly Vector3 UIText = new Vector3(1.0f, 1.0f, 1.0f);
        public static readonly Vector3 UIBorder = new Vector3(0.08f, 0.08f, 0.09f);
    }

    public class DSShader : IDisposable
    {
        public int Handle { get; private set; }
        private bool _disposed = false;

        public DSShader(string vertexSrc, string fragmentSrc)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertexSrc);
            GL.CompileShader(vs);
            CheckCompile(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragmentSrc);
            GL.CompileShader(fs);
            CheckCompile(fs);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vs);
            GL.AttachShader(Handle, fs);
            GL.LinkProgram(Handle);
            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
                throw new Exception($"Program linking failed: {GL.GetProgramInfoLog(Handle)}");

            GL.DetachShader(Handle, vs);
            GL.DetachShader(Handle, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private void CheckCompile(int shader)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
                throw new Exception(GL.GetShaderInfoLog(shader));
        }

        public void Use() => GL.UseProgram(Handle);

        public void Dispose()
        {
            if (!_disposed)
            {
                if (Handle != 0) GL.DeleteProgram(Handle);
                _disposed = true;
            }
        }
    }

    internal static class EmbeddedShaders
    {
        public const string Vertex = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;

uniform mat4 uProjection;
uniform vec3 uColor;

out vec3 fragColor;
out vec2 vTex;

void main()
{
    fragColor = uColor;
    vTex = aTexCoord;
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
}";

        public const string Fragment = @"
#version 330 core

in vec3 fragColor;
in vec2 vTex;

out vec4 FragColor;

uniform sampler2D uTexture;
uniform int uUseTexture;
uniform float uAlpha;

uniform vec2 uSize;
uniform float uRadius;
uniform int uRounded;

void main()
{
    if (uRounded == 1)
    {
        vec2 p = vTex * uSize;

        float r = uRadius;
        vec2 tl = vec2(r, r);
        vec2 tr = vec2(uSize.x - r, r);
        vec2 bl = vec2(r, uSize.y - r);
        vec2 br = vec2(uSize.x - r, uSize.y - r);

        float dist = 0.0;

        if (p.x < r && p.y < r)
            dist = distance(p, tl);
        else if (p.x > uSize.x - r && p.y < r)
            dist = distance(p, tr);
        else if (p.x < r && p.y > uSize.y - r)
            dist = distance(p, bl);
        else if (p.x > uSize.x - r && p.y > uSize.y - r)
            dist = distance(p, br);

        if (dist > r)
            discard;
    }

    vec4 base =
        (uUseTexture == 1)
        ? texture(uTexture, vTex)
        : vec4(1.0);

    FragColor = base * vec4(fragColor, uAlpha);
}";
    }

    public abstract class UIElement : IDisposable
    {
        public Vector2 Position;
        public Vector2 Size;
        public int ZIndex = 0;
        public bool IsHovered;
        public bool IsFocused;
        public bool Visible = true;
        public int TabIndex = -1;
        public Vector2 HitPadding = Vector2.Zero;
        public bool InputTransparent = false;
        public virtual void ResetTextCache() { }
        public virtual void DrawBackground(VectUserInterfaceLib ui) { }
        public virtual void DrawText(VectUserInterfaceLib ui) { }
        public virtual void Update(VectUserInterfaceLib ui, float dt) { }
        public virtual void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button) { }
        public virtual void OnMouseUp(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button) { }
        public virtual void OnRightClick(VectUserInterfaceLib ui, Vector2 localPos) { }
        public virtual void OnKeyDown(VectUserInterfaceLib ui, KeyboardKeyEventArgs e) { }
        public virtual void Dispose() { }

        public Vector2 ToLocal(Vector2 fbPoint, VectUserInterfaceLib ui)
        {
            Vector2 posFb = ui.ToFramebuffer(Position);
            return new Vector2(fbPoint.X - posFb.X, fbPoint.Y - posFb.Y);
        }

        public virtual bool HitTest(Vector2 fbPoint, VectUserInterfaceLib ui)
        {
            Vector2 posFb = ui.ToFramebuffer(Position);
            Vector2 sizeFb = ui.ToFramebufferSize(Size + HitPadding * 2f);
            Vector2 paddedPos = posFb - ui.ToFramebuffer(HitPadding);
            return fbPoint.X >= paddedPos.X && fbPoint.X <= paddedPos.X + sizeFb.X && fbPoint.Y >= paddedPos.Y && fbPoint.Y <= paddedPos.Y + sizeFb.Y;
        }
    }

    public class VectUserInterfaceLib : IDisposable
    {
        private int _vao;
        private int _vbo;
        private int _texVao;
        private int _texVbo;
        private int _ebo;
        private readonly DSShader _shader;
        private int _overlayTex = -1;
        private int _whiteTex = -1;
        private int _cornerTex = -1;
        private readonly List<UIElement> _elements = new();
        public IEnumerable<UIButton> Buttons => _elements.OfType<UIButton>();
        public IEnumerable<UISlider> Sliders => _elements.OfType<UISlider>();
        public IEnumerable<UITextBox> TextBoxes => _elements.OfType<UITextBox>();
        private int _windowWidth = 1;
        private int _windowHeight = 1;
        private int _framebufferWidth = 1;
        private int _framebufferHeight = 1;
        internal float _scaleX = 1f;
        internal float _scaleY = 1f;
        internal float _scaleAvg = 1f;

        public int ViewportWidth => _windowWidth;
        public int ViewportHeight => _windowHeight;
        public int FramebufferWidth => _framebufferWidth;
        public int FramebufferHeight => _framebufferHeight;

        public event Action? ViewportChanged;
        public void ClearTimedTexts()
        {
            _timedTexts.Clear();
        }
        private class TimedText
        {
            public Vector2 Pos;
            public string Text = "";
            public float FontSize;
            public Vector3 Color;
            public float Alpha;
            public float RemainingMs;
            public TimedText(Vector2 pos, string text, float fontSize, Vector3 color, float alpha, float ms)
            {
                Pos = pos; Text = text; FontSize = fontSize; Color = color; Alpha = alpha; RemainingMs = ms;
            }
        }

        private readonly List<TimedText> _timedTexts = new();

        public Vector2 CenterPosition(Vector2 size, float yOffset = 0f)
        {
            float x = (_windowWidth - size.X) * 0.5f;
            float y = (_windowHeight - size.Y) * 0.5f + yOffset;
            return new Vector2(x, y);
        }
        public void DrawText(Vector2 posLogical, string text, float fontSize = 16f, Vector3? color = null, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(text)) return;
            var col = color ?? Vector3.One;
            int texW, texH;
            int tex = GetOrCreateCachedTextTexture(text, fontSize, out texW, out texH);
            if (tex <= 0) return;
            float texLogicalW = texW / _scaleX;
            float texLogicalH = texH / _scaleY;
            DrawTexture(tex, posLogical, new Vector2(texLogicalW, texLogicalH), col, alpha);
        }
        public void DrawTextFor(Vector2 posLogical, string text, int milliseconds, float fontSize = 16f, Vector3? color = null, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(text) || milliseconds <= 0) return;
            var col = color ?? Vector3.One;
            _timedTexts.Add(new TimedText(posLogical, text, fontSize, col, alpha, milliseconds));
        }

        private bool _prevLeftDown = false;
        private Vector2 _lastMouseFb = Vector2.Zero;
        internal Vector2 LastMouseFramebuffer => _lastMouseFb;
        private bool _modalActive = false;
        private string _modalMessage = "";
        private Action<bool>? _modalCallback;
        private UIButton? _modalYesButton;
        private UIButton? _modalNoButton;
        private int _modalTextTex = -1;
        private int _modalTextW;
        private int _modalTextH;
        private bool _modalLayoutDirty = true;
        private readonly Dictionary<(string text, float size), (int tex, int w, int h)> _textCache = new();

        public VectUserInterfaceLib()
        {
            InitBuffers();
            _shader = new DSShader(EmbeddedShaders.Vertex, EmbeddedShaders.Fragment);
        }

        private void InitBuffers()
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * 12, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            _texVao = GL.GenVertexArray();
            _texVbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_texVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _texVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * 4 * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            uint[] indices = { 0, 1, 2, 2, 3, 0 };
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
            _overlayTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _overlayTex);

            byte[] overlayPx = new byte[] { 0, 0, 0, 128 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, overlayPx);

            _whiteTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _whiteTex);
            byte[] whitePx = new byte[] { 255, 255, 255, 255 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, whitePx);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            _cornerTex = CreateCircleTexture(64);
        }

        public int OverlayTexture => _overlayTex;

        public void SetViewport(int windowWidth, int windowHeight, int framebufferWidth, int framebufferHeight)
        {
            if (windowWidth <= 0) windowWidth = 1;
            if (windowHeight <= 0) windowHeight = 1;
            if (framebufferWidth <= 0) framebufferWidth = 1;
            if (framebufferHeight <= 0) framebufferHeight = 1;

            bool changed = windowWidth != _windowWidth || windowHeight != _windowHeight
                        || framebufferWidth != _framebufferWidth || framebufferHeight != _framebufferHeight;

            _windowWidth = windowWidth;
            _windowHeight = windowHeight;
            _framebufferWidth = framebufferWidth;
            _framebufferHeight = framebufferHeight;

            _scaleX = framebufferWidth / (float)windowWidth;
            _scaleY = framebufferHeight / (float)windowHeight;
            _scaleAvg = (_scaleX + _scaleY) * 0.5f;

            GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
            _modalLayoutDirty = true;

            if (changed)
            {
                ClearTextCache();

                foreach (var el in _elements) el.ResetTextCache();

                if (_modalTextTex != -1)
                {
                    GL.DeleteTexture(_modalTextTex);
                    _modalTextTex = -1;
                }

                ViewportChanged?.Invoke();
            }
        }

        internal Vector2 ToFramebuffer(Vector2 logical) => new Vector2(logical.X * _scaleX, logical.Y * _scaleY);
        internal Vector2 ToFramebufferSize(Vector2 logicalSize) => new Vector2(logicalSize.X * _scaleX, logicalSize.Y * _scaleY);

        private static bool PointInRect(Vector2 pFb, Vector2 posFb, Vector2 sizeFb)
        {
            return pFb.X >= posFb.X &&
                   pFb.X <= posFb.X + sizeFb.X &&
                   pFb.Y >= posFb.Y &&
                   pFb.Y <= posFb.Y + sizeFb.Y;
        }

        private int GetOrCreateCachedTextTexture(string text, float fontSizeLogical, out int texW, out int texH)
        {
            var key = (text, fontSizeLogical);
            if (_textCache.TryGetValue(key, out var entry))
            {
                texW = entry.w;
                texH = entry.h;
                return entry.tex;
            }

            int existing = -1;
            int tex = CreateTextTexture(text, fontSizeLogical, out texW, out texH, ref existing);
            if (tex > 0)
                _textCache[key] = (tex, texW, texH);
            return tex;
        }

        private void ClearTextCache()
        {
            foreach (var kv in _textCache.Values)
            {
                if (kv.tex > 0) GL.DeleteTexture(kv.tex);
            }
            _textCache.Clear();
        }

        public int CreateTextTexture(string text, float fontSizeLogical, out int texW, out int texH, ref int existingTexture)
        {
            float pixFontSize = Math.Max(1f, fontSizeLogical * _scaleAvg);

            int widthEstimate = Math.Max(4, (int)(text.Length * pixFontSize * 0.6f));
            int heightEstimate = Math.Max(4, (int)(pixFontSize * 1.5f));

            var info = new SKImageInfo(widthEstimate, heightEstimate, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var typeface = SKTypeface.FromFamilyName("Noto Sans");
            using var font = new SKFont(typeface, pixFontSize) { Edging = SKFontEdging.Antialias };

            float textWidth = font.MeasureText(text);
            int actualWidth = Math.Max(4, (int)Math.Ceiling(textWidth));
            int actualHeight = Math.Max(4, (int)Math.Ceiling(pixFontSize * 1.2f));

            var realInfo = new SKImageInfo(actualWidth, actualHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var realSurface = SKSurface.Create(realInfo);
            var realCanvas = realSurface.Canvas;
            realCanvas.Clear(SKColors.Transparent);

            var metrics = font.Metrics;
            float baseline = -metrics.Ascent;
            realCanvas.DrawText(text, 0, baseline, SKTextAlign.Left, font, paint);
            realCanvas.Flush();

            using var image = realSurface.Snapshot();
            using var pix = image.PeekPixels();
            var span = pix.GetPixelSpan();
            byte[] pixels = span.ToArray();

            if (existingTexture != -1)
            {
                GL.DeleteTexture(existingTexture);
                existingTexture = -1;
            }

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, pix.Width, pix.Height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            texW = pix.Width;
            texH = pix.Height;
            existingTexture = tex;
            return tex;
        }

        private int CreateCircleTexture(int size)
        {
            if (size <= 0) size = 64;
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            float r = size / 2f;
            canvas.DrawCircle(r, r, r, paint);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var pix = image.PeekPixels();
            var span = pix.GetPixelSpan();
            byte[] pixels = span.ToArray();

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, pix.Width, pix.Height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        public void AddElement(UIElement e)
        {
            _elements.Add(e);
            _elements.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
        }
        public void RemoveElement(UIElement e)
        {
            e.Dispose();
            _elements.Remove(e);
        }
        public void AddButton(UIButton b) => AddElement(b);
        public void AddTextBox(UITextBox tb) => AddElement(tb);
        public void AddSlider(UISlider s) => AddElement(s);

        public void ProcessMouse(Vector2 mousePosLogical, bool leftClick, bool rightClick)
        {
            Vector2 mouse = new Vector2(mousePosLogical.X * _scaleX, mousePosLogical.Y * _scaleY);
            _lastMouseFb = mouse;
            bool leftDown = leftClick;
            bool leftPressedThisFrame = leftDown && !_prevLeftDown;
            bool leftReleasedThisFrame = !leftDown && _prevLeftDown;
            _prevLeftDown = leftDown;

            if (_modalActive)
            {
                HandleModalInput(mouse, leftPressedThisFrame, leftReleasedThisFrame, rightClick);
                return;
            }

            var ordered = _elements.Where(e => e.Visible).OrderByDescending(e => e.ZIndex).ToList();

            bool anyTextBoxClicked = false;

            foreach (var e in ordered)
            {
                if (e.InputTransparent) { e.IsHovered = false; continue; }

                Vector2 posFb = ToFramebuffer(e.Position);
                Vector2 sizeFb = ToFramebufferSize(e.Size + e.HitPadding * 2f);
                Vector2 paddedPosFb = posFb - ToFramebuffer(e.HitPadding);
                bool hovered = PointInRect(mouse, paddedPosFb, sizeFb);
                e.IsHovered = hovered;

                if (hovered && leftPressedThisFrame)
                {
                    foreach (var other in _elements.OfType<UITextBox>()) other.IsFocused = false;

                    Vector2 localFb = new Vector2(mouse.X - paddedPosFb.X, mouse.Y - paddedPosFb.Y);
                    e.OnMouseDown(this, localFb, MouseButton.Left);

                    if (e is UITextBox) anyTextBoxClicked = true;

                    break;
                }

                if (hovered && rightClick)
                {
                    Vector2 localFb = new Vector2(mouse.X - paddedPosFb.X, mouse.Y - paddedPosFb.Y);
                    e.OnRightClick(this, localFb);
                    break;
                }
            }

            if (leftReleasedThisFrame)
            {
                foreach (var e in _elements) e.OnMouseUp(this, Vector2.Zero, MouseButton.Left);
            }

            if (leftPressedThisFrame && !anyTextBoxClicked)
            {
                foreach (var tb in _elements.OfType<UITextBox>()) tb.IsFocused = false;
            }
        }

        public void HandleKeyDown(KeyboardKeyEventArgs e)
        {
            if (e.Key == Keys.Tab)
            {
                bool reverse = false;
                try
                {
                    var modProp = e.GetType().GetProperty("Modifiers");
                    if (modProp != null)
                    {
                        var modVal = modProp.GetValue(e);
                        if (modVal != null)
                        {
                            reverse = modVal.ToString()!.Contains("Shift", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch
                {
                }

                CycleFocus(reverse);
                return;
            }
            foreach (var tb in _elements.OfType<UITextBox>())
            {
                if (!tb.IsFocused) continue;
                if (e.Key == Keys.Backspace)
                {
                    tb.Backspace();
                    return;
                }
                if (e.Key == Keys.Enter)
                {
                    tb.IsFocused = false;
                    return;
                }
                tb.OnKeyDown(this, e);
                return;
            }

            var focused = _elements.FirstOrDefault(x => x.IsFocused);
            focused?.OnKeyDown(this, e);
        }

        public void HandleTextInput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tb = _elements.OfType<UITextBox>().FirstOrDefault(x => x.IsFocused);
            tb?.InsertText(text);
        }

        private void CycleFocus(bool reverse)
        {
            var tabbables = _elements.Where(e => e.TabIndex >= 0 && e.Visible).OrderBy(e => e.TabIndex).ToList();
            if (tabbables.Count == 0) return;
            int current = tabbables.FindIndex(e => e.IsFocused);
            int next;
            if (current == -1)
                next = 0;
            else
            {
                next = reverse ? (current - 1 + tabbables.Count) % tabbables.Count : (current + 1) % tabbables.Count;
            }
            foreach (var e in tabbables) e.IsFocused = false;
            tabbables[next].IsFocused = true;
        }

        private void HandleModalInput(Vector2 mouse, bool leftPressed, bool leftReleased, bool rightClick)
        {
            UpdateModalLayoutIfNeeded();

            if (_modalYesButton != null)
            {
                var posFb = ToFramebuffer(_modalYesButton.Position);
                var sizeFb = ToFramebufferSize(_modalYesButton.Size);
                bool hovered = PointInRect(mouse, posFb, sizeFb);
                _modalYesButton.IsHovered = hovered;
                if (hovered && leftPressed)
                {
                    foreach (var tb in _elements.OfType<UITextBox>()) tb.IsFocused = false;
                    _modalYesButton.OnMouseDown(this, Vector2.Zero, MouseButton.Left);
                    return;
                }
            }

            if (_modalNoButton != null)
            {
                var posFb = ToFramebuffer(_modalNoButton.Position);
                var sizeFb = ToFramebufferSize(_modalNoButton.Size);
                bool hovered = PointInRect(mouse, posFb, sizeFb);
                _modalNoButton.IsHovered = hovered;
                if (hovered && leftPressed)
                {
                    foreach (var tb in _elements.OfType<UITextBox>()) tb.IsFocused = false;
                    _modalNoButton.OnMouseDown(this, Vector2.Zero, MouseButton.Left);
                    return;
                }
            }
        }

        public void ShowConfirmationDialog(string message, Action<bool> callback)
        {
            if (_modalTextTex != -1)
            {
                GL.DeleteTexture(_modalTextTex);
                _modalTextTex = -1;
            }

            _modalMessage = message ?? "";
            _modalCallback = callback;
            _modalActive = true;
            _modalLayoutDirty = true;

            CreateTextTexture(_modalMessage, 18f, out _modalTextW, out _modalTextH, ref _modalTextTex);

            _modalYesButton = new UIButton(new Vector2(0, 0), new Vector2(140, 40), "Yes") { ZIndex = 1000 };
            _modalNoButton = new UIButton(new Vector2(0, 0), new Vector2(140, 40), "No") { ZIndex = 1000 };

            _modalYesButton.OnMouseDownAction = (ui, local, b) =>
            {
                _modalActive = false;
                var cb = _modalCallback;
                _modalCallback = null;
                cb?.Invoke(true);
            };
            _modalNoButton.OnMouseDownAction = (ui, local, b) =>
            {
                _modalActive = false;
                var cb = _modalCallback;
                _modalCallback = null;
                cb?.Invoke(false);
            };
        }

        private void UpdateModalLayoutIfNeeded()
        {
            if (!_modalLayoutDirty) return;

            float dialogW = Math.Min(600f, _windowWidth * 0.8f);
            float dialogH = 160f;
            float cx = (_windowWidth - dialogW) * 0.5f;
            float cy = (_windowHeight - dialogH) * 0.5f;

            float btnW = 140f;
            float btnH = 40f;
            float spacing = 20f;
            float btnTotalW = btnW * 2 + spacing;
            float btnStartX = cx + (dialogW - btnTotalW) / 2f;
            float btnY = cy + dialogH - btnH - 20f;

            if (_modalYesButton != null)
            {
                _modalYesButton.Position = new Vector2(btnStartX, btnY);
                _modalYesButton.Size = new Vector2(btnW, btnH);
            }
            if (_modalNoButton != null)
            {
                _modalNoButton.Position = new Vector2(btnStartX + btnW + spacing, btnY);
                _modalNoButton.Size = new Vector2(btnW, btnH);
            }

            _modalLayoutDirty = false;
        }

        public void Render(int windowWidth, int windowHeight)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.DepthTest);

            GL.Disable(EnableCap.CullFace);
            GL.FrontFace(FrontFaceDirection.Ccw);

            _shader.Use();

            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
                0f, _framebufferWidth,
                _framebufferHeight, 0f,
                -1f, 1f
            );

            int projLoc = GL.GetUniformLocation(_shader.Handle, "uProjection");
            GL.UniformMatrix4(projLoc, false, ref projection);

            foreach (var e in _elements.Where(x => x.Visible).OrderBy(x => x.ZIndex))
                e.DrawBackground(this);

            foreach (var e in _elements.Where(x => x.Visible).OrderBy(x => x.ZIndex))
                e.DrawText(this);

            if (_modalActive)
            {
                DrawTexture(_overlayTex, new Vector2(0f, 0f), new Vector2(_windowWidth, _windowHeight));

                UpdateModalLayoutIfNeeded();

                float dialogW = Math.Min(600f, _windowWidth * 0.8f);
                float dialogH = 160f;
                float cx = (_windowWidth - dialogW) * 0.5f;
                float cy = (_windowHeight - dialogH) * 0.5f;

                DrawRoundedRect(new Vector2(cx, cy), new Vector2(dialogW, dialogH), DSTheme.UILighter, 8f);

                if (_modalTextTex > 0)
                {
                    float texLogicalW = _modalTextW / _scaleX;
                    float texLogicalH = _modalTextH / _scaleY;
                    float tx = cx + (dialogW - texLogicalW) / 2f;
                    float ty = cy + 20f;
                    DrawTexture(_modalTextTex, new Vector2(tx, ty), new Vector2(texLogicalW, texLogicalH));
                }

                _modalYesButton?.DrawBackground(this);
                _modalYesButton?.DrawText(this);
                _modalNoButton?.DrawBackground(this);
                _modalNoButton?.DrawText(this);
            }

            if (_timedTexts.Count > 0)
            {
                foreach (var t in _timedTexts)
                {
                    int texW, texH;
                    int tex = GetOrCreateCachedTextTexture(t.Text, t.FontSize, out texW, out texH);

                    if (tex > 0)
                    {
                        float texLogicalW = texW / _scaleX;
                        float texLogicalH = texH / _scaleY;

                        DrawTexture(tex, t.Pos, new Vector2(texLogicalW, texLogicalH), t.Color, t.Alpha);
                    }
                }
            }

            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        public void Update(float dt)
        {
            foreach (var e in _elements.Where(x => x.Visible))
                e.Update(this, dt);
            if (_modalActive)
            {
                _modalYesButton?.Update(this, dt);
                _modalNoButton?.Update(this, dt);
            }

            if (_timedTexts.Count > 0)
            {
                float deltaMs = dt * 1000f;
                for (int i = _timedTexts.Count - 1; i >= 0; i--)
                {
                    var t = _timedTexts[i];
                    t.RemainingMs -= deltaMs;
                    if (t.RemainingMs <= 0f)
                        _timedTexts.RemoveAt(i);
                }
            }
        }

        public void DrawRect(Vector2 posLogical, Vector2 sizeLogical, Vector3 color, float alpha = 1f)
        {
            Vector2 pos = ToFramebuffer(posLogical);
            Vector2 size = ToFramebufferSize(sizeLogical);

            float[] vertices =
            {
                pos.X, pos.Y,
                pos.X + size.X, pos.Y,
                pos.X + size.X, pos.Y + size.Y,
                pos.X + size.X, pos.Y + size.Y,
                pos.X, pos.Y + size.Y,
                pos.X, pos.Y
            };

            _shader.Use();
            int colorLoc = GL.GetUniformLocation(_shader.Handle, "uColor");
            GL.Uniform3(colorLoc, color);

            int useTexLoc = GL.GetUniformLocation(_shader.Handle, "uUseTexture");
            GL.Uniform1(useTexLoc, 0);

            int alphaLoc = GL.GetUniformLocation(_shader.Handle, "uAlpha");
            GL.Uniform1(alphaLoc, alpha);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, sizeof(float) * vertices.Length, vertices);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
        }

        public void DrawRoundedRect(
            Vector2 posLogical,
            Vector2 sizeLogical,
            Vector3 fillColor,
            float radiusLogical,
            float fillAlpha,
            float borderWidth,
            Vector3 borderColor,
            float borderAlpha = 1f)
        {
            DrawRoundedRectSimple(
                posLogical,
                sizeLogical,
                borderColor,
                radiusLogical,
                borderAlpha
            );

            var innerPos = posLogical + new Vector2(borderWidth, borderWidth);
            var innerSize = sizeLogical - new Vector2(borderWidth * 2f, borderWidth * 2f);

            if (innerSize.X > 0 && innerSize.Y > 0)
            {
                DrawRoundedRectSimple(
                    innerPos,
                    innerSize,
                    fillColor,
                    MathF.Max(0f, radiusLogical - borderWidth),
                    fillAlpha
                );
            }
        }


        public void DrawRoundedRect(
            Vector2 posLogical,
            Vector2 sizeLogical,
            Vector3 color,
            float radiusLogical,
            float alpha = 1f)
        {
            Vector2 pos = ToFramebuffer(posLogical);
            Vector2 size = ToFramebufferSize(sizeLogical);
            float radius = radiusLogical * _scaleAvg;

            _shader.Use();

            GL.Uniform3(GL.GetUniformLocation(_shader.Handle, "uColor"), color);
            GL.Uniform1(GL.GetUniformLocation(_shader.Handle, "uAlpha"), alpha);
            GL.Uniform1(GL.GetUniformLocation(_shader.Handle, "uUseTexture"), 0);

            GL.Uniform1(GL.GetUniformLocation(_shader.Handle, "uRounded"), 1);
            GL.Uniform2(GL.GetUniformLocation(_shader.Handle, "uSize"), size);
            GL.Uniform1(GL.GetUniformLocation(_shader.Handle, "uRadius"), radius);

            float[] vertices =
            {
                pos.X, pos.Y, 0f, 0f,
                pos.X + size.X, pos.Y, 1f, 0f,
                pos.X + size.X, pos.Y + size.Y, 1f, 1f,
                pos.X, pos.Y + size.Y, 0f, 1f
            };

            GL.BindVertexArray(_texVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _texVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);

            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

            GL.BindVertexArray(0);

            GL.Uniform1(GL.GetUniformLocation(_shader.Handle, "uRounded"), 0);
        }

        private void DrawRoundedRectBorder(Vector2 posLogical, Vector2 sizeLogical, float radiusLogical, float borderWidth, Vector3 color, float alpha)
        {
            DrawRoundedRectSimple(posLogical, sizeLogical, color, radiusLogical, alpha);
            var innerPos = posLogical + new Vector2(borderWidth, borderWidth);
            var innerSize = sizeLogical - new Vector2(borderWidth * 2f, borderWidth * 2f);
            var innerColor = DSTheme.UIDarker;
            DrawRoundedRectSimple(innerPos, innerSize, innerColor, MathF.Max(0f, radiusLogical - borderWidth), alpha);
        }

        private void DrawRoundedRectSimple(Vector2 posLogical, Vector2 sizeLogical, Vector3 color, float radiusLogical, float alpha)
        {
            float r = MathF.Max(0f, MathF.Min(radiusLogical, MathF.Min(sizeLogical.X, sizeLogical.Y) / 2f));
            var centerPos = posLogical + new Vector2(r, 0f);
            var centerSize = new Vector2(sizeLogical.X - 2f * r, sizeLogical.Y);
            if (centerSize.X > 0 && centerSize.Y > 0) DrawRect(centerPos, centerSize, color, alpha);
            var leftStripPos = posLogical + new Vector2(0f, r);
            var leftStripSize = new Vector2(r, sizeLogical.Y - 2f * r);
            if (leftStripSize.X > 0 && leftStripSize.Y > 0) DrawRect(leftStripPos, leftStripSize, color, alpha);
            var rightStripPos = posLogical + new Vector2(sizeLogical.X - r, r);
            var rightStripSize = new Vector2(r, sizeLogical.Y - 2f * r);
            if (rightStripSize.X > 0 && rightStripSize.Y > 0) DrawRect(rightStripPos, rightStripSize, color, alpha);
            if (_cornerTex > 0 && r > 0f)
            {
                float cornerSz = r * 2f;
                DrawTexture(_cornerTex, posLogical + new Vector2(0f, 0f), new Vector2(cornerSz, cornerSz), color, alpha);
                DrawTexture(_cornerTex, posLogical + new Vector2(sizeLogical.X - cornerSz, 0f), new Vector2(cornerSz, cornerSz), color, alpha);
                DrawTexture(_cornerTex, posLogical + new Vector2(sizeLogical.X - cornerSz, sizeLogical.Y - cornerSz), new Vector2(cornerSz, cornerSz), color, alpha);
                DrawTexture(_cornerTex, posLogical + new Vector2(0f, sizeLogical.Y - cornerSz), new Vector2(cornerSz, cornerSz), color, alpha);
            }
        }

        public void DrawTexture(int texture, Vector2 posLogical, Vector2 sizeLogical, Vector3? color = null, float alpha = 1f)
        {
            if (texture <= 0) return;

            Vector2 pos = ToFramebuffer(posLogical);
            Vector2 size = ToFramebufferSize(sizeLogical);

            _shader.Use();

            Vector3 col = color ?? Vector3.One;
            int colorLoc = GL.GetUniformLocation(_shader.Handle, "uColor");
            GL.Uniform3(colorLoc, col);

            int useTexLoc = GL.GetUniformLocation(_shader.Handle, "uUseTexture");
            GL.Uniform1(useTexLoc, 1);

            int alphaLoc = GL.GetUniformLocation(_shader.Handle, "uAlpha");
            GL.Uniform1(alphaLoc, alpha);

            int texLoc = GL.GetUniformLocation(_shader.Handle, "uTexture");
            GL.Uniform1(texLoc, 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);

            float[] vertices = new float[]
            {
                pos.X, pos.Y, 0f, 0f,
                pos.X + size.X, pos.Y, 1f, 0f,
                pos.X + size.X, pos.Y + size.Y, 1f, 1f,
                pos.X, pos.Y + size.Y, 0f, 1f
            };

            GL.BindVertexArray(_texVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _texVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);

            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

            GL.BindVertexArray(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Dispose()
        {
            ClearTextCache();

            foreach (var e in _elements) e.Dispose();

            if (_modalTextTex != -1)
            {
                GL.DeleteTexture(_modalTextTex);
                _modalTextTex = -1;
            }

            if (_overlayTex != -1)
            {
                GL.DeleteTexture(_overlayTex);
                _overlayTex = -1;
            }

            if (_whiteTex != -1)
            {
                GL.DeleteTexture(_whiteTex);
                _whiteTex = -1;
            }

            if (_cornerTex != -1)
            {
                GL.DeleteTexture(_cornerTex);
                _cornerTex = -1;
            }

            _shader.Dispose();
        }

        public float MeasureTextWidth(string text, float fontSizeLogical)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float pixFontSize = Math.Max(1f, fontSizeLogical * _scaleAvg);
            using var typeface = SKTypeface.FromFamilyName("Noto Sans");
            using var font = new SKFont(typeface, pixFontSize) { Edging = SKFontEdging.Antialias };
            float px = font.MeasureText(text);
            return px / _scaleX;
        }

        public class UIButton : UIElement
        {
            public string Text;
            public float FontSize;
            public Action<VectUserInterfaceLib, Vector2, MouseButton>? OnMouseDownAction;
            public Action? OnClick;
            public Action? OnRightClickAction;
            private int _tex = -1;
            private int _texW;
            private int _texH;
            private string _lastText = "";
            private float _lastFontSize = -1f;

            public float CornerRadius = 6f;
            public Vector2 Padding = new Vector2(8f, 6f);

            public UIButton(Vector2 pos, Vector2 size, string text, float fontSize = 16f)
            {
                Position = pos;
                Size = size;
                Text = text;
                FontSize = fontSize;
                HitPadding = new Vector2(4f, 4f);
                TabIndex = -1;
            }

            public override void DrawBackground(VectUserInterfaceLib ui)
            {
                var col = IsHovered ? DSTheme.UILighter : DSTheme.UIBase;
                if (IsFocused && IsHovered) col = DSTheme.UIDarker;

                ui.DrawRoundedRect(Position, Size, col, CornerRadius, 1f, 1f, DSTheme.UIBorder);
            }

            public override void DrawText(VectUserInterfaceLib ui)
            {
                if (string.IsNullOrEmpty(Text)) return;

                if (_lastText != Text || Math.Abs(_lastFontSize - FontSize) > 0.001f)
                {
                    ui.CreateTextTexture(Text, FontSize, out _texW, out _texH, ref _tex);
                    _lastText = Text;
                    _lastFontSize = FontSize;
                }

                if (_tex <= 0) return;

                float texLogicalW = _texW / (ui._scaleX);
                float texLogicalH = _texH / (ui._scaleY);

                float maxW = Math.Max(0f, Size.X - Padding.X * 2f);
                float drawW = Math.Min(maxW, texLogicalW);
                float x = Position.X + (Size.X - drawW) / 2f;
                float y = Position.Y + (Size.Y - texLogicalH) / 2f;

                ui.DrawTexture(_tex, new Vector2(x, y), new Vector2(drawW, texLogicalH));
            }

            public override void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                OnMouseDownAction?.Invoke(ui, localPos, button);
                if (button == MouseButton.Left)
                {
                    OnClick?.Invoke();
                }
                else if (button == MouseButton.Right)
                {
                    OnRightClickAction?.Invoke();
                }
            }

            public override void Dispose()
            {
                if (_tex != -1) { GL.DeleteTexture(_tex); _tex = -1; }
            }

            public override void ResetTextCache()
            {
                _lastText = "";
                _lastFontSize = -1f;
                if (_tex != -1) { GL.DeleteTexture(_tex); _tex = -1; }
            }
        }

        public class UISlider : UIElement
        {
            public float GrabOffsetX;
            public float Value;
            public bool IsDragging = false;

            public Action<float>? OnValueChanged;

            public UISlider(Vector2 pos, Vector2 size)
            {
                Position = pos;
                Size = size;
            }

            public override void DrawBackground(VectUserInterfaceLib ui)
            {
                ui.DrawRoundedRect(Position, Size, DSTheme.UIBase, MathF.Min(Size.Y / 2f, 6f));
                ui.DrawRoundedRect(Position, new Vector2(Size.X * Value, Size.Y), DSTheme.UIHighlight, MathF.Min(Size.Y / 2f, 6f));
            }

            public override void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                if (button != MouseButton.Left) return;

                float sizeFbX = ui.ToFramebufferSize(Size).X;
                float thumbX = Value * sizeFbX;
                GrabOffsetX = localPos.X - thumbX;
                IsDragging = true;

                UpdateValueFromLocal(localPos.X, sizeFbX);
            }

            public override void OnMouseUp(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                if (button != MouseButton.Left) return;
                if (IsDragging)
                {
                    IsDragging = false;
                    OnValueChanged?.Invoke(Value);
                }
            }

            public override void Update(VectUserInterfaceLib ui, float dt)
            {
                if (!IsDragging) return;

                Vector2 mouseFb = ui.LastMouseFramebuffer;

                Vector2 posFb = ui.ToFramebuffer(Position);
                float localX = mouseFb.X - posFb.X;

                float sizeFbX = ui.ToFramebufferSize(Size).X;
                UpdateValueFromLocal(localX, sizeFbX);
            }

            private void UpdateValueFromLocal(float localX, float sizeFbX)
            {
                float v = (localX - GrabOffsetX) / sizeFbX;
                Value = Math.Clamp(v, 0f, 1f);
            }
        }

        public class UITextBox : UIElement
        {
            public string Text = "";
            private int _tex = -1;
            private int _texW;
            private int _texH;
            private string _lastText = "";
            public int CaretIndex = 0;
            public int SelectionStart = 0;
            public int SelectionEnd = 0;
            private int _selectionAnchor = 0;
            private bool _isDragging = false;
            private int _lastClickTime = 0;
            private float _caretTimer = 0f;
            private bool _caretVisible = true;
            private float _caretBlinkInterval = 0.5f;
            private float _scrollX = 0f;
            public float LeftPadding = 6f;
            public float FontSize = 16f;
            public UITextBox(Vector2 pos, Vector2 size)
            {
                Position = pos;
                Size = size;
                Text = "";
                IsFocused = false;
                IsHovered = false;
                TabIndex = -1;
                HitPadding = new Vector2(2f, 2f);
            }

            public override void DrawBackground(VectUserInterfaceLib ui)
            {
                var bg = IsFocused ? DSTheme.UILighter : DSTheme.UIBase;
                ui.DrawRoundedRect(Position, Size, bg, 6f, 1f, 1f, DSTheme.UIBorder);
            }

            public override void ResetTextCache()
            {
                _lastText = "";
                if (_tex != -1) { GL.DeleteTexture(_tex); _tex = -1; }
            }

            private int GetSelectionStartNormalized()
            {
                return Math.Min(SelectionStart, SelectionEnd);
            }

            private int GetSelectionEndNormalized()
            {
                return Math.Max(SelectionStart, SelectionEnd);
            }

            private int SelectionLength()
            {
                return GetSelectionEndNormalized() - GetSelectionStartNormalized();
            }

            public override void DrawText(VectUserInterfaceLib ui)
            {
                if (Text == null) Text = "";

                string visibleText = Text;
                int totalChars = visibleText.Length;

                if (_lastText != visibleText + "|" + CaretIndex + "|" + SelectionStart + "|" + SelectionEnd)
                {
                    ui.CreateTextTexture(visibleText, FontSize, out _texW, out _texH, ref _tex);
                    _lastText = visibleText + "|" + CaretIndex + "|" + SelectionStart + "|" + SelectionEnd;
                }

                if (_tex <= 0) return;

                float texLogicalW = _texW / (ui._scaleX);
                float texLogicalH = _texH / (ui._scaleY);

                float x = Position.X + LeftPadding;
                float y = Position.Y + (Size.Y - texLogicalH) / 2f;

                float innerWidth = Math.Max(0f, Size.X - LeftPadding * 2f);

                float textWidth = ui.MeasureTextWidth(visibleText, FontSize);

                _scrollX = Math.Max(0f, Math.Min(_scrollX, Math.Max(0f, textWidth - innerWidth)));

                float drawX = x - _scrollX;

                int selStart = GetSelectionStartNormalized();
                int selEnd = GetSelectionEndNormalized();
                if (selEnd > selStart)
                {
                    string beforeSel = visibleText.Substring(0, selStart);
                    string selText = visibleText.Substring(selStart, selEnd - selStart);
                    float beforeWidth = ui.MeasureTextWidth(beforeSel, FontSize);
                    float selWidth = ui.MeasureTextWidth(selText, FontSize);
                    Vector2 selPos = new Vector2(drawX + beforeWidth, y);
                    Vector2 selSize = new Vector2(selWidth, texLogicalH);
                    ui.DrawRect(selPos, selSize, DSTheme.UIHighlight, 0.3f);
                }

                int texToDraw = _tex;
                ui.DrawTexture(texToDraw, new Vector2(drawX, y), new Vector2(texLogicalW, texLogicalH), DSTheme.UIText, 1f);

                if (IsFocused)
                {
                    _caretTimer += 1f / 60f;
                    if (_caretTimer >= _caretBlinkInterval)
                    {
                        _caretVisible = !_caretVisible;
                        _caretTimer = 0f;
                    }
                }
                else
                {
                    _caretVisible = false;
                }

                if (IsFocused && _caretVisible)
                {
                    string beforeCaret = visibleText.Substring(0, Math.Clamp(CaretIndex, 0, visibleText.Length));
                    float caretPx = ui.MeasureTextWidth(beforeCaret, FontSize);
                    float caretX = drawX + caretPx;
                    Vector2 caretPos = new Vector2(caretX, y);
                    Vector2 caretSize = new Vector2(1f, texLogicalH);
                    ui.DrawRect(caretPos, caretSize, DSTheme.UIText, 1f);
                }
            }

            public override void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                if (button != MouseButton.Left) return;
                IsFocused = true;
                int now = Environment.TickCount;
                bool isDouble = (now - _lastClickTime) < 400;
                _lastClickTime = now;
                float clickX = localPos.X;
                float clickLogicalX = clickX / ui._scaleX;
                float x = Position.X + LeftPadding;
                float innerWidth = Math.Max(0f, Size.X - LeftPadding * 2f);
                float textX = clickLogicalX + _scrollX - LeftPadding;
                int idx = GetIndexFromLocalX(ui, textX);
                SetCaretAndSelectionFromClick(idx, isDouble);
                _isDragging = true;
                _selectionAnchor = CaretIndex;
            }

            public override void OnMouseUp(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                if (button != MouseButton.Left) return;
                _isDragging = false;
            }

            public override void Update(VectUserInterfaceLib ui, float dt)
            {
                if (_isDragging)
                {
                    Vector2 mouseFb = ui.LastMouseFramebuffer;
                    Vector2 posFb = ui.ToFramebuffer(Position);
                    float localX = (mouseFb.X - posFb.X) / ui._scaleX;
                    float textX = localX + _scrollX - LeftPadding;
                    int idx = GetIndexFromLocalX(ui, textX);
                    CaretIndex = Math.Clamp(idx, 0, Text.Length);
                    SelectionStart = _selectionAnchor;
                    SelectionEnd = CaretIndex;
                    EnsureCaretVisible(ui);
                    _lastText = "";
                }
            }

            private int GetIndexFromLocalX(VectUserInterfaceLib ui, float localX)
            {
                if (string.IsNullOrEmpty(Text)) return 0;
                int n = Text.Length;
                int best = 0;
                float acc = 0f;
                for (int i = 0; i <= n; i++)
                {
                    string s = Text.Substring(0, i);
                    float w = ui.MeasureTextWidth(s, FontSize);
                    if (Math.Abs(w - localX) < Math.Abs(acc - localX) || i == 0)
                    {
                        best = i;
                        acc = w;
                    }
                }
                for (int i = 0; i <= n; i++)
                {
                    string s = Text.Substring(0, i);
                    float w = ui.MeasureTextWidth(s, FontSize);
                    if (w >= localX)
                    {
                        return i;
                    }
                }
                return Text.Length;
            }

            private void SetCaretAndSelectionFromClick(int idx, bool doubleClick)
            {
                if (doubleClick)
                {
                    int s = idx;
                    if (s < 0) s = 0;
                    if (s > Text.Length) s = Text.Length;
                    int wstart = FindWordStart(s);
                    int wend = FindWordEnd(s);
                    SelectionStart = wstart;
                    SelectionEnd = wend;
                    CaretIndex = wend;
                }
                else
                {
                    CaretIndex = Math.Clamp(idx, 0, Text.Length);
                    SelectionStart = CaretIndex;
                    SelectionEnd = CaretIndex;
                }
                EnsureCaretVisible(null);
                _lastText = "";
            }

            public override void OnKeyDown(VectUserInterfaceLib ui, KeyboardKeyEventArgs e)
            {
                bool shift = (e.Modifiers & KeyModifiers.Shift) != 0;
                bool ctrl = (e.Modifiers & KeyModifiers.Control) != 0;
                if (ctrl && e.Key == Keys.A)
                {
                    SelectAll();
                    return;
                }
                if (ctrl && e.Key == Keys.C)
                {
                    Copy();
                    return;
                }
                if (ctrl && e.Key == Keys.X)
                {
                    Cut();
                    return;
                }
                if (ctrl && e.Key == Keys.V)
                {
                    Paste();
                    return;
                }
                if (e.Key == Keys.Left)
                {
                    if (ctrl)
                    {
                        int target = FindWordStart(CaretIndex);
                        MoveCaretTo(target, shift);
                    }
                    else
                    {
                        MoveCaretBy(-1, shift);
                    }
                    return;
                }
                if (e.Key == Keys.Right)
                {
                    if (ctrl)
                    {
                        int target = FindWordEnd(CaretIndex);
                        MoveCaretTo(target, shift);
                    }
                    else
                    {
                        MoveCaretBy(1, shift);
                    }
                    return;
                }
                if (e.Key == Keys.Home)
                {
                    MoveCaretTo(0, shift);
                    return;
                }
                if (e.Key == Keys.End)
                {
                    MoveCaretTo(Text.Length, shift);
                    return;
                }
                if (e.Key == Keys.Delete)
                {
                    if (HasSelection())
                    {
                        DeleteSelection();
                    }
                    else
                    {
                        if (CaretIndex < Text.Length)
                        {
                            Text = Text.Remove(CaretIndex, 1);
                            _lastText = "";
                        }
                    }
                    return;
                }
            }

            private bool HasSelection()
            {
                return GetSelectionEndNormalized() > GetSelectionStartNormalized();
            }

            public void InsertText(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                if (HasSelection())
                {
                    int ss = GetSelectionStartNormalized();
                    int len = SelectionLength();
                    Text = Text.Remove(ss, len).Insert(ss, s);
                    CaretIndex = ss + s.Length;
                    SelectionStart = CaretIndex;
                    SelectionEnd = CaretIndex;
                }
                else
                {
                    Text = Text.Insert(CaretIndex, s);
                    CaretIndex += s.Length;
                }
                EnsureCaretVisible(null);
                _lastText = "";
            }

            public void Backspace()
            {
                if (HasSelection())
                {
                    DeleteSelection();
                    return;
                }
                if (CaretIndex <= 0) return;
                Text = Text.Remove(CaretIndex - 1, 1);
                CaretIndex = Math.Max(0, CaretIndex - 1);
                EnsureCaretVisible(null);
                _lastText = "";
            }

            private void DeleteSelection()
            {
                int ss = GetSelectionStartNormalized();
                int len = SelectionLength();
                if (len <= 0) return;
                Text = Text.Remove(ss, len);
                CaretIndex = ss;
                SelectionStart = CaretIndex;
                SelectionEnd = CaretIndex;
                EnsureCaretVisible(null);
                _lastText = "";
            }

            private void MoveCaretBy(int delta, bool shift)
            {
                int target = Math.Clamp(CaretIndex + delta, 0, Text.Length);
                MoveCaretTo(target, shift);
            }

            private void MoveCaretTo(int index, bool shift)
            {
                index = Math.Clamp(index, 0, Text.Length);
                if (shift)
                {
                    if (!HasSelection())
                    {
                        _selectionAnchor = CaretIndex;
                    }
                    CaretIndex = index;
                    SelectionStart = _selectionAnchor;
                    SelectionEnd = CaretIndex;
                }
                else
                {
                    CaretIndex = index;
                    SelectionStart = CaretIndex;
                    SelectionEnd = CaretIndex;
                }
                EnsureCaretVisible(null);
                _lastText = "";
            }

            private int FindWordStart(int idx)
            {
                if (string.IsNullOrEmpty(Text)) return 0;
                idx = Math.Clamp(idx, 0, Text.Length);
                if (idx > 0 && idx == Text.Length) idx--;
                while (idx > 0 && char.IsWhiteSpace(Text[idx - 1])) idx--;
                while (idx > 0 && !char.IsWhiteSpace(Text[idx - 1])) idx--;
                return idx;
            }

            private int FindWordEnd(int idx)
            {
                if (string.IsNullOrEmpty(Text)) return 0;
                idx = Math.Clamp(idx, 0, Text.Length);
                while (idx < Text.Length && char.IsWhiteSpace(Text[idx])) idx++;
                while (idx < Text.Length && !char.IsWhiteSpace(Text[idx])) idx++;
                return idx;
            }

            private static string _clipboard = "";

            private void Copy()
            {
                if (!HasSelection()) return;
                int ss = GetSelectionStartNormalized();
                int len = SelectionLength();
                string s = Text.Substring(ss, len);
                _clipboard = s;
            }

            private void Cut()
            {
                if (!HasSelection()) return;
                int ss = GetSelectionStartNormalized();
                int len = SelectionLength();
                string s = Text.Substring(ss, len);
                _clipboard = s;
                Text = Text.Remove(ss, len);
                CaretIndex = ss;
                SelectionStart = ss;
                SelectionEnd = ss;
                EnsureCaretVisible(null);
                _lastText = "";
            }

            private void Paste()
            {
                if (string.IsNullOrEmpty(_clipboard)) return;
                InsertText(_clipboard);
            }

            public void SelectAll()
            {
                SelectionStart = 0;
                SelectionEnd = Text.Length;
                CaretIndex = SelectionEnd;
                _lastText = "";
                EnsureCaretVisible(null);
            }

            private void EnsureCaretVisible(VectUserInterfaceLib? uiMaybe)
            {
                VectUserInterfaceLib ui = uiMaybe ?? GetUi();
                if (ui == null) return;
                string beforeCaret = Text.Substring(0, Math.Clamp(CaretIndex, 0, Text.Length));
                float caretPx = ui.MeasureTextWidth(beforeCaret, FontSize);
                float innerWidth = Math.Max(0f, Size.X - LeftPadding * 2f);
                float minScroll = caretPx - innerWidth + 6f;
                if (_scrollX < minScroll) _scrollX = minScroll;
                if (_scrollX > caretPx - 6f) _scrollX = caretPx - 6f;
                _scrollX = Math.Max(0f, _scrollX);
                _lastText = "";
            }

            private VectUserInterfaceLib? GetUi()
            {
                return null;
            }

            public override void Dispose()
            {
                if (_tex != -1) { GL.DeleteTexture(_tex); _tex = -1; }
            }
        }

        public class UICheckbox : UIElement
        {
            public bool Checked;
            public Action<bool>? OnToggle;

            public UICheckbox(Vector2 pos, Vector2 size, bool initial = false)
            {
                Position = pos;
                Size = size;
                Checked = initial;
                HitPadding = new Vector2(2f, 2f);
            }

            public override void DrawBackground(VectUserInterfaceLib ui)
            {
                ui.DrawRoundedRect(Position, Size, DSTheme.UIBase, 4f);
                if (Checked)
                {
                    var inset = new Vector2(6f, 6f);
                    ui.DrawRoundedRect(Position + inset, Size - inset * 2f, DSTheme.UIHighlight, 3f);
                }
            }

            public override void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                if (button != MouseButton.Left) return;
                Checked = !Checked;
                OnToggle?.Invoke(Checked);
            }
        }

        public class UIScrollContainer : UIElement
        {
            public Vector2 ContentSize;
            public Vector2 ScrollOffset = Vector2.Zero;
            public List<UIElement> Children = new();

            public UIScrollContainer(Vector2 pos, Vector2 size, Vector2 contentSize)
            {
                Position = pos;
                Size = size;
                ContentSize = contentSize;
                HitPadding = Vector2.Zero;
            }

            public override void DrawBackground(VectUserInterfaceLib ui)
            {
                ui.DrawRoundedRect(Position, Size, DSTheme.UIDarker, 6f);
                foreach (var c in Children)
                {
                    var origPos = c.Position;
                    c.Position = origPos - ScrollOffset + Position;
                    c.DrawBackground(ui);
                    c.DrawText(ui);
                    c.Position = origPos;
                }
            }

            public override void OnMouseDown(VectUserInterfaceLib ui, Vector2 localPos, MouseButton button)
            {
                Vector2 mouseFb = localPos + ui.ToFramebuffer(Position);
                foreach (var c in Children.OrderByDescending(x => x.ZIndex))
                {
                    var childGlobalPos = Position + (c.Position - ScrollOffset);
                    var childFbPos = ui.ToFramebuffer(childGlobalPos);
                    var childFbSize = ui.ToFramebufferSize(c.Size);
                    if (PointInRect(mouseFb, childFbPos, childFbSize))
                    {
                        Vector2 childLocalFb = new Vector2(mouseFb.X - childFbPos.X, mouseFb.Y - childFbPos.Y);
                        c.OnMouseDown(ui, childLocalFb, button);
                        break;
                    }
                }
            }

            public void AddChild(UIElement e)
            {
                Children.Add(e);
            }
        }
    }
}
