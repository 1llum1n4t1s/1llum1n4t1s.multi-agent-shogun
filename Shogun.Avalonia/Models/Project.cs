using CommunityToolkit.Mvvm.ComponentModel;
using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

/// <summary>
/// プロジェクトモデル。
/// </summary>
[YamlObject(NamingConvention.LowerCamelCase)]
public partial class Project : ObservableObject
{
    /// <summary>プロジェクトID（一意）。</summary>
    [ObservableProperty]
    [property: YamlMember("id")]
    private string _id = string.Empty;

    /// <summary>プロジェクト名。</summary>
    [ObservableProperty]
    [property: YamlMember("name")]
    private string _name = string.Empty;

    /// <summary>プロジェクトパス。</summary>
    [ObservableProperty]
    [property: YamlMember("path")]
    private string _path = string.Empty;

    /// <summary>優先度（high, medium, low 等）。</summary>
    [ObservableProperty]
    [property: YamlMember("priority")]
    private string _priority = "medium";

    /// <summary>ステータス（active, inactive, archived 等）。</summary>
    [ObservableProperty]
    [property: YamlMember("status")]
    private string _status = "active";

    /// <summary>Notion URL（任意）。</summary>
    [ObservableProperty]
    [property: YamlMember("notion_url")]
    private string _notionUrl = string.Empty;

    /// <summary>説明（任意）。</summary>
    [ObservableProperty]
    [property: YamlMember("description")]
    private string _description = string.Empty;
}
