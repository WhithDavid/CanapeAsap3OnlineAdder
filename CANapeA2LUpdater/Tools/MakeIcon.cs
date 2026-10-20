using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

internal static class MakeIcon
{
    private static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "qt-auto-adder.ico";
        using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using (Brush background = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(0, 145, 210), Color.FromArgb(0, 82, 150), 45F))
            using (Font font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush text = new SolidBrush(Color.White))
            {
                graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 7);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("QT", font, text, new PointF(3, 7));
            }
            using (Icon icon = Icon.FromHandle(bitmap.GetHicon()))
            using (FileStream stream = File.Create(path))
            {
                icon.Save(stream);
            }
        }
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            int diameter = radius * 2;
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
        }
    }
}
