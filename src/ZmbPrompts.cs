using System;
using System.Windows;
using System.Windows.Media;

namespace ZMB
{
    // Ikon tombol di layar: keyboard, stik Xbox (A B X Y) atau stik PlayStation (silang, bulat, kotak, segitiga)
    public partial class ZmbWindow
    {
        bool promptKeyboard; // true setelah keyboard/mouse dipakai, false lagi saat stik ditekan

        bool PadPrompts { get { return Gamepad.AnyConnected && !promptKeyboard; } }
        bool SonyPrompts { get { return Gamepad.Style == Gamepad.StyleSony; } }

        // k: ok, back, opt, sel, updown, bumpers
        string PromptText(string k, bool pad)
        {
            if (!pad)
                switch (k) { case "ok": return "Enter"; case "back": return "Esc"; case "opt": return "Tab"; case "sel": return "I"; case "updown": return "\u2191\u2193"; default: return "[ ]"; }
            bool sony = SonyPrompts;
            switch (k)
            {
                case "ok": return "A";
                case "back": return "B";
                case "opt": return "Y";
                case "sel": return "X";
                case "updown": return "\u2195";
                default: return sony ? "L1 R1" : "LB RB";
            }
        }

        static bool IsFaceButton(string k) { return k == "ok" || k == "back" || k == "opt" || k == "sel"; }

        double PromptWidth(string k, double fs, double kh, bool pad)
        {
            if (pad && IsFaceButton(k)) return kh;
            return Math.Max(kh, FT(PromptText(k, pad), fs, 2, false, 0).Width + 14 * S);
        }

        void DrawPrompt(DrawingContext dc, string k, Rect kr, double fs, double a, bool pad)
        {
            double kh = kr.Height;
            if (pad && IsFaceButton(k) && SonyPrompts)
            {
                DrawSonyButton(dc, k, kr, a);
                return;
            }
            Color c = Rgb(80, 84, 96);
            if (pad)
            {
                switch (k)
                {
                    case "ok": c = Rgb(90, 200, 90); break;
                    case "back": c = Rgb(230, 70, 60); break;
                    case "opt": c = Rgb(240, 200, 40); break;
                    case "sel": c = Rgb(60, 120, 230); break;
                    default: c = Rgb(120, 124, 136); break;
                }
            }
            bool round = pad && IsFaceButton(k);
            double rad = round ? kh / 2 : 5 * S;
            dc.DrawRoundedRectangle(BrushA(BadgeBrush(c), 0.92 * a), null, kr, rad, rad);
            FormattedText kf = FT(PromptText(k, pad), fs, 2, false, 0);
            kf.SetForegroundBrush(WhiteA(a));
            dc.DrawText(kf, new Point(kr.X + (kr.Width - kf.Width) / 2, kr.Y + (kh - kf.Height) / 2));
            kf.SetForegroundBrush(brWhite);
        }

        // Tombol PlayStation: lingkaran gelap dengan simbol berwarna
        void DrawSonyButton(DrawingContext dc, string k, Rect kr, double a)
        {
            double cx = kr.X + kr.Width / 2, cy = kr.Y + kr.Height / 2, R = kr.Height / 2;
            dc.DrawEllipse(BrushA(SolidOf(Rgb(34, 36, 43)), 0.95 * a), new Pen(WhiteA(0.22 * a), Math.Max(1, R * 0.07)), new Point(cx, cy), R, R);
            double r = R * 0.48, th = Math.Max(1.2, R * 0.16);
            Color col;
            Geometry g;
            switch (k)
            {
                case "ok": // silang
                    col = Rgb(124, 178, 232);
                    {
                        double d = r * 0.9;
                        StreamGeometry sg = new StreamGeometry();
                        using (StreamGeometryContext gc = sg.Open())
                        {
                            gc.BeginFigure(new Point(cx - d, cy - d), false, false); gc.LineTo(new Point(cx + d, cy + d), true, false);
                            gc.BeginFigure(new Point(cx + d, cy - d), false, false); gc.LineTo(new Point(cx - d, cy + d), true, false);
                        }
                        g = sg;
                    }
                    break;
                case "back": // bulat
                    col = Rgb(255, 102, 102);
                    g = new EllipseGeometry(new Point(cx, cy), r, r);
                    break;
                case "opt": // segitiga
                    col = Rgb(64, 226, 160);
                    {
                        double h = r * 1.05;
                        StreamGeometry sg = new StreamGeometry();
                        using (StreamGeometryContext gc = sg.Open())
                        {
                            gc.BeginFigure(new Point(cx, cy - h), false, true);
                            gc.LineTo(new Point(cx + h * 0.95, cy + h * 0.62), true, true);
                            gc.LineTo(new Point(cx - h * 0.95, cy + h * 0.62), true, true);
                        }
                        g = sg;
                    }
                    break;
                default: // kotak
                    col = Rgb(240, 140, 200);
                    g = new RectangleGeometry(new Rect(cx - r * 0.85, cy - r * 0.85, r * 1.7, r * 1.7));
                    break;
            }
            Pen p = new Pen(BrushA(SolidOf(col), a), th);
            p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; p.LineJoin = PenLineJoin.Round;
            dc.DrawGeometry(null, p, g);
        }
    }
}
