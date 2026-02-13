#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TombLib.LevelData;
using TombLib.Utils;
using TombLib.Wad;
using TombLib.Wad.Catalog;

namespace TombEditor.ViewModels;

/// <summary>
/// Categories for grouping assets in the Content Browser.
/// </summary>
public enum AssetCategory
{
    All,
    Moveables,
    Statics,
    ImportedGeometry
}

/// <summary>
/// ViewModel representing a single asset item in the Content Browser.
/// </summary>
public partial class AssetItemViewModel : ObservableObject
{
    /// <summary>
    /// The underlying WAD object (WadMoveable, WadStatic, or ImportedGeometry).
    /// </summary>
    public IWadObject WadObject { get; }

    /// <summary>
    /// Display name of the asset.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Category this asset belongs to.
    /// </summary>
    public AssetCategory Category { get; }

    /// <summary>
    /// Category display name for grouping.
    /// </summary>
    public string CategoryName => Category switch
    {
        AssetCategory.Moveables => "Moveables",
        AssetCategory.Statics => "Statics",
        AssetCategory.ImportedGeometry => "Imported Geometry",
        _ => "Unknown"
    };

    /// <summary>
    /// Name/path of the WAD file this asset was loaded from.
    /// </summary>
    public string WadSource { get; }

    /// <summary>
    /// Whether this asset exists in multiple WAD files.
    /// </summary>
    public bool IsInMultipleWads { get; }

    /// <summary>
    /// Color brush for the placeholder thumbnail, based on category.
    /// </summary>
    public SolidColorBrush ThumbnailBrush { get; }

    /// <summary>
    /// Initials shown on the placeholder thumbnail.
    /// </summary>
    public string Initials { get; }

    /// <summary>
    /// Sort order for category grouping (Moveables=0, Statics=1, ImportedGeometry=2).
    /// </summary>
    public int CategoryOrder => Category switch
    {
        AssetCategory.Moveables => 0,
        AssetCategory.Statics => 1,
        AssetCategory.ImportedGeometry => 2,
        _ => 3
    };

    /// <summary>
    /// Whether a rendered 3D thumbnail is available.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private BitmapSource? _thumbnail;

    /// <summary>
    /// Whether a rendered thumbnail has been set.
    /// </summary>
    public bool HasThumbnail => Thumbnail != null;

    /// <summary>
    /// A unique cache key for this asset's thumbnail.
    /// </summary>
    public string CacheKey { get; }

    private static readonly SolidColorBrush MoveableBrush = new(Color.FromRgb(0x4B, 0x6E, 0xAF));
    private static readonly SolidColorBrush StaticBrush = new(Color.FromRgb(0x5A, 0x8C, 0x46));
    private static readonly SolidColorBrush ImportedGeometryBrush = new(Color.FromRgb(0xC0, 0x7B, 0x38));

    static AssetItemViewModel()
    {
        MoveableBrush.Freeze();
        StaticBrush.Freeze();
        ImportedGeometryBrush.Freeze();
    }

    public AssetItemViewModel(IWadObject wadObject, string name, AssetCategory category, string wadSource, bool isInMultipleWads)
    {
        WadObject = wadObject;
        Name = name;
        Category = category;
        WadSource = wadSource;
        IsInMultipleWads = isInMultipleWads;

        ThumbnailBrush = category switch
        {
            AssetCategory.Moveables => MoveableBrush,
            AssetCategory.Statics => StaticBrush,
            AssetCategory.ImportedGeometry => ImportedGeometryBrush,
            _ => MoveableBrush
        };

        Initials = BuildInitials(name);
        CacheKey = BuildCacheKey(wadObject, category);
    }

    private static string BuildCacheKey(IWadObject wadObject, AssetCategory category)
    {
        string prefix = category.ToString();
        if (wadObject.Id != null)
            return $"{prefix}_{wadObject.Id}";
        if (wadObject is ImportedGeometry geo)
            return $"{prefix}_{geo.GetHashCode()}";
        return $"{prefix}_{wadObject.GetHashCode()}";
    }

    /// <summary>
    /// Converts an ImageC (BGRA byte array) to a frozen WPF BitmapSource.
    /// </summary>
    public static BitmapSource? ImageCToBitmapSource(ImageC image)
    {
        if (image.Width == 0 || image.Height == 0)
            return null;

        byte[] data = image.ToByteArray();
        int stride = image.Width * 4; // BGRA = 4 bytes per pixel
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96,
            PixelFormats.Bgra32, null, data, stride);
        bmp.Freeze();
        return bmp;
    }

    private static string BuildInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        // Take the first 2 characters of the name, capitalized
        var cleaned = name.Trim();
        if (cleaned.Length <= 2)
            return cleaned.ToUpperInvariant();

        // If the name contains spaces or underscores, use first letters of first two words
        var parts = cleaned.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();

        return cleaned[..2].ToUpperInvariant();
    }
}

