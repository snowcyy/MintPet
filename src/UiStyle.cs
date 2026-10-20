using System;
using System.Drawing;

namespace MintPet
{
    internal enum BubbleStyle
    {
        Pixel,
        Soft,
        Minimal,
        Double
    }

    internal sealed class BubbleLook
    {
        public Color Fill;
        public int FillAlpha;
        public Color Border;
        public float BorderWidth;
        public int Radius;
        public int ShadowAlpha;
        public int ShadowOffset;
        public bool DoubleBorder;
        public Color InnerBorder;
    }

    /// <summary>Shared border/shadow presets for the speech bubble and the balance chip.</summary>
    internal static class UiStyle
    {
        public static BubbleStyle Current = BubbleStyle.Pixel;

        public static BubbleLook Look
        {
            get
            {
                switch (Current)
                {
                    case BubbleStyle.Soft:
                        return new BubbleLook
                        {
                            Fill = Color.FromArgb(255, 253, 247),
                            FillAlpha = 250,
                            Border = Color.FromArgb(168, 158, 142),
                            BorderWidth = 1.2f,
                            Radius = 14,
                            ShadowAlpha = 45,
                            ShadowOffset = 3
                        };

                    case BubbleStyle.Minimal:
                        return new BubbleLook
                        {
                            Fill = Color.FromArgb(255, 253, 247),
                            FillAlpha = 238,
                            Border = Color.Transparent,
                            BorderWidth = 0f,
                            Radius = 14,
                            ShadowAlpha = 55,
                            ShadowOffset = 2
                        };

                    case BubbleStyle.Double:
                        return new BubbleLook
                        {
                            Fill = Color.FromArgb(255, 250, 233),
                            FillAlpha = 252,
                            Border = Color.FromArgb(26, 28, 44),
                            BorderWidth = 2f,
                            Radius = 10,
                            ShadowAlpha = 60,
                            ShadowOffset = 3,
                            DoubleBorder = true,
                            InnerBorder = Color.FromArgb(214, 190, 150)
                        };

                    default: // Pixel
                        return new BubbleLook
                        {
                            Fill = Color.FromArgb(255, 250, 233),
                            FillAlpha = 252,
                            Border = Color.FromArgb(26, 28, 44),
                            BorderWidth = 3f,
                            Radius = 4,
                            ShadowAlpha = 90,
                            ShadowOffset = 3
                        };
                }
            }
        }

        public static BubbleStyle Parse(string name)
        {
            try
            {
                return (BubbleStyle)Enum.Parse(typeof(BubbleStyle), name, true);
            }
            catch
            {
                return BubbleStyle.Pixel;
            }
        }

        public static string Label(BubbleStyle style)
        {
            switch (style)
            {
                case BubbleStyle.Soft: return "柔和细边";
                case BubbleStyle.Minimal: return "无边框";
                case BubbleStyle.Double: return "双描边";
                default: return "像素风";
            }
        }
    }
}
