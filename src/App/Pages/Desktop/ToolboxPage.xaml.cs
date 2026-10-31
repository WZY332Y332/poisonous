// Copyright (c) Richasy. All rights reserved.

using System.Threading.Tasks;
using Bili.App.Controls;
using Bili.DI.Container;
using Bili.Lib.Interfaces;
using Bili.ViewModels.Interfaces.Home;
using Bili.ViewModels.Interfaces.Toolbox;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Bili.App.Pages.Desktop
{
    /// <summary>
    /// 工具箱页面.
    /// </summary>
    public sealed partial class ToolboxPage : ToolboxPageBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolboxPage"/> class.
        /// </summary>
        public ToolboxPage() => InitializeComponent();

        /// <inheritdoc/>
        protected override void OnPageLoaded()
            => Bindings.Update();

        /// <inheritdoc/>
        protected override void OnPageUnloaded()
            => Bindings.StopTracking();

        private async void OnItemClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement).DataContext as IToolboxItemViewModel;
            switch (item.Type)
            {
                case Models.Enums.ToolboxItemType.AvBvConverter:
                    new AvBvConverterView().Show();
                    break;
                case Models.Enums.ToolboxItemType.CoverDownloader:
                    new CoverDownloaderView().Show();
                    break;
                case Models.Enums.ToolboxItemType.CookieBackup:
                    await CopyCookieAsync();
                    break;
                default:
                    break;
            }
        }

        private async Task CopyCookieAsync()
        {
            var cookie = Locator.Instance.GetService<IAuthorizeProvider>().GetCookieString();
            var dialog = new ContentDialog
            {
                Title = "登录 Cookie",
                CloseButtonText = "关闭",
            };

            if (string.IsNullOrEmpty(cookie))
            {
                dialog.Content = "当前没有可备份的 Cookie，请先登录。";
            }
            else
            {
                var data = new DataPackage();
                data.SetText(cookie);
                Clipboard.SetContent(data);
                dialog.Content = $"已复制到剪贴板（{cookie.Length} 字符）。Cookie 等同于账号凭证，请妥善保管，不要外传。";
            }

            await dialog.ShowAsync();
        }
    }

    /// <summary>
    /// <see cref="ToolboxPage"/> 的基类.
    /// </summary>
    public class ToolboxPageBase : AppPage<IToolboxPageViewModel>
    {
    }
}
