using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EdgeBar.Models;

namespace EdgeBar.Services;

/// <summary>Gitee 仓库手动同步：上传 / 下拉，不做自动同步</summary>
public static class GiteeSyncService
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeBar/2.2");
        return client;
    }

    private static string BaseUrl(AppSettings s)
        => $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(s.GiteeOwner)}/{Uri.EscapeDataString(s.GiteeRepo)}/contents/{s.GiteePath.TrimStart('/')}";

    private static void Validate(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.GiteeOwner) || string.IsNullOrWhiteSpace(s.GiteeRepo))
            throw new InvalidOperationException("请先填写仓库所有者和仓库名");
        if (string.IsNullOrWhiteSpace(s.GiteeToken))
            throw new InvalidOperationException("请先填写 Gitee 私人令牌");
        if (string.IsNullOrWhiteSpace(s.GiteePath))
            throw new InvalidOperationException("请填写云端文件路径，例如 edgebar/data.json");
    }

    private sealed class RemoteFile
    {
        public bool Exists;
        public string Sha;
    }

    /// <summary>查询云端的文件；空仓库或文件不存在时 Gitee 会返回 200 + []</summary>
    private static async Task<RemoteFile> GetRemoteFileAsync(AppSettings s)
    {
        string url = $"{BaseUrl(s)}?access_token={Uri.EscapeDataString(s.GiteeToken)}&ref={Uri.EscapeDataString(s.GiteeBranch)}";
        using var get = await Http.GetAsync(url);
        string body = await get.Content.ReadAsStringAsync();

        if (get.StatusCode == HttpStatusCode.NotFound)
            return new RemoteFile();
        if (!get.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe("读取", get.StatusCode, body));

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("sha", out var sha))
        {
            return new RemoteFile { Exists = true, Sha = sha.GetString() };
        }

        return new RemoteFile();
    }

    /// <summary>确定要提交的分支：空仓库不传分支，让 Gitee 建默认分支</summary>
    private static async Task<string> GetTargetBranchAsync(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.GiteeBranch)) return null;

        string url = $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(s.GiteeOwner)}/{Uri.EscapeDataString(s.GiteeRepo)}/branches?access_token={Uri.EscapeDataString(s.GiteeToken)}";
        using var get = await Http.GetAsync(url);
        if (!get.IsSuccessStatusCode) return s.GiteeBranch;

        using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return s.GiteeBranch;

        var names = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var name))
                names.Add(name.GetString() ?? "");
        }

        if (names.Count == 0) return null;
        if (names.Any(n => string.Equals(n, s.GiteeBranch, StringComparison.OrdinalIgnoreCase)))
            return s.GiteeBranch;

        throw new InvalidOperationException($"分支「{s.GiteeBranch}」不存在，现有分支：{string.Join("、", names)}");
    }

    public static async Task<string> UploadAsync(AppSettings s)
    {
        Validate(s);

        // 私人令牌只留本机，绝不写进云端文件
        string json = SanitizeForUpload(StorageService.Serialize(App.State));
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var remote = await GetRemoteFileAsync(s);
        string branch = await GetTargetBranchAsync(s);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("access_token", s.GiteeToken),
            new("content", content),
            new("message", $"EdgeBar 数据备份 {DateTime.Now:yyyy-MM-dd HH:mm}")
        };
        if (remote.Exists && !string.IsNullOrEmpty(remote.Sha))
            fields.Add(new("sha", remote.Sha));
        if (!string.IsNullOrEmpty(branch))
            fields.Add(new("branch", branch));

        // 新建文件用 POST，更新已有文件用 PUT
        using var request = new HttpRequestMessage(remote.Exists ? HttpMethod.Put : HttpMethod.Post, BaseUrl(s))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await Http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(Describe(remote.Exists ? "上传" : "创建", response.StatusCode, body));
        }

        return $"已上传 {DateTime.Now:HH:mm} · {json.Length / 1024.0:0.#} KB";
    }

    public static async Task<string> PullAsync(AppSettings s)
    {
        Validate(s);

        string url = $"{BaseUrl(s)}?access_token={Uri.EscapeDataString(s.GiteeToken)}&ref={Uri.EscapeDataString(s.GiteeBranch)}";
        using var get = await Http.GetAsync(url);
        string body = await get.Content.ReadAsStringAsync();

        if (!get.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe("读取", get.StatusCode, body));

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("content", out var contentElement))
        {
            throw new InvalidOperationException("云端还没有备份文件，请先点「上传数据」");
        }

        string base64 = contentElement.GetString()?.Replace("\n", "").Replace("\r", "") ?? "";
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

        // 云端数据不包含令牌，导入时保留本机的同步配置
        if (!StorageService.Import(ApplyLocalGiteeSettings(json, s), out string error))
            throw new InvalidOperationException(error ?? "导入失败");

        return $"已下拉并写入本地 {DateTime.Now:HH:mm}";
    }

    private static string SanitizeForUpload(string json)
    {
        var node = JsonNode.Parse(json);
        if (node?["Settings"] is JsonObject settings)
            settings["GiteeToken"] = "";
        return node?.ToJsonString(SyncJsonOptions) ?? json;
    }

    private static string ApplyLocalGiteeSettings(string json, AppSettings local)
    {
        var node = JsonNode.Parse(json);
        if (node?["Settings"] is JsonObject settings)
        {
            settings["GiteeOwner"] = local.GiteeOwner;
            settings["GiteeRepo"] = local.GiteeRepo;
            settings["GiteeToken"] = local.GiteeToken;
            settings["GiteeBranch"] = local.GiteeBranch;
            settings["GiteePath"] = local.GiteePath;
        }
        return node?.ToJsonString(SyncJsonOptions) ?? json;
    }

    private static string Describe(string action, HttpStatusCode status, string body)
    {
        string hint = status switch
        {
            HttpStatusCode.Unauthorized => "令牌无效或已过期",
            HttpStatusCode.Forbidden => "令牌没有仓库读写权限",
            HttpStatusCode.NotFound => "仓库或分支不存在",
            HttpStatusCode.BadRequest => "请求被拒绝，请检查仓库、分支和令牌权限",
            _ => "请求失败"
        };

        string detail = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var message))
            {
                detail = message.GetString() ?? "";
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(body) && !body.TrimStart().StartsWith("<"))
                detail = Trim(body);
        }

        return $"{action}失败（{(int)status}）：{hint}" + (string.IsNullOrEmpty(detail) ? "" : $" · {detail}");
    }

    private static string Trim(string text)
        => string.IsNullOrWhiteSpace(text) ? "无返回内容" : text.Length > 120 ? text[..120] : text;
}
