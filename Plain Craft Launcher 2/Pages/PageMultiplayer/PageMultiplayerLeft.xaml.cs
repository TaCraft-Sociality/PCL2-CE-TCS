using System.Windows.Input;

namespace PCL;

/// <summary>多人游戏页左侧栏：只有「服务器」与「联机房间」两个入口。</summary>
public partial class PageMultiplayerLeft
{
    public PageMultiplayerLeft()
    {
        InitializeComponent();
        ModServerList.EnsureLoaded();
        SyncSelection(ModMain.frmMultiplayerHost?.Current ?? 0);
    }

    /// <summary>按当前子页同步两个入口的高亮（陶瓦界面算作「联机房间」）。</summary>
    public void SyncSelection(int index)
    {
        ItemServers.Checked = index == 0;
        ItemRooms.Checked = index != 0;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        var target = (sender as MyListItem)?.Tag?.ToString() == "rooms" ? 1 : 0;
        ModMain.frmMain.PageChange(FormMain.PageType.Link);
        ModMain.frmMultiplayerHost?.SwitchTo(target);
    }
}
