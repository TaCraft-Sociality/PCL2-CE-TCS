using System;
using System.Windows;
using System.Windows.Controls;

namespace PCL;

/// <summary>多人游戏右侧宿主：自己在「服务器 / 联机房间 / 陶瓦详情页」之间切换。</summary>
public partial class PageMultiplayerHost
{
    /// <summary>当前子页：0 = 服务器，1 = 联机房间（选择方式），2 = 陶瓦联机详情。</summary>
    public int Current;

    private PageMultiplayerServers _servers;
    private PageMultiplayerRooms _cards;
    private PageMultiplayerTerracotta _terraPage;
    private PageMultiplayerVoxLink _voxPage;
    private PageMultiplayerFrp _frpPage;
    private PageTcsBrowser _terraUi;

    public PageMultiplayerHost()
    {
        InitializeComponent();
        SwitchTo(0);
    }

    public void SwitchTo(int index)
    {
        Current = index;
        PanRoot.Children.Clear();
        FrameworkElement page;
        switch (index)
        {
            case 2:
                _terraPage ??= new PageMultiplayerTerracotta();
                page = _terraPage;
                break;
            case 3:
                if (_terraUi == null)
                {
                    _terraUi = new PageTcsBrowser(ModTerracotta.PrepareHintPage(), "陶瓦房间界面", showToolbar: false);
                    _terraUi.InjectedCss = PageTcsLinkLeft.BuildSkinCss();
                }
                if (!string.IsNullOrWhiteSpace(ModTerracotta.UiUrl))
                    _terraUi.NavigateTo(ModTerracotta.UiUrl);
                page = _terraUi;
                break;            case 5:
                _frpPage ??= new PageMultiplayerFrp();
                page = _frpPage;
                break;
            case 4:
                _voxPage ??= new PageMultiplayerVoxLink();
                page = _voxPage;
                break;
            case 1:
                _cards ??= new PageMultiplayerRooms();
                page = _cards;
                break;
            default:
                _servers ??= new PageMultiplayerServers();
                _servers.Refresh();
                page = _servers;
                break;
        }
        PanRoot.Children.Add(page);
        ModMain.frmMultiplayerLeft?.SyncSelection(index);
    }
}
