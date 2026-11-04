#:package System.Drawing.Common@10.0.0
// Tạo icon QuadPane theo thiết kế trong Assets/logo.svg:
//   Assets/QuadPane.ico (16…256 px), Assets/logo-256.png, tools/icon-preview.png
// Mỗi kích thước được dựng lại với toạ độ làm tròn theo pixel để icon nhỏ (16/24 px) vẫn sắc nét.
// Chạy từ thư mục QuadPane:  dotnet run tools/make-icon.cs
#pragma warning disable CA1416 // chỉ chạy trên Windows
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
string assets = Path.Combine(root, "Assets");
Directory.CreateDirectory(assets);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
WriteIco(Path.Combine(assets, "QuadPane.ico"), sizes);
using (var b = Render(256)) b.Save(Path.Combine(assets, "logo-256.png"), ImageFormat.Png);

// Ảnh xem trước các kích thước trên nền tối và nền sáng
using (var prev = new Bitmap(640, 560))
using (var g = Graphics.FromImage(prev))
{
    g.Clear(Color.FromArgb(32, 32, 32));
    g.FillRectangle(Brushes.White, 0, 280, 640, 280);
    foreach (int y0 in new[] { 10, 290 })
    {
        int x = 10;
        foreach (int s in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            using var i = Render(s);
            g.DrawImageUnscaled(i, x, y0);
            x += s + 12;
        }
    }
    prev.Save(Path.Combine(root, "tools", "icon-preview.png"), ImageFormat.Png);
}
Console.WriteLine($"OK: {Path.Combine(assets, "QuadPane.ico")}");

static GraphicsPath Rounded(float x, float y, float w, float h, float r)
{
    var p = new GraphicsPath();
    float d = Math.Min(r * 2, Math.Min(w, h));
    if (d <= 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
    p.AddArc(x, y, d, d, 180, 90);
    p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
    p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}

static Bitmap Render(int s)
{
    // Render ở độ phân giải gấp bốn lần rồi thu nhỏ để nét chéo của chữ Q rõ ở 16 px.
    const int scale = 4;
    int n = s * scale;
    using var large = new Bitmap(n, n, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(large))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(n / 256f, n / 256f);

        using var background = Rounded(0, 0, 256, 256, 54);
        using var backgroundBrush = new LinearGradientBrush(new PointF(0, 0), new PointF(256, 256),
            Color.FromArgb(0x17, 0x3E, 0x62), Color.FromArgb(0x0A, 0x20, 0x39));
        g.FillPath(backgroundBrush, background);

        using var frame = Rounded(43.5f, 43.5f, 169, 169, 31.5f);
        using var framePen = new Pen(Color.FromArgb(0xD8, 0xF2, 0xFF), 13) { LineJoin = LineJoin.Round };
        g.DrawPath(framePen, frame);
        using var dividerPen = new Pen(Color.FromArgb(0xD8, 0xF2, 0xFF), 12);
        g.DrawLine(dividerPen, 128, 44, 128, 212);
        g.DrawLine(dividerPen, 44, 128, 212, 128);

        using var active = Rounded(52, 52, 68, 68, 10);
        using var activeBrush = new LinearGradientBrush(new PointF(52, 52), new PointF(120, 120),
            Color.FromArgb(0x74, 0xE5, 0xFF), Color.FromArgb(0x28, 0xB9, 0xED));
        g.FillPath(activeBrush, active);
        using var tailPen = new Pen(Color.FromArgb(0x41, 0xD1, 0xF5), 22)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(tailPen, 173, 173, 226, 226);
    }

    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using var outputGraphics = Graphics.FromImage(bmp);
    outputGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    outputGraphics.DrawImage(large, 0, 0, s, s);
    return bmp;
}

// File .ico nhiều kích thước, mỗi ảnh nén PNG (Windows Vista trở lên hỗ trợ)
static void WriteIco(string path, int[] sizes)
{
    var images = sizes.Select(s =>
    {
        using var bmp = Render(s);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }).ToArray();

    using var w = new BinaryWriter(File.Create(path));
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        int s = sizes[i];
        w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(images[i].Length); w.Write(offset);
        offset += images[i].Length;
    }
    foreach (var img in images) w.Write(img);
}
