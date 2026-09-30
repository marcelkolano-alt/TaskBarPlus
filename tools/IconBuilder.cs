using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// The geometry in assets/logo.svg, optically fitted to each Windows pixel grid.
internal static class IconBuilder
{
    static void Rounded(Graphics g, Brush brush, float x, float y, float w, float h, float radius)
    {
        using (var p = new GraphicsPath())
        {
            float d = radius * 2;
            p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure(); g.FillPath(brush, p);
        }
    }
    static byte[] Render(int size)
    {
        float scale = size / 256f;
        Func<float, float> snap = value => (float)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        float tile = Math.Max(3, snap(48));
        float gap = Math.Max(1, snap(12));
        float tileX = (float)Math.Round(118 * scale - tile - gap / 2, MidpointRounding.AwayFromZero);
        float tileY = snap(94);
        float tileRadius = Math.Max(0.5f, (float)Math.Round(10 * scale * 2, MidpointRounding.AwayFromZero) / 2);
        // An even-width bar stays precisely centered with integer pixel edges.
        float barWidth = snap(84) * 2;
        float barHeight = Math.Max(1, snap(16));
        float plusWidth = Math.Max(1, snap(10));
        float plusHalf = Math.Max(1, snap(16));
        float centerBias = ((int)plusWidth % 2 == 0) ? 0 : 0.5f;
        float plusX = (float)Math.Floor(188 * scale) + centerBias;
        float plusY = (float)Math.Floor(72 * scale) + centerBias;
        using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(large))
        using (var bg = new SolidBrush(Color.FromArgb(25, 30, 39)))
        using (var mint = new SolidBrush(Color.FromArgb(109, 220, 176)))
        using (var plus = new Pen(Color.FromArgb(236, 240, 246), plusWidth))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(4, 4);
            Rounded(g, bg, 8 * scale, 8 * scale, 240 * scale, 240 * scale, 52 * scale);
            Rounded(g, mint, tileX, tileY, tile, tile, tileRadius);
            Rounded(g, mint, tileX + tile + gap, tileY, tile, tile, tileRadius);
            Rounded(g, mint, (size - barWidth) / 2, snap(158), barWidth, barHeight, barHeight / 2);
            plus.StartCap = LineCap.Round; plus.EndCap = LineCap.Round;
            g.DrawLine(plus, plusX, plusY - plusHalf, plusX, plusY + plusHalf);
            g.DrawLine(plus, plusX - plusHalf, plusY, plusX + plusHalf, plusY);
            using (var small = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var target = Graphics.FromImage(small))
            using (var stream = new MemoryStream())
            {
                target.CompositingMode = CompositingMode.SourceCopy;
                target.InterpolationMode = InterpolationMode.HighQualityBicubic;
                target.PixelOffsetMode = PixelOffsetMode.HighQuality;
                target.DrawImage(large, new Rectangle(0, 0, size, size));
                small.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int size in sizes) images.Add(Render(size));
        using (var file = File.Create(Path.Combine(args[0], "TaskBarPlus.ico")))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            uint offset = (uint)(6 + 16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)images[i].Length); writer.Write(offset); offset += (uint)images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
        File.WriteAllBytes(Path.Combine(args[0], "logo.png"), Render(256));
        if (args.Length > 1)
        {
            Directory.CreateDirectory(args[1]);
            using (var sheet = new Bitmap(1040, 710))
            using (var g = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 11))
            using (var ink = new SolidBrush(Color.FromArgb(236, 240, 246)))
            using (var dark = new SolidBrush(Color.FromArgb(16, 19, 24)))
            using (var light = new SolidBrush(Color.FromArgb(240, 243, 248)))
            {
                g.Clear(Color.FromArgb(16, 19, 24));
                g.DrawString("TaskBar+ / native-size icon check", font, ink, 24, 16);
                int x = 24;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int size = sizes[i];
                    File.WriteAllBytes(Path.Combine(args[1], "icon-" + size + ".png"), images[i]);
                    using (var stream = new MemoryStream(images[i]))
                    using (var icon = Image.FromStream(stream))
                    {
                        int cell = Math.Max(62, size + 20);
                        g.DrawString(size + " px", font, ink, x, 48);
                        g.FillRectangle(light, x, 78, cell, 280);
                        g.DrawImageUnscaled(icon, x + (cell - size) / 2, 84);
                        g.FillRectangle(dark, x, 382, cell, 300);
                        g.DrawImageUnscaled(icon, x + (cell - size) / 2, 394);
                        x += cell + 8;
                    }
                }
                sheet.Save(Path.Combine(args[1], "icon-size-check.png"), ImageFormat.Png);
            }
        }
    }
}
