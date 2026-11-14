// Copyright (c) Richasy. All rights reserved.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bili.Lib.Interfaces;
using Bili.ViewModels.Uwp.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;

namespace Bili.ViewModels.Uwp.Download
{
    /// <summary>
    /// 下载任务视图模型.
    /// </summary>
    public sealed partial class DownloadTaskViewModel : ObservableObject
    {
        /// <summary>
        /// 进度（0-100）.
        /// </summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>
        /// 状态文本.
        /// </summary>
        [ObservableProperty]
        private string _statusText = "等待中";

        /// <summary>
        /// 是否正在下载.
        /// </summary>
        [ObservableProperty]
        private bool _isDownloading;

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadTaskViewModel"/> class.
        /// </summary>
        /// <param name="title">标题.</param>
        /// <param name="videoId">视频 ID（aid）.</param>
        /// <param name="partId">分 P 的 cid.</param>
        /// <param name="quality">清晰度（qn）.</param>
        public DownloadTaskViewModel(string title, string videoId, string partId, int quality)
        {
            Title = title;
            VideoId = videoId;
            PartId = partId;
            Quality = quality;
            Cancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// 标题.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// 视频 ID（aid）.
        /// </summary>
        public string VideoId { get; }

        /// <summary>
        /// 分 P 的 cid.
        /// </summary>
        public string PartId { get; }

        /// <summary>
        /// 清晰度（qn）.
        /// </summary>
        public int Quality { get; }

        /// <summary>
        /// 取消令牌.
        /// </summary>
        public CancellationTokenSource Cancellation { get; }

        /// <summary>
        /// 保存路径.
        /// </summary>
        public string FilePath { get; set; }


    }

    /// <summary>
    /// 下载管理页面视图模型.
    /// </summary>
    public sealed partial class DownloadPageViewModel : ViewModelBase
    {
        private static readonly HttpClient HttpClientInstance = new HttpClient();

        private readonly IPlayerProvider _playerProvider;

        [ObservableProperty]
        private bool _isEmpty = true;

        [ObservableProperty]
        private string _inputText;

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadPageViewModel"/> class.
        /// </summary>
        /// <param name="playerProvider">播放数据提供者.</param>
        public DownloadPageViewModel(IPlayerProvider playerProvider)
        {
            _playerProvider = playerProvider;
            Items = new ObservableCollection<DownloadTaskViewModel>();
            OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
            CancelCommand = new RelayCommand<DownloadTaskViewModel>(Cancel);
            RemoveCommand = new RelayCommand<DownloadTaskViewModel>(Remove);
            AddCommand = new AsyncRelayCommand(AddAsync);
        }

        /// <summary>
        /// 下载任务集合.
        /// </summary>
        public ObservableCollection<DownloadTaskViewModel> Items { get; }

        /// <summary>
        /// 打开下载文件夹的命令.
        /// </summary>
        public IAsyncRelayCommand OpenFolderCommand { get; }

        /// <summary>
        /// 取消下载的命令.
        /// </summary>
        public IRelayCommand<DownloadTaskViewModel> CancelCommand { get; }

        /// <summary>
        /// 移除任务的命令.
        /// </summary>
        public IRelayCommand<DownloadTaskViewModel> RemoveCommand { get; }

        /// <summary>
        /// 按输入的视频 ID 添加下载任务的命令.
        /// </summary>
        public IAsyncRelayCommand AddCommand { get; }

        /// <summary>
        /// 添加下载任务.
        /// </summary>
        /// <param name="videoId">视频 ID（aid）.</param>
        /// <param name="partId">分 P 的 cid.</param>
        /// <param name="title">标题.</param>
        /// <param name="quality">清晰度（qn），默认 64（720P）.</param>
        /// <returns><see cref="Task"/>.</returns>
        public async Task EnqueueAsync(string videoId, string partId, string title, int quality = 64)
        {
            if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(partId))
            {
                return;
            }

            var item = new DownloadTaskViewModel(title, videoId, partId, quality);
            Items.Insert(0, item);
            IsEmpty = false;
            await DownloadAsync(item);
        }

        private async Task AddAsync()
        {
            var id = InputText;
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            InputText = string.Empty;
            await EnqueueByVideoIdAsync(id.Trim());
        }

        private async Task EnqueueByVideoIdAsync(string videoId)
        {
            var view = await _playerProvider.GetVideoDetailAsync(videoId);
            if (view == null)
            {
                return;
            }

            var part = view.SubVideos?.FirstOrDefault();
            await EnqueueAsync(view.Information.Identifier.Id, part?.Id, view.Information.Identifier.Title);
        }
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "video";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            name = name.Trim();
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        private async Task DownloadAsync(DownloadTaskViewModel item)
        {
            item.IsDownloading = true;
            item.Progress = 0;
            item.StatusText = "获取下载地址…";

            try
            {
                var urls = await _playerProvider.GetVideoDownloadUrlsAsync(item.VideoId, item.PartId, item.Quality);
                if (urls == null || urls.Count == 0)
                {
                    item.StatusText = "没有可用的下载地址（可能需要登录或该视频不支持）";
                    item.IsDownloading = false;
                    return;
                }

                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
                var fileName = SanitizeFileName(item.Title) + ".mp4";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                item.FilePath = file.Path;

                long totalRead = 0;
                long totalLength = 0;

                foreach (var url in urls)
                {
                    using (var response = await HttpClientInstance.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, item.Cancellation.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        var length = response.Content.Headers.ContentLength ?? 0;
                        totalLength += length;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            using (var fileStream = new FileStream(file.Path, totalRead == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                var buffer = new byte[81920];
                                int read;
                                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, item.Cancellation.Token)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, read, item.Cancellation.Token);
                                    totalRead += read;
                                    item.StatusText = string.Format("下载中 {0:0.0} MB", totalRead / 1024d / 1024d);
                                    if (totalLength > 0)
                                    {
                                        item.Progress = Math.Min(99, totalRead * 100d / totalLength);
                                    }
                                }
                            }
                        }
                    }
                }

                item.Progress = 100;
                item.StatusText = "已完成";
            }
            catch (OperationCanceledException)
            {
                item.StatusText = "已取消";
            }
            catch (Exception ex)
            {
                item.StatusText = "失败：" + ex.Message;
                LogException(ex);
            }
            finally
            {
                item.IsDownloading = false;
            }
        }

        private void Cancel(DownloadTaskViewModel item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.Cancellation.Cancel();
            }
            catch (Exception)
            {
            }

            item.StatusText = "已取消";
            item.IsDownloading = false;
        }

        private void Remove(DownloadTaskViewModel item)
        {
            if (item != null && Items.Contains(item))
            {
                Items.Remove(item);
                IsEmpty = Items.Count == 0;
            }
        }

        private async Task OpenFolderAsync()
        {
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
    }
}
