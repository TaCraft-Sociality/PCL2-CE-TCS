using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL
{
    /// <summary>TCS：按主题给小型图标块上色（深色主题统一 #A8A8A8 底）</summary>
    public static class ModTcsTheme
    {
        public static void ApplyIconTiles(FrameworkElement root)
        {
            try
            {
                var isDark = false;
                if (Application.Current.TryFindResource("ColorBrush1") is SolidColorBrush fg)
                {
                    var c = fg.Color;
                    isDark = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;
                }
                if (!isDark) return;
                Walk(root, 0);
            }
            catch { }
        }


        /// <summary>TCS：深色主题下把白底容器（房间卡、日志框、输入框）压暗</summary>
        public static void ApplySurfaceColors(FrameworkElement root)
        {
            try
            {
                if (!IsDark()) return;
                WalkSurface(root, 0, IsDark());
            }
            catch { }
        }

        private static bool IsDark()
        {
            if (Application.Current.TryFindResource("ColorBrush1") is SolidColorBrush fg)
            {
                var c = fg.Color;
                return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;
            }
            return false;
        }

        private static void WalkSurface(DependencyObject root, int depth, bool dark)
        {
            if (depth > 40) return;
            var n = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border b && b.Background is SolidColorBrush bc && bc.Color.R > 230 && bc.Color.G > 230 && bc.Color.B > 230)
                    b.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2C, 0x30));
                if (child is TextBox tb && tb.Background is SolidColorBrush tc && tc.Color.R > 230 && tc.Color.G > 230 && tc.Color.B > 230)
                {
                    tb.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x20));
                    tb.Foreground = new SolidColorBrush(Colors.White);
                }
                WalkSurface(child, depth + 1, dark);
            }
        }
        private static void Walk(DependencyObject root, int depth)
        {
            if (depth > 40) return;
            var n = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border b && b.Width >= 20 && b.Width <= 44 && Math.Abs(b.Width - b.Height) < 1.5)
                {
                    b.Background = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8));
                }
                Walk(child, depth + 1);
            }
        }
    }
}
