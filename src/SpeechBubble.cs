using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace MintPet
{
    /// <summary>
    /// Cream speech bubble with a tail. Layout and drawing are split: the owner
    /// decides where the bubble may sit on screen, this class measures the text
    /// and paints it at the given local rectangle.
    /// </summary>
    public sealed class SpeechBubble
    {
        public const int DurationMs = 4600;
        private const int FadeInMs = 140;
        private const int FadeOutMs = 280;
        private const int PadX = 13;
        private const int PadY = 8;
        private const int AlertMs = 620;

        private static readonly StringFormat LineFormat = new StringFormat(StringFormatFlags.NoWrap);

        public bool Visible { get; private set; }
        public string Text { get; private set; }
        public bool NeedsLayout { get; private set; }
        public Rectangle Bounds { get; private set; }   // window-local, updated by Draw

        private List<string> lines;
        private int bubbleWidth;
        private int bubbleHeight;
        private int shownAt;
        private int alertStart = -1;

        public float Alpha
        {
            get
            {
                if (!Visible) return 0f;
                int age = Environment.TickCount - shownAt;
                if (age < 0) age = 0;
                if (age > DurationMs) return 0f;

                float alpha = 1f;
                if (age < FadeInMs) alpha = (float)age / FadeInMs;
                int remaining = DurationMs - age;
                if (remaining < FadeOutMs) alpha = Math.Min(alpha, (float)remaining / FadeOutMs);
                if (alpha < 0f) alpha = 0f;
                if (alpha > 1f) alpha = 1f;
                return alpha;
            }
        }

        public bool AlertActive
        {
            get
            {
                if (alertStart < 0) return false;
                int age = Environment.TickCount - alertStart;
                return age >= 0 && age < AlertMs;
            }
        }

        /// <summary>Starts a short horizontal jitter + red border, used for balance deductions.</summary>
        public void StartAlert()
        {
            alertStart = Environment.TickCount;
        }

        private int AlertOffsetX
        {
            get
            {
                if (alertStart < 0) return 0;
                int elapsed = Environment.TickCount - alertStart;
                if (elapsed < 0 || elapsed >= AlertMs) return 0;

                double t = (double)elapsed / AlertMs;
                double damp = 1.0 - t;
                return (int)Math.Round(Math.Sin(t * Math.PI * 8.0) * 5.0 * damp);
            }
        }

        public void Show(string text)
        {
            Text = text;
            Visible = true;
            NeedsLayout = true;
            shownAt = Environment.TickCount;
            alertStart = -1;
        }

        public void Hide()
        {
            Visible = false;
            alertStart = -1;
        }

        public void InvalidateLayout()
        {
            NeedsLayout = true;
        }

        public void Tick()
        {
            if (Visible && Environment.TickCount - shownAt > DurationMs) Visible = false;
        }

        /// <summary>Wraps the text and returns the bubble size (without the tail).</summary>
        public Size Prepare(Graphics g, Font font, int maxTextWidth)
        {
            if (!Visible) return Size.Empty;
            if (lines != null && !NeedsLayout) return new Size(bubbleWidth, bubbleHeight);

            lines = Wrap(g, Text, font, maxTextWidth);

            float lineHeight = font.GetHeight(g);
            int textWidth = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                int w = (int)Math.Ceiling(g.MeasureString(lines[i], font, PointF.Empty, LineFormat).Width);
                if (w > textWidth) textWidth = w;
            }

            bubbleWidth = Math.Max(84, textWidth + PadX * 2);
            bubbleWidth = Math.Min(bubbleWidth, maxTextWidth + PadX * 2);
            bubbleHeight = (int)Math.Ceiling(lines.Count * lineHeight) + PadY * 2;
            NeedsLayout = false;
            return new Size(bubbleWidth, bubbleHeight);
        }

        public void Draw(Graphics g, Font font, Rectangle bounds, Point tailTip, bool tailUp)
        {
            float alpha = Alpha;
            if (!Visible || alpha <= 0.01f || lines == null || bounds.Width <= 0) return;

            int dx = AlertOffsetX;
            Rectangle drawBounds = new Rectangle(bounds.X + dx, bounds.Y, bounds.Width, bounds.Height);
            Bounds = drawBounds;

            BubbleLook look = UiStyle.Look;
            bool alert = AlertActive;
            int a = (int)(255 * alpha);
            Color fill = Color.FromArgb((int)(look.FillAlpha * alpha), look.Fill);
            Color border = alert
                ? Color.FromArgb(a, 222, 55, 55)
                : Color.FromArgb(a, look.Border);
            Color textColor = Color.FromArgb(a, 33, 35, 52);
            Color shadow = Color.FromArgb((int)(look.ShadowAlpha * alpha), 0, 0, 0);
            float borderWidth = alert ? Math.Max(2.0f, look.BorderWidth + 0.8f) : look.BorderWidth;
            bool hasBorder = alert || look.BorderWidth > 0.01f;

            // keep the tail root inside the bubble
            int rootX = Math.Max(drawBounds.Left + 14, Math.Min(drawBounds.Right - 14, tailTip.X + dx));
            int baseY = tailUp ? drawBounds.Top : drawBounds.Bottom;
            PointF left = new PointF(rootX - 9, baseY);
            PointF right = new PointF(rootX + 9, baseY);
            PointF tip = new PointF(rootX, tailTip.Y);

            // shadow
            using (GraphicsPath path = RoundedRect(new Rectangle(drawBounds.X, drawBounds.Y + look.ShadowOffset, drawBounds.Width, drawBounds.Height), look.Radius))
            using (SolidBrush brush = new SolidBrush(shadow))
            {
                g.FillPath(brush, path);
            }

            // tail first, then the body covers its open end
            using (GraphicsPath tail = new GraphicsPath())
            {
                tail.AddPolygon(new PointF[] { left, tip, right });
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, tail);
                if (hasBorder)
                {
                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        g.DrawLine(pen, left, tip);
                        g.DrawLine(pen, tip, right);
                    }
                }
            }

            // body
            using (GraphicsPath path = RoundedRect(drawBounds, look.Radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (hasBorder)
                {
                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // optional inner hairline, also red while alerting
            if (look.DoubleBorder || alert)
            {
                Rectangle inner = new Rectangle(drawBounds.X + 4, drawBounds.Y + 4, drawBounds.Width - 8, drawBounds.Height - 8);
                if (inner.Width > 8 && inner.Height > 8)
                {
                    Color innerColor = alert
                        ? Color.FromArgb((int)(210 * alpha), 230, 76, 76)
                        : Color.FromArgb((int)(210 * alpha), look.InnerBorder);
                    using (GraphicsPath path = RoundedRect(inner, Math.Max(2, look.Radius - 3)))
                    using (Pen pen = new Pen(innerColor, 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // text
            float lineHeight = font.GetHeight(g);
            using (SolidBrush brush = new SolidBrush(textColor))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    g.DrawString(lines[i], font, brush, drawBounds.X + PadX, drawBounds.Y + PadY + i * lineHeight, LineFormat);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static List<string> Wrap(Graphics g, string text, Font font, float maxWidth)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            string[] paragraphs = text.Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                List<string> tokens = Tokenize(paragraphs[p]);
                string line = "";

                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];

                    if (line.Length == 0)
                    {
                        line = token.TrimStart();
                        if (line.Length > 1 && Measure(g, line, font) > maxWidth)
                        {
                            string rest = line;
                            line = "";
                            while (rest.Length > 0)
                            {
                                int take = rest.Length;
                                while (take > 1 && Measure(g, rest.Substring(0, take), font) > maxWidth) take--;
                                result.Add(rest.Substring(0, take));
                                rest = rest.Substring(take);
                            }
                        }
                        continue;
                    }

                    string candidate = line + token;
                    if (Measure(g, candidate, font) <= maxWidth)
                    {
                        line = candidate;
                    }
                    else
                    {
                        result.Add(line.TrimEnd());
                        line = token.TrimStart();
                    }
                }

                result.Add(line.TrimEnd());
            }

            if (result.Count == 0) result.Add("");
            return result;
        }

        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();
            StringBuilder word = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (IsWordChar(c))
                {
                    word.Append(c);
                    continue;
                }

                if (word.Length > 0)
                {
                    tokens.Add(word.ToString());
                    word.Length = 0;
                }
                tokens.Add(c.ToString());
            }

            if (word.Length > 0) tokens.Add(word.ToString());
            return tokens;
        }

        private static bool IsWordChar(char c)
        {
            if (c > 0x2E80) return false;
            return char.IsLetterOrDigit(c) || c == '.' || c == ',' || c == '_' ||
                   c == '-' || c == '¥' || c == '$' || c == '%' || c == ':' || c == '/';
        }

        private static float Measure(Graphics g, string text, Font font)
        {
            return g.MeasureString(text, font, PointF.Empty, LineFormat).Width;
        }
    }
}
