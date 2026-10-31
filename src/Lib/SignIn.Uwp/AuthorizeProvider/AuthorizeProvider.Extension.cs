// Copyright (c) Richasy. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Bili.DI.Container;
using Bili.Lib.Interfaces;
using Bili.Models.App.Constants;
using Bili.Models.App.Other;
using Bili.Models.BiliBili;
using Bili.Models.Enums;
using Bili.Toolkit.Interfaces;
using Newtonsoft.Json.Linq;
using QRCoder;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http.Filters;
using static Bili.Models.App.Constants.ApiConstants;
using static Bili.Models.App.Constants.ServiceConstants;

namespace Bili.SignIn.Uwp
{
    /// <summary>
    /// 授权模块的属性集及扩展.
    /// </summary>
    public partial class AuthorizeProvider
    {
        private readonly IMD5Toolkit _md5Toolkit;
        private readonly ISettingsToolkit _settingsToolkit;
        private readonly string _guid;
        private readonly bool _isXbox;
        private AuthorizeState _state;
        private TokenInfo _tokenInfo;
        private DateTimeOffset _lastAuthorizeTime;
        private string _internalQRAuthCode;
        private DispatcherTimer _qrTimer;
        private CancellationTokenSource _qrPollCancellationTokenSource;

        /// <summary>
        /// 是否处于「仅 Cookie」登录模式（无 access_token，鉴权依赖 Cookie）。
        /// </summary>
        private bool _cookieOnlyMode;

        /// <summary>
        /// 仅 Cookie 登录模式下，本地记录的用户 Id 键名。
        /// </summary>
        private const string CookieOnlyMidKey = "CookieOnlyMid";

        internal async Task<AuthorizeResult> InternalLoginAsync(string userName, string password, int geeType = 10, Dictionary<string, string> additionalParams = null)
        {
            var encryptedPwd = await EncryptedPasswordAsync(password);
            var queryParameters = new Dictionary<string, string>
            {
                { Query.UserName, Uri.EscapeDataString(userName) },
                { Query.Password, Uri.EscapeDataString(encryptedPwd) },
                { Query.GeeType, geeType.ToString() },
            };

            if (additionalParams != null)
            {
                foreach (var item in additionalParams)
                {
                    if (!queryParameters.ContainsKey(item.Key))
                    {
                        queryParameters.Add(item.Key, item.Value);
                    }
                }
            }

            var httpProvider = Locator.Instance.GetService<IHttpProvider>();
            var query = await GenerateAuthorizedQueryDictionaryAsync(queryParameters, RequestClientType.Login);
            query[Query.UserName] = userName;
            query[Query.Password] = encryptedPwd;
            var request = new HttpRequestMessage(HttpMethod.Post, Passport.Login);
            request.Content = new FormUrlEncodedContent(query);
            var response = await httpProvider.SendAsync(request);
            var result = await httpProvider.ParseAsync<ServerResponse<AuthorizeResult>>(response);
            await SSOInitAsync();
            return result.Data;
        }

