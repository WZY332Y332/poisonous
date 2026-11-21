// Copyright (c) Richasy. All rights reserved.

using Bili.DI.Container;
using Bili.ViewModels.Interfaces.Download;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Bili.App.Pages.Desktop.Overlay
{
    /// <summary>
    /// 下载管理页面.
    /// </summary>
    public sealed partial class DownloadPage : Page
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadPage"/> class.
        /// </summary>
        public DownloadPage()
        {
            InitializeComponent();
            ViewModel = Locator.Instance.GetService<IDownloadPageViewModel>();
            DataContext = ViewModel;
        }

        /// <summary>
        /// 下载管理视图模型.
        /// </summary>
        public IDownloadPageViewModel ViewModel { get; }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is IDownloadTaskViewModel task)
            {
                ViewModel.CancelCommand.Execute(task);
            }
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is IDownloadTaskViewModel task)
            {
                ViewModel.OpenFileCommand.Execute(task);
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is IDownloadTaskViewModel task)
            {
                ViewModel.RemoveCommand.Execute(task);
            }
        }
    }
}