/// <summary>
/// Main ViewModel for the Content Browser tool window.
/// Manages the collection of assets, search/filtering, and category grouping.
/// </summary>
public partial class ContentBrowserViewModel : ObservableObject
{
    /// <summary>
    /// Full collection of all asset items.
    /// </summary>
    public ObservableCollection<AssetItemViewModel> AllItems { get; } = new();

    /// <summary>
    /// Filtered and grouped view of the items.
    /// </summary>
    public ICollectionView FilteredItems { get; }

    /// <summary>
    /// Search text for filtering assets by name.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemCount))]
    private string _searchText = string.Empty;

    /// <summary>
    /// Currently selected category filter.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemCount))]
    private AssetCategory _selectedCategory = AssetCategory.All;

    /// <summary>
    /// Currently selected asset item.
    /// </summary>
    [ObservableProperty]
    private AssetItemViewModel? _selectedItem;

    /// <summary>
    /// WAD source info for the selected item.
    /// </summary>
    [ObservableProperty]
    private string _selectedItemWadInfo = string.Empty;

    /// <summary>
    /// Tile size (width) in pixels. Controls the grid tile dimensions.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileHeight))]
    [NotifyPropertyChangedFor(nameof(ThumbSize))]
    private double _tileWidth = 88;

    /// <summary>
    /// Computed tile height based on tile width.
    /// </summary>
    public double TileHeight => TileWidth * 1.15;

    /// <summary>
    /// Computed thumbnail square size based on tile width.
    /// </summary>
    public double ThumbSize => TileWidth * 0.73;

    /// <summary>
    /// Minimum tile width.
    /// </summary>
    public double MinTileWidth => 60;

    /// <summary>
    /// Maximum tile width.
    /// </summary>
    public double MaxTileWidth => 180;

    /// <summary>
    /// Number of items currently visible after filtering.
    /// </summary>
    public int ItemCount => FilteredItems.Cast<object>().Count();

    /// <summary>
    /// Available category filter options.
    /// </summary>
    public IReadOnlyList<AssetCategory> Categories { get; } = new[]
    {
        AssetCategory.All,
        AssetCategory.Moveables,
        AssetCategory.Statics,
        AssetCategory.ImportedGeometry
    };

