// Copyright (c) Richasy. All rights reserved.

using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Bili.ViewModels.Interfaces.Download
{
    /// <summary>
    /// 下载管理页面视图模型接口.
    /// </summary>
    public interface IDownloadPageViewModel
    {
        /// <summary>
        /// 下载任务集合.
        /// </summary>
        ObservableCollection<IDownloadTaskViewModel> Items { get; }

        /// <summary>
        /// 打开下载文件夹的命令.
        /// </summary>
        IAsyncRelayCommand OpenFolderCommand { get; }

        /// <summary>
        /// 取消下载的命令.
        /// </summary>
        IRelayCommand<IDownloadTaskViewModel> CancelCommand { get; }

        /// <summary>
        /// 移除任务的命令.
        /// </summary>
        IAsyncRelayCommand<IDownloadTaskViewModel> RemoveCommand { get; }

        /// <summary>
        /// 删除已下载文件（保留任务）的命令.
        /// </summary>
        IAsyncRelayCommand<IDownloadTaskViewModel> DeleteCommand { get; }

        /// <summary>
        /// 输入的视频 ID.
        /// </summary>
        string InputText { get; set; }

        /// <summary>
        /// 是否没有下载任务.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// 按输入的视频 ID 添加下载任务的命令.
        /// </summary>
        IAsyncRelayCommand AddCommand { get; }

        /// <summary>
        /// 打开已下载文件的命令.
        /// </summary>
        IAsyncRelayCommand<IDownloadTaskViewModel> OpenFileCommand { get; }

        /// <summary>
        /// 添加下载任务.
        /// </summary>
        /// <param name="videoId">视频 ID（aid）.</param>
        /// <param name="partId">分 P 的 cid.</param>
        /// <param name="title">标题.</param>
        /// <param name="quality">清晰度（qn），默认 64（720P）.</param>
        /// <returns><see cref="Task"/>.</returns>
        Task EnqueueAsync(string videoId, string partId, string title, int quality = 64);
    }

    /// <summary>
    /// 下载任务视图模型接口.
    /// </summary>
    public interface IDownloadTaskViewModel
    {
        /// <summary>
        /// 标题.
        /// </summary>
        string Title { get; }

        /// <summary>
        /// 进度（0-100）.
        /// </summary>
        /// <summary>
        /// 视频 ID（aid）.
        /// </summary>
        string VideoId { get; }

        /// <summary>
        /// 分 P 的 cid.
        /// </summary>
        string PartId { get; }

        /// <summary>
        /// 清晰度（qn）.
        /// </summary>
        int Quality { get; }

        double Progress { get; set; }

        /// <summary>
        /// 状态文本.
        /// </summary>
        string StatusText { get; set; }

        /// <summary>
        /// 速度文本.
        /// </summary>
        string SpeedText { get; set; }

        /// <summary>
        /// 大小文本.
        /// </summary>
        string SizeText { get; set; }

        /// <summary>
        /// 是否正在下载.
        /// </summary>
        bool IsDownloading { get; set; }

        /// <summary>
        /// 取消令牌.
        /// </summary>
        CancellationTokenSource Cancellation { get; }

        /// <summary>
        /// 保存路径.
        /// </summary>
        string FilePath { get; set; }
    }
}
