using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

using Timer = System.Windows.Forms.Timer;

namespace MintPet
{
    /// <summary>
    /// Frameless, per-pixel transparent, always-on-top window that plays a GIF
    /// as the desktop pet.  Dragging moves the window, a plain click makes the
    /// pet talk, right click opens the menu.
    /// </summary>
    public sealed class PetWindow : Form
    {
        private const int BubblePadX = 300;
        private const int BubblePadTop = 140;
        private const int BubblePadBottom = 190;
        private const int MaxBubbleTextWidth = 240;
        private const int TargetPetHeight = 160;

        private readonly PetSettings settings;
        private readonly Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly SpeechBubble bubble = new SpeechBubble();
        private readonly List<string> lines = new List<string>();
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem sizeMenu;
        private readonly Random random = new Random();
        private readonly Font bubbleFont;
        private readonly ToolStripMenuItem balanceItem;
        private readonly ToolStripMenuItem showBalanceItem;
        private readonly ToolStripMenuItem sfxMenu;
        private readonly ToolStripMenuItem clickSoundItem;
        private readonly ToolStripMenuItem balanceVolumeMenu;
        private readonly ToolStripMenuItem clickVolumeMenu;
        private readonly ToolStripMenuItem refreshMenu;
        private readonly ToolStripMenuItem tickMenu;
        private readonly ToolStripMenuItem hitMenu;
        private readonly ToolStripMenuItem styleMenu;
        private readonly ToolStripMenuItem autoStartItem;

        private GifAnimation idleAnimation;
        private GifAnimation clickAnimation;
        private Bitmap[] idleFrames;
        private Bitmap[] clickFrames;
        private Playback idle;
        private Playback click;
        private bool clickPlaying;

        private int displayWidth;
        private int displayHeight;
        private int petX;
        private int petY;
        private int contentLeft;
        private int contentTop;
        private int contentWidth;
        private int contentHeight;
        private Bitmap canvas;

        private bool dragging;
        private bool dragged;
        private Point dragCursorStart;
        private Point dragWindowStart;
        private int hopStart = -1;
        private string lastLine = "";
        private bool forceRender = true;

        // DeepSeek balance widget
        private Rectangle chipBounds;
        private BalanceInfo currentBalance;
        private decimal? previousTotal;
        private string previousCurrency = "";
        private string balanceText = "余额未设置";
        private int balanceState = 3;               // 0 ok, 1 error/stale, 2 loading, 3 not configured

        // animated balance counter: the number jumps in discrete steps, the chip
        // and pet shake briefly when a change is detected
        private const int BalanceShakeMs = 420;
        private const int BalanceStepWindowMs = 2400;
        private bool stepActive;
        private decimal stepFrom;
        private decimal stepTo;
        private string stepSymbol = "¥";
        private int stepIndex;
        private int stepCount = 1;
        private int stepInterval = 300;
        private int stepNextAt;
        private decimal displayTotal;
        private int shakeStart = -1;
        private bool shakeDeduction;
        private string pendingBubbleText;
        private bool pendingBubbleAlert;
        private int pendingBubbleDue;
        private IntPtr menuHook = IntPtr.Zero;
        private Native.LowLevelMouseProc menuHookProc;
        private HitSound hitSound = HitSound.Classic;

        private bool fetchingBalance;
        private int fetchGeneration;
        private DateTime lastFetchAttempt = DateTime.MinValue;

