// Copyright (c) Richasy. All rights reserved.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bili.Lib.Interfaces;
using Bili.ViewModels.Interfaces.Download;
using Bili.ViewModels.Uwp.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;

namespace Bili.ViewModels.Uwp.Download
{
    /// <summary>
    /// 下载任务视图模型.
    /// </summary>
    public sealed partial class DownloadTaskViewModel : ObservableObject, IDownloadTaskViewModel
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

        [ObservableProperty]
        private string _speedText = string.Empty;

        [ObservableProperty]
        private string _sizeText = string.Empty;

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
    public sealed partial class DownloadPageViewModel : ViewModelBase, IDownloadPageViewModel
    {
        private static readonly HttpClient HttpClientInstance = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private static Windows.UI.Core.CoreDispatcher _uiDispatcher;

        private const string TasksFileName = "downloads.txt";

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
            Items = new ObservableCollection<IDownloadTaskViewModel>();
            OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
            CancelCommand = new RelayCommand<IDownloadTaskViewModel>(Cancel);
            RemoveCommand = new RelayCommand<IDownloadTaskViewModel>(Remove);
            OpenFileCommand = new AsyncRelayCommand<IDownloadTaskViewModel>(OpenFileAsync);
            LoadTasks();
            AddCommand = new AsyncRelayCommand(AddAsync);
        }

        /// <summary>
        /// 下载任务集合.
        /// </summary>
        public ObservableCollection<IDownloadTaskViewModel> Items { get; }

        /// <summary>
        /// 打开下载文件夹的命令.
        /// </summary>
        public IAsyncRelayCommand OpenFolderCommand { get; }

        /// <summary>
        /// 取消下载的命令.
        /// </summary>
        public IRelayCommand<IDownloadTaskViewModel> CancelCommand { get; }

        /// <summary>
        /// 移除任务的命令.
        /// </summary>
        public IRelayCommand<IDownloadTaskViewModel> RemoveCommand { get; }

        /// <inheritdoc/>
        public IAsyncRelayCommand<IDownloadTaskViewModel> OpenFileCommand { get; }

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

            if (_uiDispatcher == null)
            {
                _uiDispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView?.CoreWindow?.Dispatcher;
            }

