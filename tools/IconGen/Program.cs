using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        var sizes = new[] { 256, 48, 32, 16 };
        var pngs = new List<byte[]>();

        foreach (var size in sizes)
        {
            using var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var accent = Color.FromArgb(88, 166, 255);
            var dark = Color.FromArgb(28, 33, 40);
            var white = Color.FromArgb(230, 237, 243);
            float s = size / 128f;

            using (var b = new SolidBrush(accent))
                g.FillEllipse(b, 2*s, 2*s, 124*s, 124*s);
            using (var b = new SolidBrush(dark))
                g.FillEllipse(b, 14*s, 14*s, 100*s, 100*s);
            using (var p = new Pen(accent, 2*s))
                g.DrawEllipse(p, 24*s, 24*s, 80*s, 80*s);

            float cx = 64*s, cy = 64*s;
            using (var b = new SolidBrush(white))
            {
                var pts = new PointF[] {
                    new(cx, 36*s), new(cx+3*s, 48*s), new(cx+3*s, 53*s),
                    new(cx+26*s, 60*s), new(cx+26*s, 64*s), new(cx+3*s, 62*s),
                    new(cx+3*s, 78*s), new(cx+12*s, 84*s), new(cx+12*s, 87*s),
                    new(cx+3*s, 82*s), new(cx, 90*s),
                    new(cx-3*s, 82*s), new(cx-12*s, 87*s), new(cx-12*s, 84*s),
                    new(cx-3*s, 78*s), new(cx-3*s, 62*s),
                    new(cx-26*s, 64*s), new(cx-26*s, 60*s), new(cx-3*s, 53*s),
                    new(cx-3*s, 48*s)
                };
                g.FillPolygon(b, pts);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        var icoPath = Path.Combine("src", "ModInstaller.App", "Resources", "icon.ico");
        using var fs = new FileStream(icoPath, FileMode.Create);
        using var w = new BinaryWriter(fs);

        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)pngs.Count);

        int offset = 6 + pngs.Count * 16;
        for (int i = 0; i < pngs.Count; i++)
        {
            byte dim = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
            w.Write(dim); w.Write(dim);
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);

        Console.WriteLine($"Icon created at {icoPath} ({new FileInfo(icoPath).Length} bytes)");
    }
}
