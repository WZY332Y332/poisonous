// Copyright (c) Richasy. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bili.Models.App.Other;
using Newtonsoft.Json.Linq;

namespace Bili.SignIn.Uwp.ModernAuth
{
    /// <summary>
    /// 重写后的 B 站认证核心。所有接口与参数均已实测通过，要点如下：
    /// 1. 二维码登录必须走 passport.bilibili.com，且请求不能带 build（旧值会被判「版本过低」）；
    /// 2. 轮询必须复用同一个 guid / local_id，不能每次重新生成；
    /// 3. 写操作（点赞/投币/收藏）只需要 Cookie 中的 bili_jct 作为 csrf；
    /// 4. WinRT 的 CookieManager 不可靠，鉴权统一用缓存的 Cookie 字符串拼请求头。
    /// </summary>
    internal static class ModernAuthService
    {
        private const string PassportBase = "https://passport.bilibili.com";
        private const string QrCodeApi = PassportBase + "/x/passport-tv-login/qrcode/auth_code";
        private const string QrPollApi = PassportBase + "/x/passport-tv-login/qrcode/poll";
        private const string NavApi = "https://api.bilibili.com/x/web-interface/nav";
        private const string AppKey = "27eb53fc9058f8c3";
        private const string AppSecret = "c2ed53a74eeefe3cf99fbd01d8c9c375";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

        /// <summary>
        /// 申请登录二维码。
        /// </summary>
        /// <param name="localId">本次登录会话的本地标识，轮询时必须复用同一个值。</param>
        /// <returns>包含 url 与 auth_code 的响应数据。</returns>
        internal static async Task<JObject> RequestQrCodeAsync(string localId)
        {
            var parameters = new Dictionary<string, string>
            {
                { "appkey", AppKey },
                { "local_id", localId },
                { "ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
            };

            var data = await PostFormAsync(QrCodeApi, parameters, null);
            AppLogger.Info("ModernAuth", $"申请二维码: code={data["code"]} auth_code={data["data"]?["auth_code"]}");
            return data;
        }

        /// <summary>
        /// 轮询二维码状态。
        /// </summary>
        /// <param name="authCode">申请得到的 auth_code。</param>
        /// <param name="localId">与申请时相同的 local_id。</param>
        /// <returns>包含 code 与 data 的响应。</returns>
        internal static async Task<JObject> PollQrAsync(string authCode, string localId)
        {
            var parameters = new Dictionary<string, string>
            {
                { "appkey", AppKey },
                { "auth_code", authCode },
                { "local_id", localId },
                { "guid", localId },
                { "ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
            };

            return await PostFormAsync(QrPollApi, parameters, null);
        }

        /// <summary>
        /// 用 Cookie 校验登录并取回用户信息。
        /// </summary>
        /// <param name="cookieString">形如 SESSDATA=...; bili_jct=...; DedeUserID=... 的 Cookie。</param>
        /// <returns>nav 接口的完整响应。</returns>
        internal static async Task<JObject> ValidateCookieAsync(string cookieString)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                client.DefaultRequestHeaders.Add("Cookie", cookieString);
                var text = await client.GetStringAsync(NavApi);
                var data = JObject.Parse(text);
                AppLogger.Info("ModernAuth", $"Cookie 校验: code={data["code"]} isLogin={data["data"]?["isLogin"]}");
                return data;
            }
        }

        /// <summary>
        /// 把浏览器 Cookie 文本解析成键值对，保留原始值（SESSDATA 含 URL 编码，不能解码）。
        /// </summary>
        /// <param name="rawCookie">整段 Cookie 文本。</param>
        /// <returns>Cookie 键值对集合。</returns>
        internal static Dictionary<string, string> ParseCookie(string rawCookie)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(rawCookie))
            {
                return result;
            }

            foreach (var segment in rawCookie.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = segment.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var name = segment.Substring(0, index).Trim();
                var value = segment.Substring(index + 1).Trim();
                if (name.Length > 0)
                {
                    result[name] = value;
                }
            }

            return result;
        }

        private static async Task<JObject> PostFormAsync(string url, Dictionary<string, string> parameters, string cookieString)
        {
            var query = string.Join("&", parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
            parameters["sign"] = ComputeSign(query);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                if (!string.IsNullOrEmpty(cookieString))
                {
                    client.DefaultRequestHeaders.Add("Cookie", cookieString);
                }

                var content = new FormUrlEncodedContent(parameters);
                var response = await client.PostAsync(url, content);
                var text = await response.Content.ReadAsStringAsync();
                return JObject.Parse(text);
            }
        }

        private static string ComputeSign(string query)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(query + AppSecret));
                return string.Concat(bytes.Select(b => b.ToString("x2")));
            }
        }
    }
}
