using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// The same simple vector geometry as assets/logo.svg, rasterized at each Windows size.
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
        using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(large))
        using (var bg = new SolidBrush(Color.FromArgb(25, 30, 39)))
        using (var mint = new SolidBrush(Color.FromArgb(109, 220, 176)))
        using (var plus = new Pen(Color.FromArgb(236, 240, 246), 12))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size * 4f / 256, size * 4f / 256);
            Rounded(g, bg, 8, 8, 240, 240, 52);
            Rounded(g, mint, 52, 96, 48, 48, 10);
            Rounded(g, mint, 116, 96, 48, 48, 10);
            Rounded(g, mint, 44, 168, 168, 16, 8);
            plus.StartCap = LineCap.Round; plus.EndCap = LineCap.Round;
            g.DrawLine(plus, 192, 52, 192, 92); g.DrawLine(plus, 172, 72, 212, 72);
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
    }
}
