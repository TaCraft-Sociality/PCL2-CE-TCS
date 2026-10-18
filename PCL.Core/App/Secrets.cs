using System.IO;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.OS;

namespace PCL.Core.App;

// ReSharper disable InconsistentNaming
public static class Secrets
{
    /// <summary>
    ///     微软 OAuth 的 Client ID。
    ///     TCS 定制：按以下顺序取用（都不需要重新编译）：
    ///     1. 环境变量 <c>PCL_MS_CLIENT_ID</c>（Release 也读取）
    ///     2. 启动器 exe 同目录下的 <c>ms-client-id.txt</c>（只写一行 client id 即可）
    ///     3. 内置默认值（TCS 发布版开箱即可使用正版登录）
    /// </summary>
    public static string MSOAuthClientId { get; } = _ResolveMsOAuthClientId();

    /// <summary>TCS 发布版内置的微软登录 Client ID（可用上面两种方式覆盖）。</summary>
    private const string BuiltInMsOAuthClientId = "c14b0370-8d75-42f8-b329-5b60d39e319f";

    private static string _ResolveMsOAuthClientId()
    {
        var fromSecret = EnvironmentInterop.GetSecret("MS_CLIENT_ID").ReplaceNullOrEmpty();
        if (!string.IsNullOrWhiteSpace(fromSecret)) return fromSecret;
        try
        {
            var path = Path.Combine(System.AppContext.BaseDirectory, "ms-client-id.txt");
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                if (text.Length > 0) return text;
            }
        }
        catch
        {
            // 读取失败时继续使用内置默认值
        }
        return BuiltInMsOAuthClientId;
    }

    /// <summary>
    /// CurseForge API 的 Client ID
    /// </summary>
    public static string CurseForgeAPIKey { get; } = EnvironmentInterop.GetSecret("CURSEFORGE_API_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 遥测密钥
    /// </summary>
    public static string TelemetryKey { get; } = EnvironmentInterop.GetSecret("TELEMETRY_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientId { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_ID", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientSecret { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_SECRET", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 联机根服务器
    /// </summary>
    public static string[] LinkServers { get; } = EnvironmentInterop.GetSecret("LINK_SERVER_ROOT", readEnvDebugOnly: true).ReplaceNullOrEmpty().Split("|");

    /// <summary>
    /// 当前版本的 Git 提交 SHA
    /// </summary>
    public static string CommitHash { get; } = EnvironmentInterop.GetSecret("GITHUB_SHA", readEnvDebugOnly: true).ReplaceNullOrEmpty();
}