        internal async Task<TokenInfo> InternalRefreshTokenAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tokenInfo?.RefreshToken))
                {
                    var queryParameters = new Dictionary<string, string>
                    {
                        { Query.AccessToken, _tokenInfo.AccessToken },
                        { Query.RefreshToken, _tokenInfo.RefreshToken },
                    };

                    var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                    var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Post, Passport.RefreshToken, queryParameters);
                    var response = await httpProvider.SendAsync(request);
                    var result = await httpProvider.ParseAsync<ServerResponse<TokenInfo>>(response);
                    await SSOInitAsync();

                    return result.Data;
                }
            }
            catch
            {
            }

            return null;
        }

        internal void GenerateAppKey(Dictionary<string, string> queryParameters, RequestClientType clientType, bool onlyAppKey = false)
        {
            if (clientType == RequestClientType.IOS)
            {
                queryParameters.Add(ServiceConstants.Query.AppKey, ServiceConstants.Keys.IOSKey);
                if (!onlyAppKey)
                {
                    queryParameters.Add(ServiceConstants.Query.MobileApp, "iphone");
                    queryParameters.Add(ServiceConstants.Query.Platform, "ios");
                    queryParameters.Add(ServiceConstants.Query.TimeStamp, GetNowSeconds().ToString());
                }
            }
            else if (clientType == RequestClientType.Web)
            {
                queryParameters.Add(ServiceConstants.Query.AppKey, ServiceConstants.Keys.WebKey);
                if (!onlyAppKey)
                {
                    queryParameters.Add(ServiceConstants.Query.Platform, "web");
                    queryParameters.Add(ServiceConstants.Query.TimeStamp, GetNowMilliSeconds().ToString());
                }
            }
            else if (clientType == RequestClientType.Login)
            {
                queryParameters.Add(ServiceConstants.Query.AppKey, ServiceConstants.Keys.LoginKey);
                if (!onlyAppKey)
                {
                    queryParameters.Add(ServiceConstants.Query.TimeStamp, GetNowMilliSeconds().ToString());
                }
            }
            else
            {
                queryParameters.Add(ServiceConstants.Query.AppKey, ServiceConstants.Keys.AndroidKey);
                if (!onlyAppKey)
                {
                    queryParameters.Add(ServiceConstants.Query.MobileApp, "android");
                    queryParameters.Add(ServiceConstants.Query.Platform, "android");
                    queryParameters.Add(ServiceConstants.Query.TimeStamp, GetNowSeconds().ToString());
                }
            }
        }

        internal string GenerateSign(Dictionary<string, string> queryParameters, RequestClientType clientType)
        {
            var queryList = queryParameters.Select(p => $"{p.Key}={p.Value}").ToList();
            queryList.Sort();

            var apiSecret = string.Empty;

            switch (clientType)
            {
                case RequestClientType.IOS:
                    apiSecret = Keys.IOSSecret;
                    break;
                case RequestClientType.Android:
                    apiSecret = Keys.AndroidSecret;
                    break;
                case RequestClientType.Login:
                    apiSecret = Keys.LoginSecret;
                    break;
                default:
                    apiSecret = Keys.WebSecret;
                    break;
            }

            var query = string.Join('&', queryList);
            var signQuery = query + apiSecret;
            var sign = _md5Toolkit.GetMd5String(signQuery).ToLower();
            return sign;
        }

        internal async Task<string> EncryptedPasswordAsync(string password)
        {
            string base64String;
            try
            {
                var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                var param = await GenerateAuthorizedQueryDictionaryAsync(null, RequestClientType.Android);
                var request = new HttpRequestMessage(HttpMethod.Post, Passport.PasswordEncrypt);
                request.Content = new FormUrlEncodedContent(param);
                var response = await httpProvider.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                var jobj = JObject.Parse(responseContent);
                var str = jobj["data"]["hash"].ToString();
                var str1 = jobj["data"]["key"].ToString();
                var str2 = string.Concat(str, password);
                var str3 = Regex.Match(str1, "BEGIN PUBLIC KEY-----(?<key>[\\s\\S]+)-----END PUBLIC KEY").Groups["key"].Value.Trim();
                var numArray = Convert.FromBase64String(str3);
                var asymmetricKeyAlgorithmProvider = AsymmetricKeyAlgorithmProvider.OpenAlgorithm(AsymmetricAlgorithmNames.RsaPkcs1);
                var cryptographicKey = asymmetricKeyAlgorithmProvider.ImportPublicKey(WindowsRuntimeBufferExtensions.AsBuffer(numArray), 0);
                var buffer = CryptographicEngine.Encrypt(cryptographicKey, WindowsRuntimeBufferExtensions.AsBuffer(System.Text.Encoding.UTF8.GetBytes(str2)), null);
                base64String = Convert.ToBase64String(WindowsRuntimeBufferExtensions.ToArray(buffer));
            }
            catch (Exception)
            {
                base64String = password;
            }

            return base64String;
        }

        internal async Task<string> GetConfirmUriAsync()
        {
            var url = "https://passport.bilibili.com/login/app/third?appkey=27eb53fc9058f8c3&api=http%3A%2F%2Flink.acg.tv%2Fforum.php&sign=67ec798004373253d60114caaad89a8c";

            try
            {
                using (var httpClient = new Windows.Web.Http.HttpClient())
                {
                    var result = await httpClient.GetStringAsync(new Uri(url));
                    var jobj = JObject.Parse(result);
                    if (Convert.ToInt32(jobj["code"].ToString()) == 0)
                    {
                        return jobj["data"]["confirm_uri"].ToString();
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        internal async Task<BitmapImage> GetQRImageAsync()
        {
            try
            {
                StopQRLoginListener();
                var queryParameters = new Dictionary<string, string>
                {
                    { Query.LocalId, _guid },
                };
                var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Post, Passport.QRCode, queryParameters, RequestClientType.Login);
                var response = await httpProvider.SendAsync(request);
                var result = await httpProvider.ParseAsync<ServerResponse<QRInfo>>(response);

                _internalQRAuthCode = result.Data.AuthCode;
                var generator = new QRCodeGenerator();
                var data = generator.CreateQrCode(result.Data.Url, QRCodeGenerator.ECCLevel.Q);
                var code = new BitmapByteQRCode(data);
                var image = code.GetGraphic(20);
                using (var stream = new InMemoryRandomAccessStream())
                {
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(image);
                        await writer.StoreAsync();
                    }

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 开始本地轮询二维码状态.
        /// </summary>
        internal void StartQRLoginListener()
        {
            if (_qrTimer == null)
            {
                _qrTimer = new DispatcherTimer();
                _qrTimer.Interval = TimeSpan.FromSeconds(3);
                _qrTimer.Tick += OnQRTimerTickAsync;
            }

            _qrTimer?.Start();
        }

        internal void StopQRLoginListener()
        {
            _qrTimer?.Stop();
            _qrTimer = null;
            CleanQRCodeCancellationToken();
        }

        private async void OnQRTimerTickAsync(object sender, object e)
        {
            if (await IsTokenValidAsync())
            {
                StopQRLoginListener();
                return;
            }

            CleanQRCodeCancellationToken();
            _qrPollCancellationTokenSource = new CancellationTokenSource();
            var queryParameters = new Dictionary<string, string>
            {
                { Query.AuthCode, _internalQRAuthCode },
                { Query.LocalId, _guid },
                { "guid", _guid },
            };

            try
            {
                var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Post, Passport.QRCodeCheck, queryParameters, RequestClientType.Login);
                var response = await httpProvider.SendAsync(request, _qrPollCancellationTokenSource.Token);
                var result = await httpProvider.ParseAsync<ServerResponse<TokenInfo>>(response);

                try
                {
                    AppLogger.Info(
                        "QR",
                        $"轮询成功: mid={result.Data?.Mid}, token长度={result.Data?.AccessToken?.Length ?? 0}, cookie条数={result.Data?.CookieInfo?.Cookies?.Count ?? 0}");

                    // 保存cookie
                    SaveCookie(result.Data.CookieInfo);
                    SaveAuthorizeResult(result.Data);
                    AppLogger.Info("QR", "保存登录信息完成，触发 Success 事件");
                    QRCodeStatusChanged?.Invoke(this, new Tuple<QRCodeStatus, TokenInfo>(QRCodeStatus.Success, result.Data));
                }
                catch (Exception ex)
                {
                    // 扫码已成功但本地保存失败：把真实原因暴露出去，不再静默显示「登录失败」
                    AppLogger.Error("QR", "保存登录信息失败", ex);
                    LastQRCodeError = $"保存登录信息失败: {ex.GetType().Name}: {ex.Message}";
                    QRCodeStatusChanged?.Invoke(this, new Tuple<QRCodeStatus, TokenInfo>(QRCodeStatus.Failed, null));
                }
            }
            catch (ServiceException se)
            {
                if (se.InnerException is TaskCanceledException)
                {
                    return;
                }

                if (se.Error != null)
                {
                    LastQRCodeError = $"服务端返回 {se.Error.Code}: {se.Error.Message}";
                    AppLogger.Warn("QR", $"轮询返回业务错误: code={se.Error.Code}, message={se.Error.Message}");

                    QRCodeStatus qrStatus = default;
                    if (se.Error.Code == 86039 || se.Error.Code == 86090)
                    {
                        qrStatus = QRCodeStatus.NotConfirm;
                    }
                    else if (se.Error.Code == 86038 || se.Error.Code == -3)
                    {
                        qrStatus = QRCodeStatus.Expiried;
                    }
                    else
                    {
                        qrStatus = QRCodeStatus.Failed;
                    }

                    QRCodeStatusChanged?.Invoke(this, new Tuple<QRCodeStatus, TokenInfo>(qrStatus, null));
                }
                else
                {
                    // 没有业务错误码（多为响应解析失败），同样要给出可读原因
                    AppLogger.Error("QR", "轮询异常（无业务错误码）", se);
                    LastQRCodeError = $"响应解析失败: {se.GetType().Name}: {se.Message}";
                    QRCodeStatusChanged?.Invoke(this, new Tuple<QRCodeStatus, TokenInfo>(QRCodeStatus.Failed, null));
                }
            }
        }

        private void CleanQRCodeCancellationToken()
        {
            if (_qrPollCancellationTokenSource != null)
            {
                if (_qrPollCancellationTokenSource.Token.CanBeCanceled)
                {
                    _qrPollCancellationTokenSource.Cancel();
                }

                _qrPollCancellationTokenSource.Dispose();
                _qrPollCancellationTokenSource = null;
            }
        }

        private async Task SSOInitAsync()
        {
            var url = Passport.SSO;
            var httpProvider = Locator.Instance.GetService<IHttpProvider>();
            var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Get, url);
            await httpProvider.SendAsync(request);
        }

        private async Task<bool> NetworkVerifyTokenAsync()
        {
            if (!string.IsNullOrEmpty(_tokenInfo?.AccessToken))
            {
                var queryParameters = new Dictionary<string, string>
                {
                    { Query.AccessToken, _tokenInfo.AccessToken },
                };

                try
                {
                    var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                    var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Get, Passport.CheckToken, queryParameters);
                    _ = await httpProvider.SendAsync(request);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        private async Task<AuthorizeResult> ShowAccountManagementPaneAndGetResultAsync()
        {
            var webAccountProviderTaskCompletionSource = new TaskCompletionSource<AuthorizeResult>();

            var dialog = new SignInDialog(webAccountProviderTaskCompletionSource, this);
            dialog.IsShowSwitchButton = !_isXbox;
            await dialog.ShowAsync();

            return await webAccountProviderTaskCompletionSource.Task;
        }

        private long GetNowSeconds()
        {
            return DateTimeOffset.Now.ToLocalTime().ToUnixTimeSeconds();
        }

        private long GetNowMilliSeconds()
        {
            return DateTimeOffset.Now.ToLocalTime().ToUnixTimeMilliseconds();
        }

        private void SaveAuthorizeResult(TokenInfo result)
        {
            if (result != null)
            {
                var saveTime = DateTimeOffset.Now;
                var compositeValue = new ApplicationDataCompositeValue
                {
                    [Settings.AccessTokenKey] = result.AccessToken,
                    [Settings.RefreshTokenKey] = result.RefreshToken ?? string.Empty,
                    [Settings.UserIdKey] = result.Mid,
                    [Settings.ExpiresInKey] = result.ExpiresIn,
                    [Settings.LastSaveAuthTimeKey] = saveTime.ToUnixTimeSeconds(),
                };

                CurrentUserId = result.Mid.ToString();
                ApplicationData.Current.LocalSettings.Values[Settings.AuthResultKey] = compositeValue;
                _lastAuthorizeTime = saveTime;
                _tokenInfo = result;
                State = AuthorizeState.SignedIn;
                AppLogger.Info("Auth", $"SaveAuthorizeResult 完成: mid={result.Mid}, token长度={result.AccessToken?.Length ?? 0}, 过期秒={result.ExpiresIn}");
            }
        }

        private void RetrieveAuthorizeResult()
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            // 先看是否处于「仅 Cookie」登录模式（手动粘贴 Cookie 登录）
            var cookieOnlyMid = localSettings.Values[CookieOnlyMidKey];
            if (cookieOnlyMid != null)
            {
                _cookieOnlyMode = true;
                CurrentUserId = cookieOnlyMid.ToString();
                _tokenInfo = null;
                _lastAuthorizeTime = DateTimeOffset.Now;
                State = AuthorizeState.SignedIn;
                AppLogger.Info("Auth", $"恢复仅 Cookie 登录态: mid={CurrentUserId}");
                return;
            }
            if (localSettings.Values.ContainsKey(Settings.AuthResultKey))
            {
                var data = (ApplicationDataCompositeValue)localSettings.Values[Settings.AuthResultKey];
                var tokenInfo = new TokenInfo
                {
                    AccessToken = data[Settings.AccessTokenKey].ToString(),
                    RefreshToken = data[Settings.RefreshTokenKey].ToString(),
                    Mid = Convert.ToInt64(data[Settings.UserIdKey]),
                    ExpiresIn = (int)data[Settings.ExpiresInKey],
                };

                CurrentUserId = tokenInfo.Mid.ToString();
                _tokenInfo = tokenInfo;
                _lastAuthorizeTime = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(data[Settings.LastSaveAuthTimeKey]));
                State = AuthorizeState.SignedIn;
            }
            else
            {
                _tokenInfo = null;
                _lastAuthorizeTime = default;
            }
        }

        /// <summary>
        /// 使用浏览器中已有的 Cookie 登录（无需扫码）。成功后进入「仅 Cookie」模式。
        /// </summary>
        /// <param name="rawCookie">形如 SESSDATA=...; bili_jct=...; DedeUserID=... 的整段 Cookie。</param>
        /// <returns>是否登录成功。</returns>
        internal async Task<bool> SignInWithCookieAsync(string rawCookie)
        {
            if (string.IsNullOrWhiteSpace(rawCookie))
            {
                throw new ArgumentException("Cookie 内容为空");
            }

            var cookies = new List<Cookies>();
            foreach (var segment in rawCookie.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = segment.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var name = segment.Substring(0, index).Trim();
                var value = segment.Substring(index + 1).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                cookies.Add(new Cookies { Name = name, Value = value, HttpOnly = 0, Secure = 0, Expires = 0 });
            }

            if (!cookies.Any(c => c.Name.Equals("SESSDATA", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Cookie 中缺少 SESSDATA，无法登录");
            }

            AppLogger.Info("Cookie", $"手动 Cookie 登录: 解析到 {cookies.Count} 条, 名称=[{string.Join(",", cookies.Select(c => c.Name))}]");

            SaveCookie(new CookieInfo { Cookies = cookies });

            // 用 nav 接口校验 Cookie 是否有效，同时取回用户信息
            var httpProvider = Locator.Instance.GetService<IHttpProvider>();
            var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Get, "https://api.bilibili.com/x/web-interface/nav", null, needCookie: true);
            var response = await httpProvider.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var jobj = JObject.Parse(text);
            var code = (int)jobj["code"];
            if (code != 0)
            {
                throw new InvalidOperationException($"Cookie 校验失败，服务端返回 {code}: {jobj["message"]}");
            }

            var data = jobj["data"];
            if (data == null || !(bool)data["isLogin"])
            {
                throw new InvalidOperationException("Cookie 校验失败：服务端返回未登录（Cookie 可能已失效）");
            }

            var mid = (long)data["mid"];
            var uname = (string)data["uname"];

            _cookieOnlyMode = true;
            CurrentUserId = mid.ToString();
            _tokenInfo = null;
            _lastAuthorizeTime = DateTimeOffset.Now;
            ApplicationData.Current.LocalSettings.Values[CookieOnlyMidKey] = mid;
            State = AuthorizeState.SignedIn;
            AppLogger.Info("Auth", $"Cookie 登录成功: mid={mid}, uname={uname}");
            return true;
        }

        private void SaveCookie(CookieInfo cookieInfo)
        {
            if (cookieInfo == null || cookieInfo.Cookies == null)
            {
                return;
            }

            // 关键：必须用带前导点的域，Cookie 才能被 api.bilibili.com / app.bilibili.com 等子域共享。
            // 原来只写 "bilibili.com"（无前导点）会被当作主机级 Cookie，子域请求根本带不上，
            // 于是「扫码成功、弹窗关闭，但随后拉取用户信息失败又被自动登出」。
            var domains = new List<string> { ".bilibili.com" };
            if (cookieInfo.Domains != null)
            {
                foreach (var d in cookieInfo.Domains)
                {
                    if (!string.IsNullOrWhiteSpace(d)
                        && d.IndexOf("bilibili.com", StringComparison.OrdinalIgnoreCase) >= 0
                        && !domains.Contains(d))
                    {
                        domains.Add(d);
                    }
                }
            }

            var filter = new HttpBaseProtocolFilter();
            AppLogger.Info("Cookie", $"开始写入: 域=[{string.Join(",", domains)}], 条目数={cookieInfo.Cookies.Count}");
            foreach (var cookieItem in cookieInfo.Cookies)
            {
                // 服务端对部分 Cookie 返回 expires=0（会话级），若直接 FromUnixTimeSeconds(0)
                // 会得到 1970 年，Cookie 立即过期，等同于没写入 —— 表现为「扫码成功但仍是未登录」。
                var expires = cookieItem.Expires > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(cookieItem.Expires)
                    : DateTimeOffset.Now.AddYears(1);
                if (expires <= DateTimeOffset.Now)
                {
                    expires = DateTimeOffset.Now.AddYears(1);
                }

                foreach (var domain in domains)
                {
                    filter.CookieManager.SetCookie(new Windows.Web.Http.HttpCookie(cookieItem.Name, domain, "/")
                    {
                        HttpOnly = cookieItem.HttpOnly == 1,
                        Secure = cookieItem.Secure == 1,
                        Expires = expires,
                        Value = cookieItem.Value,
                    });
                }
            }

            // 回读校验：写不进去时宁可显式报错，也不要静默「登录成功」后被自动登出
            var saved = filter.CookieManager.GetCookies(new Uri(ApiConstants.CookieGetDomain));
            AppLogger.Info("Cookie", $"回读结果: {saved.Count} 条, 含 SESSDATA={saved.Any(c => c.Name == "SESSDATA")}, 名称=[{string.Join(",", saved.Select(c => c.Name))}]");
            if (!saved.Any(c => c.Name == "SESSDATA"))
            {
                throw new InvalidOperationException(
                    "登录 Cookie 写入后回读失败，未找到 SESSDATA（域: " + string.Join(",", domains) + "）");
            }
        }

        private async Task<string> GetCookieToAccessKeyConfirmUrlAsync()
        {
            try
            {
                var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                var query = new Dictionary<string, string>
                {
                    { "api", ApiConstants.Passport.LoginAppThirdApi },
                };
                var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Get, Passport.LoginAppThird, query, type: RequestClientType.IOS, needCookie: true, needAppKey: true);
                var response = await httpProvider.SendAsync(request);
                var result = await httpProvider.ParseAsync<ServerResponse<LoginAppThird>>(response);
                return result.Data.ConfirmUri;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private async Task<string> GetAccessKeyAsync(string confirmUri)
        {
            try
            {
                var httpProvider = Locator.Instance.GetService<IHttpProvider>();
                var request = await httpProvider.GetRequestMessageAsync(HttpMethod.Get, confirmUri, needCookie: true);
                var response = await httpProvider.SendAsync(request);
                var success = response.Headers.TryGetValues("location", out var locations);
                if (!success)
                {
                    return default;
                }

                var redirectUrl = locations.FirstOrDefault();
                var uri = new Uri(redirectUrl);
                var queries = HttpUtility.ParseQueryString(uri.Query);
                var accessKey = queries.Get("access_key");
                return accessKey;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
