// Script to generate a simple ICO file for the app
// Run with: dotnet script GenerateIcon.csx
// Or just generate a BMP-based ICO manually

using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;

// Create 256x256, 48x48, 32x32, 16x16 icon bitmaps
var sizes = new[] { 256, 48, 32, 16 };
var pngs = new List<byte[]>();

foreach (var size in sizes)
{
    using var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

    var accentBlue = Color.FromArgb(88, 166, 255);
    var darkBg = Color.FromArgb(28, 33, 40);
    var white = Color.FromArgb(230, 237, 243);

    float scale = size / 128f;

    // Background circle
    using (var brush = new SolidBrush(accentBlue))
        g.FillEllipse(brush, 2 * scale, 2 * scale, 124 * scale, 124 * scale);

    // Inner circle
    using (var brush = new SolidBrush(darkBg))
        g.FillEllipse(brush, 12 * scale, 12 * scale, 104 * scale, 104 * scale);

    // Inner ring
    using (var pen = new Pen(accentBlue, 2 * scale))
        g.DrawEllipse(pen, 22 * scale, 22 * scale, 84 * scale, 84 * scale);

    // Simple aircraft shape
    var cx = 64 * scale;
    var cy = 64 * scale;
    using (var brush = new SolidBrush(white))
    {
        // Fuselage
        var fuselage = new PointF[]
        {
            new(cx, 34 * scale),       // nose
            new(cx + 4 * scale, 48 * scale),
            new(cx + 4 * scale, 52 * scale),
            new(cx + 28 * scale, 60 * scale), // right wing tip
            new(cx + 28 * scale, 64 * scale),
            new(cx + 4 * scale, 62 * scale),
            new(cx + 4 * scale, 78 * scale),
            new(cx + 14 * scale, 84 * scale), // right tail
            new(cx + 14 * scale, 87 * scale),
            new(cx + 4 * scale, 82 * scale),
            new(cx, 92 * scale),       // tail
            new(cx - 4 * scale, 82 * scale),
            new(cx - 14 * scale, 87 * scale), // left tail
            new(cx - 14 * scale, 84 * scale),
            new(cx - 4 * scale, 78 * scale),
            new(cx - 4 * scale, 62 * scale),
            new(cx - 28 * scale, 64 * scale), // left wing tip
            new(cx - 28 * scale, 60 * scale),
            new(cx - 4 * scale, 52 * scale),
            new(cx - 4 * scale, 48 * scale),
        };
        g.FillPolygon(brush, fuselage);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    pngs.Add(ms.ToArray());
}

// Build ICO file
using var icoStream = new FileStream(
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src", "ModInstaller.App", "Resources", "icon.ico"),
    FileMode.Create);

// ICO header
icoStream.Write(BitConverter.GetBytes((short)0)); // reserved
icoStream.Write(BitConverter.GetBytes((short)1)); // ICO type
icoStream.Write(BitConverter.GetBytes((short)pngs.Count)); // image count

// Calculate offsets
int headerSize = 6 + (pngs.Count * 16);
int offset = headerSize;

for (int i = 0; i < pngs.Count; i++)
{
    byte w = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
    byte h = w;
    icoStream.WriteByte(w);  // width
    icoStream.WriteByte(h);  // height
    icoStream.WriteByte(0);  // color palette
    icoStream.WriteByte(0);  // reserved
    icoStream.Write(BitConverter.GetBytes((short)1)); // color planes
    icoStream.Write(BitConverter.GetBytes((short)32)); // bits per pixel
    icoStream.Write(BitConverter.GetBytes(pngs[i].Length)); // size
    icoStream.Write(BitConverter.GetBytes(offset)); // offset
    offset += pngs[i].Length;
}

foreach (var png in pngs)
    icoStream.Write(png);

Console.WriteLine("Icon generated successfully!");
