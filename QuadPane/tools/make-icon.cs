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
    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    // Nền: bo góc 22%, gradient chéo xanh -> tím, thêm vệt sáng nhẹ ở nửa trên
    using (var bg = Rounded(0, 0, s, s, s * 0.22f))
    {
        using (var lg = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s),
                   Color.FromArgb(0x3B, 0x9B, 0xFF), Color.FromArgb(0x5B, 0x4B, 0xE8)))
            g.FillPath(lg, bg);
        // Gradient phủ hết chiều cao (GDI+ lặp lại gradient ngoài vùng của nó, gây vệt cắt ngang)
        using (var shine = new LinearGradientBrush(new PointF(0, -1), new PointF(0, s + 1),
                   Color.White, Color.White))
        {
            shine.InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)],
                Positions = [0f, 0.55f, 1f],
            };
            g.FillPath(shine, bg);
        }
    }

    // Lưới 2x2 làm tròn theo pixel nguyên, căn giữa lại sau khi làm tròn
    int gap = Math.Max(1, (int)Math.Round(s * 16.0 / 256));
    int pad = (int)Math.Round(s * 52.0 / 256);
    int tile = (s - 2 * pad - gap) / 2;
    pad = (s - 2 * tile - gap) / 2;
    float tr = s <= 16 ? 1f : Math.Max(1f, tile * 14f / 68f);

    int[] pos = [pad, pad + tile + gap];
    for (int row = 0; row < 2; row++)
        for (int col = 0; col < 2; col++)
        {
            int alpha = row == 0 && col == 0 ? 255 : 115; // ô trên-trái = panel đang hoạt động
            using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
            using var path = Rounded(pos[col], pos[row], tile, tile, tr);
            g.FillPath(brush, path);
        }
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
