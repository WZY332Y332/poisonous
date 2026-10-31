// Copyright (c) Richasy. All rights reserved.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bili.App.Controls;
using Bili.ViewModels.Interfaces.Home;
using Bili.ViewModels.Interfaces.Toolbox;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
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
                case Models.Enums.ToolboxItemType.DiagnosticLog:
                    await ShowDiagnosticLogAsync();
                    break;
                default:
                    break;
            }
        }

        private async Task ShowDiagnosticLogAsync()
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "BiliDiag");
            var exist = Directory.Exists(path);
            var dialog = new ContentDialog
            {
                Title = "诊断日志",
                CloseButtonText = "关闭",
            };

            if (!exist)
            {
                dialog.Content = $"日志目录尚未创建：\n{path}\n\n启动应用并操作一次后即可生成。";
            }
            else
            {
                var data = new DataPackage();
                data.SetText(path);
                Clipboard.SetContent(data);

                var files = Directory.GetFiles(path, "bili-*.log");
                dialog.Content = $"已复制目录路径到剪贴板：\n{path}\n\n当前日志文件 {files.Length} 个，最近的：{Path.GetFileName(files.OrderByDescending(File.GetLastWriteTime).FirstOrDefault() ?? "(无)")}";
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