            var item = new DownloadTaskViewModel(title, videoId, partId, quality);
            Items.Insert(0, item);
            UpdateUi(() => IsEmpty = false);
            SaveTasks();
            await DownloadAsync(item);
        }

        private static void UpdateUi(Action action)
        {
            var dispatcher = _uiDispatcher;
            if (dispatcher == null || dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                _ = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => action());
            }
        }

        private static void UpdateItem(IDownloadTaskViewModel item, double progress, string status, bool? isDownloading = null, string speed = null, string size = null)
        {
            UpdateUi(() =>
            {
                item.Progress = progress;
                item.StatusText = status;
                if (isDownloading.HasValue)
                {
                    item.IsDownloading = isDownloading.Value;
                }

                if (speed != null)
                {
                    item.SpeedText = speed;
                }

                if (size != null)
                {
                    item.SizeText = size;
                }
            });
        }

        private string SanitizeFileName(string name)
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

        private async Task DownloadAsync(IDownloadTaskViewModel item)
        {
            UpdateItem(item, 0, "获取下载地址…", true);

            try
            {
                var urls = await _playerProvider.GetVideoDownloadUrlsAsync(item.VideoId, item.PartId, item.Quality);
                if (urls == null || urls.Count == 0)
                {
                    UpdateItem(item, 0, "没有可用的下载地址（可能需要登录或该视频不支持）", false);
                    return;
                }

                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
                var fileName = SanitizeFileName(item.Title) + ".mp4";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                item.FilePath = file.Path;

                var isSuccess = false;
                Exception lastError = null;

                foreach (var url in urls)
                {
                    UpdateUi(() => item.StatusText = "连接中…");
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
                            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");

                            using (var response = await HttpClientInstance.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, item.Cancellation.Token))
                            {
                                response.EnsureSuccessStatusCode();
                                var totalLength = response.Content.Headers.ContentLength ?? 0;
                                long totalRead = 0;
                                var lastTick = DateTime.Now;
                                long lastBytes = 0;

                                using (var stream = await response.Content.ReadAsStreamAsync())
                                {
                                    using (var fileStream = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                                    {
                                        var buffer = new byte[81920];
                                        int read;
                                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, item.Cancellation.Token)) > 0)
                                        {
                                            await fileStream.WriteAsync(buffer, 0, read, item.Cancellation.Token);
                                            totalRead += read;

                                            var now = DateTime.Now;
                                            var elapsed = (now - lastTick).TotalSeconds;
                                            if (elapsed >= 0.4)
                                            {
                                                var speed = (totalRead - lastBytes) / elapsed;
                                                lastBytes = totalRead;
                                                lastTick = now;
                                                var sizeText = totalLength > 0
                                                    ? string.Format("{0:0.0} / {1:0.0} MB", totalRead / 1024d / 1024d, totalLength / 1024d / 1024d)
                                                    : string.Format("{0:0.0} MB", totalRead / 1024d / 1024d);
                                                var percent = totalLength > 0 ? Math.Min(99, totalRead * 100d / totalLength) : 0;
                                                UpdateItem(item, percent, "下载中", null, FormatSpeed(speed), sizeText);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        isSuccess = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                if (!isSuccess)
                {
                    throw lastError ?? new InvalidOperationException("所有下载地址均不可用");
                }

                UpdateItem(item, 100, "已完成", false, string.Empty, item.SizeText);
                SaveTasks();
            }
            catch (OperationCanceledException)
            {
                UpdateItem(item, item.Progress, "已取消", false, string.Empty);
                SaveTasks();
            }
            catch (Exception ex)
            {
                UpdateItem(item, item.Progress, "失败：" + ex.Message, false, string.Empty);
                LogException(ex);
                SaveTasks();
            }
            finally
            {
                UpdateUi(() => item.IsDownloading = false);
            }
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return string.Empty;
            }

            if (bytesPerSecond >= 1024 * 1024)
            {
                return string.Format("{0:0.0} MB/s", bytesPerSecond / 1024d / 1024d);
            }

            return string.Format("{0:0} KB/s", bytesPerSecond / 1024d);
        }

        private void SaveTasks()
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, TasksFileName);
                var sb = new System.Text.StringBuilder();
                foreach (var item in Items)
                {
                    var line = string.Join("\t", new[]
                    {
                        Clean(item.Title),
                        item.VideoId,
                        item.PartId,
                        item.Quality.ToString(),
                        item.Progress.ToString("0"),
                        Clean(item.StatusText),
                        Clean(item.FilePath),
                    });
                    sb.AppendLine(line);
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void LoadTasks()
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, TasksFileName);
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 7)
                    {
                        continue;
                    }

                    if (!int.TryParse(parts[3], out var quality))
                    {
                        quality = 64;
                    }

                    var item = new DownloadTaskViewModel(parts[0], parts[1], parts[2], quality);
                    if (double.TryParse(parts[4], out var progress))
                    {
                        item.Progress = progress;
                    }

                    item.StatusText = parts[5];
                    item.FilePath = parts[6];
                    Items.Add(item);
                }

                IsEmpty = Items.Count == 0;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private string Clean(string value)
            => string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private async Task OpenFileAsync(IDownloadTaskViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath))
            {
                return;
            }

            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.FilePath);
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }
        private void Cancel(IDownloadTaskViewModel item)
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

            UpdateItem(item, item.Progress, "已取消", false);
        }

        private void Remove(IDownloadTaskViewModel item)
        {
            if (item != null && Items.Contains(item))
            {
                Items.Remove(item);
                UpdateUi(() => IsEmpty = Items.Count == 0);
                SaveTasks();
            }
        }

        private async Task OpenFolderAsync()
        {
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
    }
}
