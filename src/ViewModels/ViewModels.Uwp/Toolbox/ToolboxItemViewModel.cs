// Copyright (c) Richasy. All rights reserved.

using Bili.Models.Enums;
using Bili.Toolkit.Interfaces;
using Bili.ViewModels.Interfaces.Toolbox;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bili.ViewModels.Uwp.Toolbox
{
    /// <summary>
    /// 工具箱条目视图模型.
    /// </summary>
    public sealed partial class ToolboxItemViewModel : ViewModelBase, IToolboxItemViewModel
    {
        private readonly IResourceToolkit _resourceToolkit;

        [ObservableProperty]
        private ToolboxItemType _type;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private string _description;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolboxItemViewModel"/> class.
        /// </summary>
        public ToolboxItemViewModel(IResourceToolkit resourceToolkit)
            => _resourceToolkit = resourceToolkit;

        /// <inheritdoc/>
        public void SetType(ToolboxItemType type)
        {
            Type = type;
            switch (type)
            {
                case ToolboxItemType.AvBvConverter:
                    Title = _resourceToolkit.GetLocaleString(LanguageNames.AvBvConverter);
                    Description = _resourceToolkit.GetLocaleString(LanguageNames.AvBvConverterDescription);
                    break;
                case ToolboxItemType.CoverDownloader:
                    Title = _resourceToolkit.GetLocaleString(LanguageNames.CoverDownloader);
                    Description = _resourceToolkit.GetLocaleString(LanguageNames.CoverDownloaderDescription);
                    break;
                case ToolboxItemType.CookieBackup:
                    Title = "登录 Cookie 备份";
                    Description = "把当前登录 Cookie 复制到剪贴板，便于备份或迁移到其他设备";
                    break;
                default:
                    break;
            }
        }
    }
}