        public PetWindow()
        {
            settings = PetSettings.Load();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Text = "MintPet";

            bubbleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            hitSound = ParseHitSound(settings.HitSound);
            UiStyle.Current = UiStyle.Parse(settings.BubbleStyle);

            lines.Add("今天也要加油呀！");
            lines.Add("我一直在这里陪着你。");
            lines.Add("戳我干嘛，嘿嘿~");
            lines.Add("要不要休息一下，喝口水？");
            lines.Add("工作再忙，也别忘了眨眨眼。");
            lines.Add("你专注的样子真好看。");
            lines.Add("摸鱼一下下，没关系的。");
            lines.Add("记得保存文件哦！");

            menu = new ContextMenuStrip();
            menu.Items.Add("说句话", null, delegate { SaySomething(); });
            menu.Items.Add("换个 GIF…", null, delegate { PickGif(); });

            sizeMenu = new ToolStripMenuItem("大小");
            AddSizeItem("自动", 0);
            for (int i = 1; i <= 6; i++) AddSizeItem(i + "×", i);
            menu.Items.Add(sizeMenu);

            menu.Items.Add(new ToolStripSeparator());

            balanceItem = new ToolStripMenuItem("余额：未设置 API Key");
            balanceItem.Enabled = false;
            menu.Items.Add(balanceItem);

            menu.Items.Add("立即刷新余额", null, delegate { StartBalanceFetch(true); });
            menu.Items.Add("设置 API Key…", null, delegate { ChangeApiKey(); });
            menu.Items.Add("清除 API Key", null, delegate { ClearApiKey(); });

            showBalanceItem = new ToolStripMenuItem("显示余额");
            showBalanceItem.Click += delegate { ToggleShowBalance(); };
            menu.Items.Add(showBalanceItem);

            sfxMenu = new ToolStripMenuItem("音效");

            hitMenu = new ToolStripMenuItem("余额变动音效");
            ToolStripMenuItem offItem = new ToolStripMenuItem("关闭");
            offItem.Click += delegate
            {
                settings.BalanceSound = false;
                settings.Save();
                Notify("余额变动音效：关");
                UpdateMenuState();
            };
            hitMenu.DropDownItems.Add(offItem);
            AddHitItem("经典受伤", HitSound.Classic);
            AddHitItem("金币", HitSound.Coin);
            AddHitItem("8-bit", HitSound.EightBit);
            AddHitItem("重拳", HitSound.Punch);
            AddHitItem("激光", HitSound.Laser);
            AddHitItem("气泡", HitSound.Bubble);
            AddHitItem("啵（点击音）", HitSound.Pop);
            sfxMenu.DropDownItems.Add(hitMenu);

            balanceVolumeMenu = new ToolStripMenuItem("余额音量");
            AddBalanceVolumeItem("小 40%", 40);
            AddBalanceVolumeItem("中 70%", 70);
            AddBalanceVolumeItem("大 100%", 100);
            sfxMenu.DropDownItems.Add(balanceVolumeMenu);
            sfxMenu.DropDownItems.Add(new ToolStripSeparator());

            clickSoundItem = new ToolStripMenuItem("点击音效");
            clickSoundItem.Click += delegate
            {
                settings.ClickSound = !settings.ClickSound;
                settings.Save();
                if (settings.ClickSound) Chime.PlayPop(settings.ClickVolume);
                Notify(settings.ClickSound ? "点击音效：开" : "点击音效：关");
            };
            sfxMenu.DropDownItems.Add(clickSoundItem);

            clickVolumeMenu = new ToolStripMenuItem("点击音量");
            AddClickVolumeItem("小 40%", 40);
            AddClickVolumeItem("中 70%", 70);
            AddClickVolumeItem("大 100%", 100);
            sfxMenu.DropDownItems.Add(clickVolumeMenu);
            menu.Items.Add(sfxMenu);

            styleMenu = new ToolStripMenuItem("气泡样式");
            AddStyleItem("像素风", BubbleStyle.Pixel);
            AddStyleItem("柔和细边", BubbleStyle.Soft);
            AddStyleItem("无边框", BubbleStyle.Minimal);
            AddStyleItem("双描边", BubbleStyle.Double);
            menu.Items.Add(styleMenu);

            refreshMenu = new ToolStripMenuItem("刷新间隔");
            AddRefreshItem("1 分钟", 60);
            AddRefreshItem("2 分钟", 120);
            AddRefreshItem("5 分钟", 300);
            AddRefreshItem("10 分钟", 600);
            AddRefreshItem("30 分钟", 1800);
            menu.Items.Add(refreshMenu);

            tickMenu = new ToolStripMenuItem("数字跳动间隔");
            AddTickItem("0.15 秒", 150);
            AddTickItem("0.3 秒", 300);
            AddTickItem("0.5 秒", 500);
            AddTickItem("0.8 秒", 800);
            menu.Items.Add(tickMenu);

            menu.Items.Add(new ToolStripSeparator());
            autoStartItem = new ToolStripMenuItem("开机自启");
            autoStartItem.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(autoStartItem);
            menu.Items.Add("修复开机自启（管理员）", null, delegate { RepairAutoStart(); });

            menu.Items.Add("重置位置", null, delegate
            {
                MoveToDefaultSpot();
                SavePosition();
                Notify("已回到默认位置");
            });
            menu.Items.Add("退出", null, delegate { Close(); });
            menu.Opening += delegate { UpdateMenuState(); };

            menu.Closed += delegate
            {
                StopMenuWatch();
                SetMenuActivation(false);
            };

            balanceText = string.IsNullOrEmpty(settings.ApiKey) ? "余额未设置" : "余额查询中…";
            balanceState = string.IsNullOrEmpty(settings.ApiKey) ? 3 : 2;

            if (!LoadStartupGif())
            {
                MessageBox.Show("没有找到可用的 GIF，MintPet 没办法启动。", "MintPet",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw new InvalidOperationException("no gif");
            }

            MoveToDefaultSpot();

            timer = new Timer();
            timer.Interval = 33;
            timer.Tick += OnTimerTick;
        }

        private static string AppFolder
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        private static string BundledGifResource
        {
            get { return "pet.gif"; }
        }

        private Rectangle PetBounds
        {
            get
            {
                return new Rectangle(petX + contentLeft, petY + contentTop,
                                     Math.Max(1, contentWidth), Math.Max(1, contentHeight));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // ---------------------------------------------------------------- load

        private bool LoadStartupGif()
        {
            string path = settings.GifPath;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(AppFolder, "assets", "pet.gif");
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path) && TryLoad(path)) return true;

            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledGifResource))
                {
                    if (stream == null) return false;
                    GifAnimation animation = GifAnimation.FromStream(stream);
                    ApplyAnimations(animation, null);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TryLoad(string path)
        {
            try
            {
                GifAnimation animation = GifAnimation.FromFile(path);
                GifAnimation clickGif = LoadSiblingClickGif(path);
                ApplyAnimations(animation, clickGif);
                settings.GifPath = path;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("这个 GIF 读不出来：\n" + ex.Message, "MintPet",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static GifAnimation LoadSiblingClickGif(string gifPath)
        {
            try
            {
                string folder = Path.GetDirectoryName(gifPath);
                if (string.IsNullOrEmpty(folder)) return null;

                string clickPath = Path.Combine(folder, "click.gif");
                if (!File.Exists(clickPath)) return null;
                if (string.Equals(clickPath, gifPath, StringComparison.OrdinalIgnoreCase)) return null;

                return GifAnimation.FromFile(clickPath);
            }
            catch
            {
                return null;
            }
        }

        private void ApplyAnimations(GifAnimation idleGif, GifAnimation clickGif)
        {
            DisposeAnimations();
            idleAnimation = idleGif;
            clickAnimation = clickGif;
            RebuildFrames();
        }

        private void DisposeAnimations()
        {
            if (idleFrames != null)
            {
                for (int i = 0; i < idleFrames.Length; i++) idleFrames[i].Dispose();
                idleFrames = null;
            }
            if (clickFrames != null)
            {
                for (int i = 0; i < clickFrames.Length; i++) clickFrames[i].Dispose();
                clickFrames = null;
            }
            if (idleAnimation != null) idleAnimation.Dispose();
            if (clickAnimation != null) clickAnimation.Dispose();
            idleAnimation = null;
            clickAnimation = null;
        }

        private void RebuildFrames()
        {
            if (idleAnimation == null) return;

            int scale = settings.Scale > 0 ? settings.Scale : PickAutoScale(idleAnimation.Height);
            // keep large sprites from becoming a 512px wall on screen; the user can
            // still pick 1x/2x manually from the size menu
            int maxPreferredScale = Math.Max(1, 320 / Math.Max(1, idleAnimation.Height));
            if (scale > maxPreferredScale)
            {
                scale = maxPreferredScale;
                settings.Scale = scale;
                settings.Save();
            }
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int maxPetHeight = Math.Max(120, workArea.Height - 160);
            while (scale > 1 && idleAnimation.Height * scale > maxPetHeight) scale--;

            idleFrames = ScaleFrames(idleAnimation, scale);
            clickFrames = clickAnimation != null ? ScaleFrames(clickAnimation, scale) : null;

            displayWidth = idleAnimation.Width * scale;
            displayHeight = idleAnimation.Height * scale;

            Rectangle content = MeasureContent(idleAnimation, scale);
            contentLeft = content.Left;
            contentTop = content.Top;
            contentWidth = content.Width;
            contentHeight = content.Height;

            int width = BubblePadX * 2 + Math.Max(1, contentWidth);
            int height = BubblePadTop + Math.Max(1, contentHeight) + BubblePadBottom;

            Size oldSize = Size;
            Point oldLocation = Location;
            bool resize = idle != null;

            Size = new Size(width, height);
            if (resize)
            {
                Location = new Point(oldLocation.X + (oldSize.Width - width) / 2,
                                     oldLocation.Y + (oldSize.Height - height) / 2);
            }

            // anchor the window around the visible pet, leaving room for the
            // bubble on every side so it never has to leave the screen
            petX = BubblePadX - contentLeft;
            petY = BubblePadTop - contentTop;

            idle = new Playback(idleFrames, Delays(idleAnimation), false);
            click = clickFrames != null ? new Playback(clickFrames, Delays(clickAnimation), true) : null;
            clickPlaying = false;
            hopStart = -1;

            bubble.InvalidateLayout();
            forceRender = true;
            Render();
        }

        private static int PickAutoScale(int gifHeight)
        {
            int scale = (int)Math.Round((double)TargetPetHeight / gifHeight);
            if (scale < 1) scale = 1;
            if (scale > 6) scale = 6;
            return scale;
        }

        private static int[] Delays(GifAnimation animation)
        {
            int[] delays = new int[animation.Frames.Count];
            for (int i = 0; i < delays.Length; i++) delays[i] = Math.Max(10, animation.Frames[i].DelayMs);
            return delays;
        }

        /// <summary>
        /// Visible (non-transparent) bounds of the animation, in display pixels.
        /// GIFs usually carry transparent padding, and the speech bubble should
        /// point at the drawn head, not at the empty canvas above it.
        /// </summary>
        private static Rectangle MeasureContent(GifAnimation animation, int scale)
        {
            int minX = animation.Width;
            int minY = animation.Height;
            int maxX = -1;
            int maxY = -1;

            for (int f = 0; f < animation.Frames.Count; f++)
            {
                Bitmap bitmap = animation.Frames[f].Bitmap;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (bitmap.GetPixel(x, y).A == 0) continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0) return new Rectangle(0, 0, animation.Width * scale, animation.Height * scale);

            return new Rectangle(minX * scale, minY * scale,
                                 (maxX - minX + 1) * scale, (maxY - minY + 1) * scale);
        }

        private static Bitmap[] ScaleFrames(GifAnimation animation, int scale)
        {
            int width = animation.Width * scale;
            int height = animation.Height * scale;
            Bitmap[] frames = new Bitmap[animation.Frames.Count];

            for (int i = 0; i < frames.Length; i++)
            {
                Bitmap frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(animation.Frames[i].Bitmap, new Rectangle(0, 0, width, height));
                }
                frames[i] = frame;
            }
            return frames;
        }

        // -------------------------------------------------------------- window

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsurePosition();
            SavePosition();
            forceRender = true;
            Render();
            timer.Start();

            if (settings.ShowBalance && !string.IsNullOrEmpty(settings.ApiKey)) StartBalanceFetch(false);
        }

        private void EnsurePosition()
        {
            bool restored = false;
            if (settings.X != int.MinValue && settings.Y != int.MinValue)
            {
                Rectangle saved = new Rectangle(settings.X, settings.Y, Width, Height);
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    if (Screen.AllScreens[i].WorkingArea.IntersectsWith(saved))
                    {
                        Location = new Point(settings.X, settings.Y);
                        restored = true;
                        break;
                    }
                }
            }

            if (!restored) MoveToDefaultSpot();
            ClampToScreen();
        }

        private void MoveToDefaultSpot()
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;

            // align the visible pet (not the transparent window) to the corner
            int petRight = petX + contentLeft + Math.Max(1, contentWidth);
            int petBottom = petY + contentTop + Math.Max(1, contentHeight);

            Location = new Point(workArea.Right - 24 - petRight, workArea.Bottom - 6 - petBottom);
        }

