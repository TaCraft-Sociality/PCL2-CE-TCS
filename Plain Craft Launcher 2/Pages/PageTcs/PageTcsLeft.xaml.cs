using System.Windows;
using System.Windows.Input;

namespace PCL;

/// <summary>
///     社区 / 皮肤站页面的左侧栏：提供内置浏览器的常用操作。
/// </summary>
public partial class PageTcsLeft
{
    private readonly PageTcsBrowser? _browser;

    /// <summary>
    ///     创建一个内置浏览器页面的左侧栏。
    /// </summary>
    /// <param name="displayName">左侧栏顶部显示的站点名称。</param>
    /// <param name="browser">与之关联的内置浏览器页面，可为空。</param>
    public PageTcsLeft(string displayName, PageTcsBrowser? browser)
    {
        _browser = browser;
        InitializeComponent();
        LabCategory.Text = displayName;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (_browser is null)
            return;
        switch ((sender as MyListItem)?.Tag?.ToString())
        {
            case "home":
                _browser.GoHome();
                break;
            case "refresh":
                _browser.Refresh();
                break;
            case "back":
                _browser.GoBack();
                break;
            case "forward":
                _browser.GoForward();
                break;
            case "external":
                _browser.OpenExternal();
                break;
        }
    }
}
