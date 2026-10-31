// Copyright (c) Richasy. All rights reserved.

using System;
using System.Threading.Tasks;
using Bili.Models.App.Args;
using Bili.Models.App.Other;
using Bili.Models.Enums;

namespace Bili.ViewModels.Uwp.Account
{
    /// <summary>
    /// 账户视图模型.
    /// </summary>
    public sealed partial class AccountViewModel
    {
        /// <summary>
        /// 尝试启动用户登录流程.
        /// </summary>
        /// <param name="isSlientOnly">是否只静默登录.</param>
        /// <returns><see cref="Task"/>.</returns>
        private async Task<bool> InternalSignInAsync(bool isSlientOnly = false)
        {
            var isSuccess = false;
            State = AuthorizeState.Loading;
            try
            {
                var isTokenValid = await _authorizeProvider.IsTokenValidAsync(true);
                if (isTokenValid)
                {
                    isSuccess = true;
                    State = AuthorizeState.SignedIn;
                    HandleLogged();
                }
                else if (!isSlientOnly)
                {
                    isSuccess = await _authorizeProvider.TrySignInAsync();
                }
                else
                {
                    HandleLoggingFailed(new OperationCanceledException());
                }
            }
            catch (Exception ex)
            {
                HandleLoggingFailed(ex);
            }

            return isSuccess;
        }

        private void HandleLogged()
        {
            AppLogger.Info("Account", $"HandleLogged 进入, State={State}");
            if (State == AuthorizeState.SignedIn)
            {
                LoadMyProfileCommand.ExecuteAsync(null).ContinueWith(async t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        AppLogger.Info("Account", "拉取用户信息成功");
                        await _dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        {
                            IsConnected = true;
                            await InitializeFixedItemAsync();
                            State = AuthorizeState.SignedIn;
                        });
                    }
                    else
                    {
                        // 拉取用户资料失败不应该导致登出：
                        // 仅 Cookie 登录模式没有 access_token，依赖 gRPC 的接口必然失败，
                        // 若因此登出就会陷入「登录成功 -> 立刻掉线」。保持登录态，仅记录错误。
                        AppLogger.Error("Account", "拉取用户信息失败（保持登录态，不自动登出）", t.Exception);
                    }
                });
            }
        }

        private void HandleLoggingFailed(Exception exception)
        {
            AppLogger.Warn("Account", $"HandleLoggingFailed: {exception?.GetType().Name}: {exception?.Message}, State={State}");

            // 它仅在用户未登录时触发.
            if (State != AuthorizeState.SignedIn)
            {
                Reset();
                State = AuthorizeState.SignedOut;

                if (exception is ServiceException serviceEx
                    && (!serviceEx.IsHttpError()))
                {
                    SignOutCommand.ExecuteAsync(null);
                }
            }
        }

        private void OnAuthorizeStateChanged(object sender, AuthorizeStateChangedEventArgs e)
        {
            State = e.NewState;
            switch (e.NewState)
            {
                case AuthorizeState.SignedIn:
                    HandleLogged();
                    break;
                case AuthorizeState.SignedOut:
                    if (!_isRequestLogout)
                    {
                        HandleLoggingFailed(new OperationCanceledException("请求失败"));
                    }
                    else
                    {
                        Reset();
                    }

                    break;
                default:
                    break;
            }
        }
    }
}