        private void SavePosition()
        {
            settings.X = Left;
            settings.Y = Top;
            settings.Save();
        }

        /// <summary>
        /// Keeps the visible pet inside the screen. The window itself is much
        /// larger than the sprite, so clamping the window rectangle is not
        /// enough: transparent padding could still be on-screen while the pet
        /// sits outside it.
        /// </summary>
        private void ClampToScreen()
        {
            int width = contentWidth > 0 ? contentWidth : displayWidth;
            int height = contentHeight > 0 ? contentHeight : displayHeight;
            int petOffsetX = petX + contentLeft;
            int petOffsetY = petY + contentTop;

            Rectangle petRect = new Rectangle(Left + petOffsetX, Top + petOffsetY,
                                              Math.Max(1, width), Math.Max(1, height));
            Screen screen = Screen.FromRectangle(petRect);
            Rectangle workArea = screen.WorkingArea;

            const int margin = 2;
            int minLeft = workArea.Left + margin - petOffsetX;
            int maxLeft = workArea.Right - margin - petOffsetX - width;
            int minTop = workArea.Top + margin - petOffsetY;
            int maxTop = workArea.Bottom - margin - petOffsetY - height;

            if (maxLeft < minLeft) maxLeft = minLeft;
            if (maxTop < minTop) maxTop = minTop;

            int x = Math.Max(minLeft, Math.Min(maxLeft, Left));
            int y = Math.Max(minTop, Math.Min(maxTop, Top));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        // --------------------------------------------------------------- frame

        private void OnTimerTick(object sender, EventArgs e)
        {
            double delta = clock.Elapsed.TotalMilliseconds;
            clock.Reset();
            clock.Start();
            if (delta < 0) delta = 0;
            if (delta > 250) delta = 250;
            Tick(delta);
        }

        private void Tick(double delta)
        {
            bool bubbleWasVisible = bubble.Visible;
            bubble.Tick();
            ScheduleBalanceRefresh();
            UpdateBalanceSteps();

            if (pendingBubbleText != null && Environment.TickCount >= pendingBubbleDue)
            {
                bubble.Show(pendingBubbleText);
                if (pendingBubbleAlert) bubble.StartAlert();
                pendingBubbleText = null;
                pendingBubbleAlert = false;
                forceRender = true;
            }

            bool changed = bubbleWasVisible && !bubble.Visible;
            if (clickPlaying && click != null)
            {
                if (click.Advance(delta)) changed = true;
                if (click.Finished)
                {
                    clickPlaying = false;
                    changed = true;
                }
            }
            else if (idle != null)
            {
                if (idle.Advance(delta)) changed = true;
            }

            bool hopping = hopStart >= 0 && Environment.TickCount - hopStart < 340;
            if (hopStart >= 0 && !hopping)
            {
                hopStart = -1;
                changed = true;
            }

            if (changed || bubble.Visible || hopping || forceRender)
            {
                forceRender = false;
                Render();
            }
        }

        private void Render()
        {
            if (Width <= 0 || Height <= 0) return;

            if (canvas == null || canvas.Width != Width || canvas.Height != Height)
            {
                if (canvas != null) canvas.Dispose();
                canvas = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                int shakeY = ShakeOffsetY();
                DrawBalanceChip(g, shakeY);

                if (bubble.Visible)
                {
                    DrawSpeechBubble(g);
                }

                Bitmap frame = null;
                if (clickPlaying && click != null) frame = click.Current;
                else if (idle != null) frame = idle.Current;

                if (frame != null)
                {
                    g.DrawImageUnscaled(frame, petX, petY + shakeY / 2 + HopOffset());
                }
            }

            PushLayered();
        }

        private Rectangle GetPetScreenRect()
        {
            int width = contentWidth > 0 ? contentWidth : displayWidth;
            int height = contentHeight > 0 ? contentHeight : displayHeight;
            return new Rectangle(Left + petX + contentLeft, Top + petY + contentTop,
                                 Math.Max(1, width), Math.Max(1, height));
        }

        private void DrawSpeechBubble(Graphics g)
        {
            Size size = bubble.Prepare(g, bubbleFont, MaxBubbleTextWidth);
            if (size.Width <= 0 || size.Height <= 0) return;

            Rectangle pet = GetPetScreenRect();
            Screen screen = Screen.FromRectangle(pet);
            Rectangle workArea = screen.WorkingArea;

            int width = size.Width;
            int height = size.Height;
            int centerX = pet.Left + pet.Width / 2;

            int x = centerX - width / 2;
            int minX = workArea.Left + 8;
            int maxX = workArea.Right - 8 - width;
            if (maxX < minX) maxX = minX;
            x = Math.Max(minX, Math.Min(maxX, x));

            int y = pet.Top - 18 - height;
            bool tailUp = false;

            if (y < workArea.Top + 8)
            {
                // no room above: put the bubble under the pet instead
                y = pet.Bottom + 10;
                tailUp = true;
            }

            int maxY = workArea.Bottom - 8 - height;
            if (y > maxY) y = Math.Max(workArea.Top + 8, maxY);

            Rectangle bounds = new Rectangle(x - Left, y - Top, width, height);
            int tailX = centerX - Left;
            int tailY = tailUp ? (pet.Bottom - Top + 2) : (pet.Top - Top - 2);

            bubble.Draw(g, bubbleFont, bounds, new Point(tailX, tailY), tailUp);
        }

        private void DrawBalanceChip(Graphics g, int offsetY)
        {
            if (!settings.ShowBalance || bubble.Visible)
            {
                chipBounds = Rectangle.Empty;
                return;
            }

            string measuredText = balanceText;
            if (stepActive)
            {
                // keep the pill width steady while the digits jump
                string fromText = FormatBalance(stepSymbol, stepFrom);
                string toText = FormatBalance(stepSymbol, stepTo);
                measuredText = g.MeasureString(fromText, bubbleFont).Width >= g.MeasureString(toText, bubbleFont).Width
                                   ? fromText
                                   : toText;
            }

            SizeF textSize = g.MeasureString(measuredText, bubbleFont);
            int width = Math.Max(84, (int)Math.Ceiling(textSize.Width) + 26);
            int height = (int)Math.Ceiling(bubbleFont.GetHeight(g)) + 16;

            Rectangle pet = GetPetScreenRect();
            Screen screen = Screen.FromRectangle(pet);
            Rectangle workArea = screen.WorkingArea;

            int x = pet.Left + pet.Width / 2 - width / 2;
            int y = pet.Top - 8 - height;

            if (y < workArea.Top + 4)
            {
                // no room above: put it below the pet, then to the side
                if (pet.Bottom + 8 + height <= workArea.Bottom - 4)
                {
                    y = pet.Bottom + 8;
                }
                else if (pet.Right + 8 + width <= workArea.Right - 4)
                {
                    x = pet.Right + 8;
                    y = pet.Top + (pet.Height - height) / 2;
                }
                else
                {
                    x = pet.Left - 8 - width;
                    y = pet.Top + (pet.Height - height) / 2;
                }
            }

            x = Math.Max(workArea.Left + 4, Math.Min(workArea.Right - 4 - width, x));
            y = Math.Max(workArea.Top + 4, Math.Min(workArea.Bottom - 4 - height, y + offsetY));

            x -= Left;
            y -= Top;
            chipBounds = new Rectangle(x, y, width, height);

            // same frame as the speech bubble, just without the tail
            BubbleLook look = UiStyle.Look;
            int radius = Math.Min(look.Radius, height / 2);
            Color borderColor = look.Border;
            float borderWidth = look.BorderWidth;

            using (GraphicsPath shadowPath = RoundedRect(new Rectangle(x, y + look.ShadowOffset, width, height), radius))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(look.ShadowAlpha, 0, 0, 0)))
            {
                g.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath path = RoundedRect(chipBounds, radius))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(look.FillAlpha, look.Fill)))
            {
                g.FillPath(fill, path);
                if (borderWidth > 0.01f)
                {
                    using (Pen border = new Pen(Color.FromArgb(250, borderColor), borderWidth))
                    {
                        g.DrawPath(border, path);
                    }
                }
            }

            if (look.DoubleBorder)
            {
                Rectangle inner = new Rectangle(x + 4, y + 4, width - 8, height - 8);
                if (inner.Width > 8 && inner.Height > 8)
                {
                    Color innerColor = Color.FromArgb(210, look.InnerBorder);
                    using (GraphicsPath path = RoundedRect(inner, Math.Max(2, look.Radius - 3)))
                    using (Pen pen = new Pen(innerColor, 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            Color dotColor;
            if (balanceState == 0) dotColor = Color.FromArgb(235, 56, 183, 100);
            else if (balanceState == 2) dotColor = Color.FromArgb(235, 220, 170, 40);
            else if (balanceState == 3) dotColor = Color.FromArgb(220, 150, 150, 155);
            else dotColor = Color.FromArgb(235, 200, 80, 40);

            using (SolidBrush dot = new SolidBrush(dotColor))
            {
                g.FillEllipse(dot, x + 12, y + height / 2 - 3, 7, 7);
            }

            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(240, 33, 35, 52)))
            {
                g.DrawString(balanceText, bubbleFont, textBrush, x + 24, y + (height - textSize.Height) / 2f);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            if (diameter > bounds.Width) diameter = bounds.Width;
            if (diameter > bounds.Height) diameter = bounds.Height;

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private int HopOffset()
        {
            if (hopStart < 0) return 0;
            double t = (Environment.TickCount - hopStart) / 340.0;
            if (t < 0 || t > 1) return 0;
            return -(int)Math.Round(2 * Math.Sin(Math.PI * t));
        }

        private void PushLayered()
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memoryDc = Native.CreateCompatibleDC(screenDc);
            IntPtr bitmap = canvas.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = Native.SelectObject(memoryDc, bitmap);

            Native.SIZE size = new Native.SIZE();
            size.cx = Width;
            size.cy = Height;

            Native.POINT source = new Native.POINT();
            Native.POINT destination = new Native.POINT();
            destination.X = Left;
            destination.Y = Top;

            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = 0;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;

            Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                                       memoryDc, ref source, 0, ref blend, Native.ULW_ALPHA);

            Native.SelectObject(memoryDc, oldBitmap);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // ---------------------------------------------------------------- input

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (menu.Visible)
            {
                if (CursorInMenu()) return;
                menu.Close();
            }

            if (e.Button == MouseButtons.Right)
            {
                UpdateMenuState();
                SetMenuActivation(true);
                StartMenuWatch();
                Activate();
                Native.SetForegroundWindow(Handle);
                menu.Show(this, PointToClient(Cursor.Position));
                menu.Focus();
                Native.SetForegroundWindow(menu.Handle);
                if (!menu.Visible) SetMenuActivation(false);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            if (settings.ShowBalance && !bubble.Visible && chipBounds.Contains(e.Location))
            {
                StartBalanceFetch(true);
                return;
            }

            if (bubble.Visible && bubble.Bounds.Contains(e.Location))
            {
                bubble.Hide();
                forceRender = true;
                Render();
                return;
            }

            if (!PetBounds.Contains(e.Location)) return;

            dragging = true;
            dragged = false;
            dragCursorStart = Cursor.Position;
            dragWindowStart = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;

            Point current = Cursor.Position;
            int dx = current.X - dragCursorStart.X;
            int dy = current.Y - dragCursorStart.Y;

            if (!dragged && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) dragged = true;
            if (dragged)
            {
                Location = new Point(dragWindowStart.X + dx, dragWindowStart.Y + dy);
                ClampToScreen();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;

            dragging = false;
            Capture = false;

            if (dragged)
            {
                ClampToScreen();
                SavePosition();
            }
            else
            {
                ClickPet();
            }
        }

        private void TriggerClickVisual()
        {
            hopStart = Environment.TickCount;
            if (click != null)
            {
                click.Index = 0;
                click.Elapsed = 0;
                click.Finished = false;
                clickPlaying = true;
            }
            forceRender = true;
        }

        private void ClickPet()
        {
            if (settings.ClickSound) Chime.PlayPop(settings.ClickVolume);
            SaySomething();
            TriggerClickVisual();
            Render();
        }

        private void SaySomething()
        {
            string line = null;

            if (currentBalance != null && balanceState == 0 && random.Next(100) < 40)
            {
                line = PickBalanceLine(currentBalance);
            }

            if (line == null || line == lastLine)
            {
                line = lines[0];
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    string candidate = lines[random.Next(lines.Count)];
                    if (candidate != lastLine)
                    {
                        line = candidate;
                        break;
                    }
                }
            }

            lastLine = line;
            bubble.Show(line);
            bubble.InvalidateLayout();
            forceRender = true;
            Render();
        }

        private void Notify(string text)
        {
            bubble.Show(text);
            forceRender = true;
            Render();
        }

        private string PickBalanceLine(BalanceInfo balance)
        {
            string money = balance.Money;

            if (balance.Total < 10m)
            {
                string[] low =
                {
                    "余额只剩 " + money + " 了，要不要充一点？",
                    "钱包快见底了，现在只剩 " + money + "。"
                };
                return low[random.Next(low.Length)];
            }

            string[] normal =
            {
                "现在还有 " + money + "，慢慢用。",
                "余额 " + money + "，够花一阵子。",
                "钱包里躺着 " + money + " 呢。",
                "报告：余额 " + money + "。"
            };
            return normal[random.Next(normal.Length)];
        }

        // ----------------------------------------------------------------- menu

        private void AddSizeItem(string text, int scale)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = scale;
            item.Click += delegate { ApplyScale(scale); };
            sizeMenu.DropDownItems.Add(item);
        }

        private void SetMenuActivation(bool active)
        {
            try
            {
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                if (active) ex &= ~Native.WS_EX_NOACTIVATE;
                else ex |= Native.WS_EX_NOACTIVATE;
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            }
            catch
            {
            }
        }

        private void StartMenuWatch()
        {
            StopMenuWatch();
            menuHookProc = MenuHookCallback;
            menuHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, menuHookProc,
                Native.GetModuleHandle(null), 0);
            if (menuHook == IntPtr.Zero)
            {
                menuHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, menuHookProc, IntPtr.Zero, 0);
            }
        }

        private void StopMenuWatch()
        {
            if (menuHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(menuHook);
                menuHook = IntPtr.Zero;
            }
            menuHookProc = null;
        }

        private IntPtr MenuHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN ||
                    msg == Native.WM_MBUTTONDOWN)
                {
                    try
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (menu.Visible && !CursorInMenu()) menu.Close();
                            }));
                        }
                        else if (menu.Visible && !CursorInMenu())
                        {
                            menu.Close();
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return Native.CallNextHookEx(menuHook, nCode, wParam, lParam);
        }

        private bool CursorInMenu()
        {
            Point p = Cursor.Position;
            if (menu.Bounds.Contains(p)) return true;
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.DropDown.Visible && mi.DropDown.Bounds.Contains(p)) return true;
            }
            return false;
        }

        private void UpdateMenuState()
        {
            for (int i = 0; i < sizeMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = sizeMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (int)item.Tag == settings.Scale;
            }

            showBalanceItem.Checked = settings.ShowBalance;
            clickSoundItem.Checked = settings.ClickSound;
            autoStartItem.Checked = AutoStart.IsEnabled();

            for (int i = 0; i < refreshMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = refreshMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (int)item.Tag == settings.RefreshSeconds;
            }

            for (int i = 0; i < tickMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = tickMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (int)item.Tag == settings.TickIntervalMs;
            }

            for (int i = 0; i < balanceVolumeMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = balanceVolumeMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (int)item.Tag == settings.BalanceVolume;
            }

            for (int i = 0; i < clickVolumeMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = clickVolumeMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (int)item.Tag == settings.ClickVolume;
            }

            for (int i = 0; i < hitMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = hitMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                if (item.Tag == null) item.Checked = !settings.BalanceSound;
                else item.Checked = settings.BalanceSound && (HitSound)item.Tag == hitSound;
            }

            for (int i = 0; i < styleMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = styleMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                item.Checked = (BubbleStyle)item.Tag == UiStyle.Current;
            }

            if (string.IsNullOrEmpty(settings.ApiKey)) balanceItem.Text = "余额：未设置 API Key";
            else if (currentBalance != null) balanceItem.Text = "余额：" + currentBalance.Money +
                (currentBalance.Currency == "CNY" ? "" : " " + currentBalance.Currency);
            else balanceItem.Text = "余额：尚未获取";
        }

        private void AddRefreshItem(string text, int seconds)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = seconds;
            item.Click += delegate
            {
                settings.RefreshSeconds = seconds;
                settings.Save();
                lastFetchAttempt = DateTime.MinValue;
                Notify("余额刷新间隔：" + text);
            };
            refreshMenu.DropDownItems.Add(item);
        }

        private void AddTickItem(string text, int milliseconds)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = milliseconds;
            item.Click += delegate
            {
                settings.TickIntervalMs = milliseconds;
                settings.Save();
                Notify("数字跳动间隔：" + text);
            };
            tickMenu.DropDownItems.Add(item);
        }

        private void AddBalanceVolumeItem(string text, int volume)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = volume;
            item.Click += delegate
            {
                settings.BalanceVolume = volume;
                settings.Save();
                Chime.PlayHit(hitSound, volume);
                Notify("余额音量：" + text);
                UpdateMenuState();
            };
            balanceVolumeMenu.DropDownItems.Add(item);
        }

        private void AddClickVolumeItem(string text, int volume)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = volume;
            item.Click += delegate
            {
                settings.ClickVolume = volume;
                settings.Save();
                Chime.PlayPop(volume);
                Notify("点击音量：" + text);
                UpdateMenuState();
            };
            clickVolumeMenu.DropDownItems.Add(item);
        }

        private void AddHitItem(string text, HitSound sound)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = sound;
            item.Click += delegate
            {
                hitSound = sound;
                settings.HitSound = sound.ToString();
                settings.BalanceSound = true;
                settings.Save();
                Chime.PlayHit(sound, settings.BalanceVolume);
                Notify("余额变动音效：" + text);
                UpdateMenuState();
            };
            hitMenu.DropDownItems.Add(item);
        }

        private void AddStyleItem(string text, BubbleStyle style)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = style;
            item.Click += delegate
            {
                UiStyle.Current = style;
                settings.BubbleStyle = style.ToString();
                settings.Save();
                bubble.InvalidateLayout();
                Notify("气泡样式：" + text);
            };
            styleMenu.DropDownItems.Add(item);
        }

        private static HitSound ParseHitSound(string name)
        {
            try
            {
                return (HitSound)Enum.Parse(typeof(HitSound), name, true);
            }
            catch
            {
                return HitSound.Classic;
            }
        }

        private void ToggleShowBalance()
        {
            settings.ShowBalance = !settings.ShowBalance;
            settings.Save();
            RebuildFrames();
            Notify(settings.ShowBalance ? "已显示余额" : "已隐藏余额");
            if (settings.ShowBalance && !string.IsNullOrEmpty(settings.ApiKey)) StartBalanceFetch(false);
        }

        private void ToggleAutoStart()
        {
            bool enable = !AutoStart.IsEnabled();
            if (!AutoStart.SetEnabled(enable))
            {
                Notify("开机自启设置失败，可能没有权限");
                return;
            }

            Notify(enable ? "已开启开机自启" : "已关闭开机自启");
        }

        private void RepairAutoStart()
        {
            Notify("正在请求管理员权限…");
            if (AutoStart.RepairWithElevation())
            {
                Notify("已切换到任务计划启动，下次开机不再走 Run 键");
            }
            else
            {
                Notify("修复失败：需要管理员权限，或被系统策略拦截");
            }
            UpdateMenuState();
        }

        private void ChangeApiKey()
        {
            using (ApiKeyDialog dialog = new ApiKeyDialog(settings.ApiKey))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                settings.ApiKey = dialog.ApiKey;
                settings.Save();

                previousTotal = null;
                previousCurrency = "";
                currentBalance = null;

                if (string.IsNullOrEmpty(settings.ApiKey))
                {
                    balanceText = "余额未设置";
                    balanceState = 3;
                    stepActive = false;
                    shakeStart = -1;
                    shakeDeduction = false;
                    
                    pendingBubbleText = null;
                    pendingBubbleAlert = false;
                    displayTotal = 0m;
                    forceRender = true;
                    Render();
                    Notify("API Key 已清除");
                    return;
                }

                Notify("API Key 已保存，正在查询余额…");
                StartBalanceFetch(true);
            }
        }

        private void ClearApiKey()
        {
            // invalidate any request that is still in flight for the old key
            fetchGeneration++;

            settings.ApiKey = "";
            settings.Save();

            currentBalance = null;
            previousTotal = null;
            previousCurrency = "";
            balanceText = "余额未设置";
            balanceState = 3;
            fetchingBalance = false;
            stepActive = false;
            shakeStart = -1;
            shakeDeduction = false;
            
            pendingBubbleText = null;
            pendingBubbleAlert = false;
            displayTotal = 0m;

            bubble.Show("已清除 API Key。");
            forceRender = true;
            Render();
        }

        private void ScheduleBalanceRefresh()
        {
            if (!settings.ShowBalance || fetchingBalance) return;
            if (string.IsNullOrEmpty(settings.ApiKey)) return;
            if (lastFetchAttempt == DateTime.MinValue) return;
            if ((DateTime.UtcNow - lastFetchAttempt).TotalSeconds < settings.RefreshSeconds) return;
            StartBalanceFetch(false);
        }

        private void StartBalanceFetch(bool manual)
        {
            if (fetchingBalance) return;

            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                if (manual)
                {
                    bubble.Show("还没有设置 API Key：右键 → 设置 API Key…");
                    forceRender = true;
                    Render();
                }
                return;
            }

            fetchingBalance = true;
            lastFetchAttempt = DateTime.UtcNow;
            int generation = ++fetchGeneration;

            if (manual)
            {
                stepActive = false;
                shakeStart = -1;
                shakeDeduction = false;
                
                pendingBubbleText = null;
                pendingBubbleAlert = false;
                balanceText = "余额查询中…";
                balanceState = 2;
                forceRender = true;
                Render();
            }

            string key = settings.ApiKey;
            string baseUrl = settings.ApiBase;

            ThreadPool.QueueUserWorkItem(delegate
            {
                BalanceResult result = DeepSeekBalance.Fetch(key, baseUrl, 10000);
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(delegate { OnBalanceResult(result, manual, generation); }));
                }
                catch
                {
                }
            });
        }

        private void OnBalanceResult(BalanceResult result, bool manual, int generation)
        {
            if (generation != fetchGeneration) return;
            fetchingBalance = false;

            if (result == null || !result.Ok)
            {
                balanceState = 1;
                stepActive = false;
                shakeStart = -1;
                shakeDeduction = false;
                
                pendingBubbleAlert = false;
                if (currentBalance == null)
                {
                    balanceText = "余额查询失败";
                }
                else
                {
                    displayTotal = currentBalance.Total;
                    balanceText = FormatBalance(currentBalance.Symbol, currentBalance.Total);
                }

                if (manual)
                {
                    bubble.Show("查余额失败：" + (result == null ? "未知错误" : result.Error));
                }

                forceRender = true;
                Render();
                return;
            }

            currentBalance = result.Primary;
            balanceState = 0;

            bool changed = previousTotal.HasValue &&
                           (previousTotal.Value != currentBalance.Total || previousCurrency != currentBalance.Currency);
            decimal previous = previousTotal.HasValue ? previousTotal.Value : 0m;
            bool deducted = changed && currentBalance.Total < previous;

            previousTotal = currentBalance.Total;
            previousCurrency = currentBalance.Currency;

            if (!changed)
            {
                shakeDeduction = false;
                
                SetBalanceInstant(currentBalance);
                if (manual) bubble.Show("当前余额 " + currentBalance.Money);
            }
            else
            {
                // a visible speech bubble hides the chip, which would swallow the
                // whole jump animation; clear it so the counter can be seen
                bubble.Hide();
                pendingBubbleText = null;
                pendingBubbleAlert = false;

                shakeDeduction = deducted;


                int duration = StartBalanceSteps(currentBalance);

                string delta = Math.Abs(currentBalance.Total - previous).ToString("0.00", CultureInfo.InvariantCulture);
                // 扣费不再弹任何“消耗了多少”的气泡，只保留胶囊红边/抖动
                string message = deducted
                    ? null
                    : "余额增加 " + currentBalance.Symbol + delta + "（当前 " + currentBalance.Money + "）";

                if (!deducted && duration > 0 && settings.ShowBalance)
                {
                    // let the digits jump first, then pop the bubble
                    pendingBubbleText = message;
                    pendingBubbleAlert = false;
                    pendingBubbleDue = Environment.TickCount + duration + 250;
                }
                else if (!deducted)
                {
                    if (duration == 0 && settings.BalanceSound) Chime.PlayHit(hitSound, settings.BalanceVolume);
                    bubble.Show(message);
                }
                else if (duration == 0 && settings.BalanceSound)
                {
                    Chime.PlayHit(hitSound, settings.BalanceVolume);
                }
            }

            forceRender = true;
            Render();
        }

        private static string FormatBalance(string symbol, decimal value)
        {
            return "余额 " + symbol + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void SetBalanceInstant(BalanceInfo info)
        {
            stepActive = false;
            shakeStart = -1;
            shakeDeduction = false;
            displayTotal = info.Total;
            balanceText = FormatBalance(info.Symbol, info.Total);
        }

        /// <summary>
        /// Kicks off the stepped counter and returns how long the jumps will run
        /// (0 means the change was too small to animate).
        /// </summary>
        private int StartBalanceSteps(BalanceInfo info)
        {
            int interval = settings.TickIntervalMs;
            if (interval < 80) interval = 80;
            if (interval > 2000) interval = 2000;

            // keep the whole animation around 2.4s, but let the chosen interval
            // decide how many jumps there are
            int steps = (int)Math.Round((double)BalanceStepWindowMs / interval);
            if (steps < 3) steps = 3;
            if (steps > 16) steps = 16;

            decimal delta = info.Total - displayTotal;
            if (Math.Abs(delta) < 0.02m)
            {
                SetBalanceInstant(info);
                return 0;
            }

            stepFrom = displayTotal;
            stepTo = info.Total;
            stepSymbol = info.Symbol;
            stepIndex = 0;
            stepCount = steps;
            stepInterval = interval;
            stepActive = true;
            stepNextAt = Environment.TickCount + interval;
            shakeStart = Environment.TickCount;

            NextBalanceStep();   // first jump immediately, with its blip
            return steps * interval;
        }

        private void NextBalanceStep()
        {
            stepIndex++;
            if (stepIndex >= stepCount)
            {
                stepIndex = stepCount;
                displayTotal = stepTo;
                stepActive = false;
            }
            else
            {
                displayTotal = stepFrom + (stepTo - stepFrom) * stepIndex / stepCount;
            }

            balanceText = FormatBalance(stepSymbol, displayTotal);
            shakeStart = Environment.TickCount;
            TriggerClickVisual();
            if (settings.BalanceSound) Chime.PlayHit(hitSound, settings.BalanceVolume);
            forceRender = true;
        }

        private int ShakeOffsetY()
        {
            if (shakeStart < 0) return 0;

            int elapsed = Environment.TickCount - shakeStart;
            if (elapsed < 0) elapsed = 0;
            if (elapsed >= BalanceShakeMs)
            {
                shakeStart = -1;
                shakeDeduction = false;
                return 0;
            }

            double t = (double)elapsed / BalanceShakeMs;
            double damp = 1.0 - t;
            double amplitude = shakeDeduction ? 3.0 : 2.0;
            return (int)Math.Round(Math.Sin(t * Math.PI * 6.0) * amplitude * damp);
        }

        private void UpdateBalanceSteps()
        {
            if (!stepActive) return;
            if (Environment.TickCount < stepNextAt) return;

            stepNextAt += stepInterval;
            NextBalanceStep();
        }

        private void ApplyScale(int scale)
        {
            settings.Scale = scale;
            settings.Save();
            RebuildFrames();
            ClampToScreen();
            SavePosition();
            Notify(scale == 0 ? "大小：自动" : "大小：" + scale + "×");
        }

        private void PickGif()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择一只桌宠 GIF";
                dialog.Filter = "GIF 动画 (*.gif)|*.gif|所有文件 (*.*)|*.*";
                if (!string.IsNullOrEmpty(settings.GifPath))
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(settings.GifPath);
                        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
                    }
                    catch
                    {
                    }
                }

                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!TryLoad(dialog.FileName)) return;

                settings.Save();
                ClampToScreen();
                SavePosition();
                forceRender = true;
                Render();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePosition();
            timer.Stop();
            StopMenuWatch();
            DisposeAnimations();
            if (canvas != null) canvas.Dispose();
            if (bubbleFont != null) bubbleFont.Dispose();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------- playback

        private sealed class Playback
        {
            public readonly Bitmap[] Frames;
            public readonly int[] Delays;
            public readonly bool OneShot;
            public int Index;
            public double Elapsed;
            public bool Finished;

            public Playback(Bitmap[] frames, int[] delays, bool oneShot)
            {
                Frames = frames;
                Delays = delays;
                OneShot = oneShot;
            }

            public Bitmap Current
            {
                get
                {
                    if (Frames == null || Frames.Length == 0) return null;
                    return Frames[Index];
                }
            }

            public bool Advance(double delta)
            {
                if (Frames == null || Frames.Length == 0 || Finished) return false;

                bool changed = false;
                Elapsed += delta;

                while (Elapsed >= Delays[Index])
                {
                    Elapsed -= Delays[Index];

                    if (OneShot && Index == Frames.Length - 1)
                    {
                        Finished = true;
                        Elapsed = 0;
                        break;
                    }

                    Index = (Index + 1) % Frames.Length;
                    changed = true;
                }
                return changed;
            }
        }
    }
}
