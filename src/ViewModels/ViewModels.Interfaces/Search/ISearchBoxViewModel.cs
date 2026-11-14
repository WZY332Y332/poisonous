// Copyright (c) Richasy. All rights reserved.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Bili.Models.Data.Search;
using CommunityToolkit.Mvvm.Input;

namespace Bili.ViewModels.Interfaces.Search
{
    /// <summary>
    /// 搜索框视图模型的接口定义.
    /// </summary>
    public interface ISearchBoxViewModel : INotifyPropertyChanged, IInitializeViewModel
    {
        /// <summary>
        /// 热搜集合.
        /// </summary>
        ObservableCollection<SearchSuggest> HotSearchCollection { get; }

        /// <summary>
        /// 搜索建议集合.
        /// </summary>
        ObservableCollection<SearchSuggest> SearchSuggestion { get; }

        /// <summary>
        /// 执行搜索的命令.
        /// </summary>
        IAsyncRelayCommand<string> SearchCommand { get; }

        /// <summary>
        /// 选中搜索建议并执行搜索的命令.
        /// </summary>
        IAsyncRelayCommand<SearchSuggest> SelectSuggestCommand { get; }

        /// <summary>
        /// 关键词.
        /// </summary>
        string Keyword { get; set; }

        /// <summary>
        /// 搜索历史集合.
        /// </summary>
        ObservableCollection<string> HistoryCollection { get; }

        /// <summary>
        /// 清空搜索历史的命令.
        /// </summary>
        IRelayCommand ClearHistoryCommand { get; }

        /// <summary>
        /// 移除单条搜索历史的命令.
        /// </summary>
        IRelayCommand<string> RemoveHistoryCommand { get; }

        /// <summary>
        /// 是否显示搜索历史.
        /// </summary>
        bool IsShowHistory { get; }
    }
}