    public ContentBrowserViewModel()
    {
        FilteredItems = CollectionViewSource.GetDefaultView(AllItems);
        FilteredItems.Filter = FilterPredicate;
        FilteredItems.SortDescriptions.Add(new SortDescription(nameof(AssetItemViewModel.CategoryOrder), ListSortDirection.Ascending));
        FilteredItems.SortDescriptions.Add(new SortDescription(nameof(AssetItemViewModel.Name), ListSortDirection.Ascending));
        FilteredItems.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AssetItemViewModel.CategoryName)));
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredItems.Refresh();
        OnPropertyChanged(nameof(ItemCount));
    }

    partial void OnSelectedCategoryChanged(AssetCategory value)
    {
        FilteredItems.Refresh();
        OnPropertyChanged(nameof(ItemCount));
    }

    partial void OnSelectedItemChanged(AssetItemViewModel? value)
    {
        if (value != null)
        {
            SelectedItemWadInfo = value.IsInMultipleWads
                ? $"From {value.WadSource} (also in other wads)"
                : $"From {value.WadSource}";
        }
        else
        {
            SelectedItemWadInfo = string.Empty;
        }

        SelectedItemChanged?.Invoke(this, value);
    }

    /// <summary>
    /// Event raised when the selected item changes.
    /// Used by the WinForms host to update the Editor state.
    /// </summary>
    public event EventHandler<AssetItemViewModel?>? SelectedItemChanged;

    /// <summary>
    /// Event raised when a drag-drop operation is requested.
    /// </summary>
    public event EventHandler<AssetItemViewModel>? DragDropRequested;

    /// <summary>
    /// Event raised when thumbnails need to be rendered.
    /// The host (WinForms ToolWindow) handles this on the UI thread where D3D11 is available.
    /// </summary>
    public event EventHandler? ThumbnailRenderRequested;

    /// <summary>
    /// In-memory thumbnail cache keyed by CacheKey.
    /// </summary>
    private readonly Dictionary<string, BitmapSource> _thumbnailCache = new();

    /// <summary>
    /// Refreshes all assets from the current level settings.
    /// </summary>
    public void RefreshAssets(LevelSettings settings)
    {
        var previousSelection = SelectedItem?.WadObject;
        AllItems.Clear();

        var gameVersion = settings.GameVersion;

        // Add moveables
        var allMoveables = settings.WadGetAllMoveables();
        foreach (var kvp in allMoveables)
        {
            var moveable = kvp.Value;
            string name = moveable.ToString(gameVersion);

            bool multiple;
            var itemType = new ItemType(moveable.Id, settings);
            var wad = settings.WadTryGetWad(itemType, out multiple);
            string wadSource = wad != null ? Path.GetFileName(wad.Path) : "Unknown";

            AllItems.Add(new AssetItemViewModel(moveable, name, AssetCategory.Moveables, wadSource, multiple));
        }

        // Add statics
        var allStatics = settings.WadGetAllStatics();
        foreach (var kvp in allStatics)
        {
            var staticMesh = kvp.Value;
            string name = staticMesh.ToString(gameVersion);

            bool multiple;
            var itemType = new ItemType(staticMesh.Id, settings);
            var wad = settings.WadTryGetWad(itemType, out multiple);
            string wadSource = wad != null ? Path.GetFileName(wad.Path) : "Unknown";

            AllItems.Add(new AssetItemViewModel(staticMesh, name, AssetCategory.Statics, wadSource, multiple));
        }

        // Add imported geometries
        foreach (var geo in settings.ImportedGeometries)
        {
            if (geo.LoadException != null)
                continue;

            string name = string.IsNullOrEmpty(geo.Info.Name)
                ? Path.GetFileNameWithoutExtension(geo.Info.Path) ?? "Unnamed"
                : geo.Info.Name;

            string wadSource = !string.IsNullOrEmpty(geo.Info.Path)
                ? Path.GetFileName(geo.Info.Path)
                : "Inline";

            AllItems.Add(new AssetItemViewModel(geo, name, AssetCategory.ImportedGeometry, wadSource, false));
        }

        FilteredItems.Refresh();
        OnPropertyChanged(nameof(ItemCount));

        // Try to restore previous selection
        if (previousSelection != null)
        {
            SelectedItem = AllItems.FirstOrDefault(i => ReferenceEquals(i.WadObject, previousSelection))
                ?? AllItems.FirstOrDefault(i => i.WadObject.Id != null && previousSelection.Id != null
                    && i.WadObject.Id.GetType() == previousSelection.Id.GetType()
                    && i.WadObject.Id.CompareTo(previousSelection.Id) == 0);
        }

        // Apply cached thumbnails and request rendering for uncached items
        bool needsRender = false;
        foreach (var item in AllItems)
        {
            if (_thumbnailCache.TryGetValue(item.CacheKey, out var cached))
            {
                item.Thumbnail = cached;
            }
            else
            {
                needsRender = true;
            }
        }

        if (needsRender)
            ThumbnailRenderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called by the host to set a rendered thumbnail for an item and cache it.
    /// </summary>
    public void SetThumbnail(AssetItemViewModel item, BitmapSource? thumbnail)
    {
        if (thumbnail == null) return;

        if (!thumbnail.IsFrozen)
            thumbnail.Freeze();

        item.Thumbnail = thumbnail;
        _thumbnailCache[item.CacheKey] = thumbnail;
    }

    /// <summary>
    /// Clears the entire thumbnail cache, forcing re-render on next refresh.
    /// </summary>
    public void InvalidateThumbnailCache()
    {
        _thumbnailCache.Clear();
        foreach (var item in AllItems)
            item.Thumbnail = null;
    }

    /// <summary>
    /// Gets all items that still need thumbnails rendered.
    /// </summary>
    public IEnumerable<AssetItemViewModel> GetItemsNeedingThumbnails()
    {
        return AllItems.Where(i => i.Thumbnail == null);
    }

    /// <summary>
    /// Requests a drag-drop operation for the given item.
    /// </summary>
    public void RequestDragDrop(AssetItemViewModel item)
    {
        DragDropRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not AssetItemViewModel item)
            return false;

        // Category filter
        if (SelectedCategory != AssetCategory.All && item.Category != SelectedCategory)
            return false;

        // Text search
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || item.WadSource.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
