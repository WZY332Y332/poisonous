// Copyright (c) Richasy. All rights reserved.

using System.Linq;
using System.Threading.Tasks;
using Bili.DI.Container;
using Bili.Lib.Interfaces;
using Bili.Models.App.Other;
using Bili.ViewModels.Interfaces.Core;
using Bili.ViewModels.Interfaces.Video;
using Bili.ViewModels.Uwp.Base;
using CommunityToolkit.Mvvm.Input;

namespace Bili.ViewModels.Uwp.Video
{
    /// <summary>
    /// 视频收藏模块视图模型.
    /// </summary>
    public sealed partial class VideoFavoriteModuleViewModel : InformationFlowViewModelBase<IVideoFavoriteFolderGroupViewModel>, IVideoFavoriteModuleViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFavoriteModuleViewModel"/> class.
        /// </summary>
        public VideoFavoriteModuleViewModel(
            INavigationViewModel navigationViewModel,
            IAccountProvider accountProvider,
            IFavoriteProvider favoriteProvider)
        {
            _navigationViewModel = navigationViewModel;
            _accountProvider = accountProvider;
            _favoriteProvider = favoriteProvider;
            ShowDefaultFolderDetailCommand = new RelayCommand(ShowDefaultFolderDetail);
        }

        /// <inheritdoc/>
        protected override void BeforeReload()
        {
            DefaultFolder = null;
            IsDefaultFolderEmpty = false;
        }

        /// <inheritdoc/>
        protected override async Task GetDataAsync()
        {
            var data = await _favoriteProvider.GetVideoFavoriteViewAsync(_accountProvider.UserId.ToString());
            AppLogger.Info("Favorite", $"收藏夹数据: mid={_accountProvider.UserId}, 默认夹={(data?.DefaultFolder == null ? "null" : "有")}, 分组数={data?.Groups?.Count() ?? 0}");

            // 默认收藏夹可能为空（接口未返回），但不能因此跳过下面的分组
            DefaultFolder = data?.DefaultFolder?.Folder;
            IsDefaultFolderEmpty = data?.DefaultFolder == null || data.DefaultFolder.VideoSet.TotalCount == 0;

            if (data?.Groups == null)
            {
                return;
            }

            foreach (var item in data.Groups)
            {
                var groupVM = Locator.Instance.GetService<IVideoFavoriteFolderGroupViewModel>();
                groupVM.InjectData(item);
                Items.Add(groupVM);
            }
        }

        private void ShowDefaultFolderDetail()
            => _navigationViewModel.NavigateToSecondaryView(Models.Enums.PageIds.VideoFavoriteDetail, DefaultFolder);
    }
}
