using System;
using System.Windows;
using System.Windows.Input;

namespace PCL;

/// <summary>多人游戏 → 联机房间：选择联机方式（FRP / VoxLink / 陶瓦）。</summary>
public partial class PageMultiplayerRooms
{

    // TCS：图标块按主题切换配色（浅色=浅蓝底蓝图案，深色=深蓝底亮蓝图案）
    private void ApplyCardTheme()
    {
        try
        {
            var isDark = false;
            if (System.Windows.Application.Current.TryFindResource("ColorBrush1") is System.Windows.Media.SolidColorBrush fg)
            {
                var cc = fg.Color;
                isDark = (0.299 * cc.R + 0.587 * cc.G + 0.114 * cc.B) > 128;
            }
            var tileBg = isDark ? System.Windows.Media.Color.FromRgb(0xA8, 0xA8, 0xA8) : System.Windows.Media.Colors.White;
            var glyphFg = System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xE0);
            var pillBg = isDark ? System.Windows.Media.Color.FromRgb(0x3A, 0x3F, 0x46) : System.Windows.Media.Color.FromRgb(0xED, 0xF1, 0xF7);
            var pillFg = isDark ? System.Windows.Media.Color.FromRgb(0xC9, 0xCE, 0xD6) : System.Windows.Media.Color.FromRgb(0x5A, 0x64, 0x72);
            foreach (var pill in new System.Windows.Controls.Border[] { PillFrp, PillVox, PillTerra })
            {
                pill.Background = new System.Windows.Media.SolidColorBrush(pillBg);
                ApplyGlyphColor(pill, pillFg);
            }

            foreach (var tile in new System.Windows.Controls.Border[] { TileFrp, TileVox, TileTerra })
            {
                tile.Background = new System.Windows.Media.SolidColorBrush(tileBg);
                ApplyGlyphColor(tile, glyphFg);
            }
        }
        catch { }
    }

    private static void ApplyCardThemeTo(System.Windows.DependencyObject root, System.Windows.Media.Color tileBg, System.Windows.Media.Color glyphFg)
    {
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Border b && Math.Abs(b.Width - 34) < 0.5 && b.Height > 30)
            {
                b.Background = new System.Windows.Media.SolidColorBrush(tileBg);
                ApplyGlyphColor(b, glyphFg);
            }
            ApplyCardThemeTo(child, tileBg, glyphFg);
        }
    }

    private static void ApplyGlyphColor(System.Windows.DependencyObject root, System.Windows.Media.Color fg)
    {
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock tb) tb.Foreground = new System.Windows.Media.SolidColorBrush(fg);
            if (child is System.Windows.Shapes.Path pth) pth.Fill = new System.Windows.Media.SolidColorBrush(fg);
            if (child is System.Windows.Controls.Control ctl) ctl.Foreground = new System.Windows.Media.SolidColorBrush(fg);
            ApplyGlyphColor(child, fg);
        }
    }
    public PageMultiplayerRooms()
    {
        InitializeComponent();
            Loaded += (_, _) => ApplyCardTheme(); // TCS：进入页面时按当前主题给图标块上色
    }

    private async void Card_Click(object sender, MouseButtonEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        switch (tag)
        {
            case "terra":
                ModMain.frmMultiplayerHost?.SwitchTo(2);
                return;
                return;
            case "frp":
                ModMain.frmMultiplayerHost?.SwitchTo(5);
                return;
            case "vox":
                ModMain.frmMultiplayerHost?.SwitchTo(4);
                return;
        }
    }
}
