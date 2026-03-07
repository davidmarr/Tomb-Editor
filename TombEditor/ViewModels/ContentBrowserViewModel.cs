#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TombLib.LevelData;
using TombLib.Utils;
using TombLib.Wad;
using TombLib.Wad.Catalog;

namespace TombEditor.ViewModels
{

    /// <summary>
    /// Categories for filtering assets in the Content Browser.
    /// </summary>
    public enum AssetCategory
    {
        All,
        Moveables,
        Statics,
        ImportedGeometry
    }

    /// <summary>
    /// Represents a single entry in the filter combobox.
    /// Can be a type filter (All/Moveables/Statics/ImportedGeometry),
    /// a category filter (from TrCatalog), or a visual splitter.
    /// </summary>
    public class FilterOption
    {
        /// <summary>
        /// Display name shown in the combobox.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// True if this is a type-based filter (All, Moveables, Statics, ImportedGeometry).
        /// </summary>
        public bool IsTypeFilter { get; }

        /// <summary>
        /// True if this is a non-selectable visual separator.
        /// </summary>
        public bool IsSplitter { get; }

        /// <summary>
        /// The type filter value, only meaningful when IsTypeFilter is true.
        /// </summary>
        public AssetCategory TypeFilter { get; }

        /// <summary>
        /// The category name for category-based filtering.
        /// Only meaningful when IsTypeFilter is false and IsSplitter is false.
        /// </summary>
        public string CategoryFilter { get; }

        private FilterOption(string displayName, bool isTypeFilter, bool isSplitter, AssetCategory typeFilter, string categoryFilter)
        {
            DisplayName = displayName;
            IsTypeFilter = isTypeFilter;
            IsSplitter = isSplitter;
            TypeFilter = typeFilter;
            CategoryFilter = categoryFilter;
        }

        public static FilterOption CreateTypeFilter(AssetCategory category, string displayName)
            => new FilterOption(displayName, true, false, category, string.Empty);

        public static FilterOption CreateSplitter()
            => new FilterOption(string.Empty, false, true, AssetCategory.All, string.Empty);

        public static FilterOption CreateCategoryFilter(string categoryName)
            => new FilterOption(categoryName, false, false, AssetCategory.All, categoryName);

        public override string ToString() => DisplayName;
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

        // Display name of the asset.
        public string Name { get; }

        // Category this asset belongs to.
        public AssetCategory Category { get; }

        // Category display name for grouping.
        public string CategoryName => Category switch
        {
            AssetCategory.Moveables => "Moveables",
            AssetCategory.Statics => "Statics",
            AssetCategory.ImportedGeometry => "Imported Geometry",
            _ => "Unknown"
        };

        // Name/path of the WAD file this asset was loaded from.
        public string WadSource { get; }

        // Whether this asset exists in multiple WAD files.
        public bool IsInMultipleWads { get; }

        // Catalog category string from TrCatalog (e.g. "Enemies", "Player").
        // May contain multiple values for special cases like "Shatterable".
        public string CatalogCategory { get; }

        // All effective categories including primary and any synthetic ones.
        public List<string> EffectiveCategories { get; } = new();

        // Color brush for placeholder thumbnail based on category.
        public SolidColorBrush ThumbnailBrush { get; }

        // Initials shown on the placeholder thumbnail.
        public string Initials { get; }

        // Sort order for type grouping (Moveables=0, Statics=1, ImportedGeometry=2).
        public int CategoryOrder => Category switch
        {
            AssetCategory.Moveables => 0,
            AssetCategory.Statics => 1,
            AssetCategory.ImportedGeometry => 2,
            _ => 3
        };

        // Backing field for thumbnail property.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasThumbnail))]
        private BitmapSource? _thumbnail;

        // Whether a rendered thumbnail has been set.
        public bool HasThumbnail => Thumbnail != null;

        // True while covered by rubber-band selection; purely visual feedback.
        [ObservableProperty]
        private bool _isRubberBandSelected;

        // Unique cache key for this asset's thumbnail.
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

        public AssetItemViewModel(IWadObject wadObject, string name, AssetCategory category, string wadSource, bool isInMultipleWads, string catalogCategory = "")
        {
            WadObject = wadObject;
            Name = name;
            Category = category;
            WadSource = wadSource;
            IsInMultipleWads = isInMultipleWads;
            CatalogCategory = catalogCategory;

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

        // Converts an ImageC (BGRA byte array) to a frozen WPF BitmapSource.
        public static BitmapSource? ImageCToBitmapSource(ImageC image)
        {
            if (image.Width == 0 || image.Height == 0)
                return null;

            byte[] data = image.ToByteArray();
            int stride = image.Width * 4; // BGRA = 4 bytes per pixel.
            var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, data, stride);
            bmp.Freeze();
            return bmp;
        }

        private static string BuildInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "?";

            var cleaned = name.Trim();

            // Skip leading "(number) " prefix, e.g. "(100) WOLF" -> "WOLF".
            if (cleaned.Length > 0 && cleaned[0] == '(')
            {
                int closeIdx = cleaned.IndexOf(')');
                if (closeIdx > 0 && closeIdx + 1 < cleaned.Length)
                {
                    cleaned = cleaned.Substring(closeIdx + 1).TrimStart();
                }
            }

            if (string.IsNullOrEmpty(cleaned))
                return "?";

            // Use the first letter of the cleaned name.
            return cleaned[0].ToString().ToUpperInvariant();
        }
    }

    /// <summary>
    /// Main ViewModel for the Content Browser tool window.
    /// Manages the collection of assets, search/filtering, and type-based sorting.
    /// </summary>
    public partial class ContentBrowserViewModel : ObservableObject
    {
        // Full collection of all asset items.
        public ObservableCollection<AssetItemViewModel> AllItems { get; } = new();

        // Filtered view of the items.
        public ICollectionView FilteredItems { get; }

        // Search text for filtering assets by name.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ItemCount))]
        private string _searchText = string.Empty;

        // Currently selected filter option (type or category).
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ItemCount))]
        private FilterOption? _selectedFilter;

        // Currently selected asset item.
        [ObservableProperty]
        private AssetItemViewModel? _selectedItem;

        // WAD source info for the selected item.
        [ObservableProperty]
        private string _selectedItemWadInfo = string.Empty;

        // Tile size (width) in pixels. Controls the grid tile dimensions.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TileHeight))]
        [NotifyPropertyChangedFor(nameof(ThumbSize))]
        private double _tileWidth = 88;

        // Computed tile height based on tile width (near-square with label allowance).
        public double TileHeight => TileWidth + 4;

        // Computed thumbnail square size based on tile width.
        public double ThumbSize => TileWidth * 0.78;

        // Minimum tile width.
        public double MinTileWidth => 60;

        // Maximum tile width.
        public double MaxTileWidth => 180;

        // Number of items currently visible after filtering.
        public int ItemCount => AllItems.Count(item => FilterPredicate(item));

        // Available filter options (type filters + optional splitter + category filters).
        public ObservableCollection<FilterOption> FilterOptions { get; } = new();

        // The default "All" filter option.
        private static readonly FilterOption AllFilter = FilterOption.CreateTypeFilter(AssetCategory.All, "All");

        public ContentBrowserViewModel()
        {
            FilteredItems = CollectionViewSource.GetDefaultView(AllItems);
            FilteredItems.Filter = FilterPredicate;

            // Initialize default type filters.
            FilterOptions.Add(AllFilter);
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.Moveables, "Moveables"));
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.Statics, "Statics"));
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.ImportedGeometry, "Imported Geometry"));
            SelectedFilter = AllFilter;

            // Use a custom comparer for natural (numeric-aware) sorting of asset names.
            // SortDescriptions uses lexicographic order which sorts (1), (10), (100), (2)...
            if (FilteredItems is ListCollectionView lcv)
            {
                lcv.CustomSort = new AssetItemComparer();
            }
            else
            {
                // Fallback for non-list views (shouldn't happen with ObservableCollection).
                FilteredItems.SortDescriptions.Add(new SortDescription(nameof(AssetItemViewModel.CategoryOrder), ListSortDirection.Ascending));
                FilteredItems.SortDescriptions.Add(new SortDescription(nameof(AssetItemViewModel.Name), ListSortDirection.Ascending));
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            FilteredItems.Refresh();
            OnPropertyChanged(nameof(ItemCount));
        }

        partial void OnSelectedFilterChanged(FilterOption? value)
        {
            // Prevent selecting splitter items.
            if (value != null && value.IsSplitter)
            {
                SelectedFilter = AllFilter;
                return;
            }

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

        // Event raised when the selected item changes; used by WinForms host to update Editor state.
        public event EventHandler<AssetItemViewModel?>? SelectedItemChanged;

        // Event raised when the selected items change (multi-selection).
        public event EventHandler<IReadOnlyList<AssetItemViewModel>>? SelectedItemsChanged;

        // Event raised when a drag-drop operation is requested; carries selected items.
        public event EventHandler<IReadOnlyList<AssetItemViewModel>>? DragDropRequested;

        // Event raised when thumbnails need rendering; host handles it on UI thread.
        public event EventHandler? ThumbnailRenderRequested;

        // Event raised when the user requests to locate a specific item (Alt+click).
        public event EventHandler<AssetItemViewModel>? LocateItemRequested;

        // Event raised when the user requests to add/place the selected item.
        public event EventHandler? AddItemRequested;

        // Event raised when the user requests to add a new WAD file.
        public event EventHandler? AddWadRequested;

        // Current list of all selected items (for multi-selection).
        public IReadOnlyList<AssetItemViewModel> SelectedItems { get; private set; } = Array.Empty<AssetItemViewModel>();

        // Updates the selected items from the view's ListBox selection.
        public void UpdateSelectedItems(IReadOnlyList<AssetItemViewModel> items)
        {
            SelectedItems = items;

            var first = items.Count > 0 ? items[0] : null;
            if (first != SelectedItem)
                SelectedItem = first;

            SelectedItemsChanged?.Invoke(this, items);
        }

        // In-memory thumbnail cache keyed by CacheKey.
        private readonly Dictionary<string, BitmapSource> _thumbnailCache = new();

        // Stored for use in FilterPredicate (set during RefreshAssets).
        private TRVersion.Game _gameVersion;
        private bool _hideInternalObjects;
        private bool _hasLoadedWads;

        // Whether any WADs are loaded in the level.
        public bool HasLoadedWads
        {
            get => _hasLoadedWads;
            private set
            {
                if (_hasLoadedWads != value)
                {
                    _hasLoadedWads = value;
                    OnPropertyChanged(nameof(HasLoadedWads));
                }
            }
        }

        /// <summary>
        /// Refreshes all assets from the current level settings.
        /// Builds moveable and static item lists in parallel for maximum performance.
        /// Also scans for catalog categories and populates filter options.
        /// </summary>
        public void RefreshAssets(LevelSettings settings, bool hideInternalObjects = false)
        {
            var previousSelection = SelectedItem?.WadObject;
            var previousFilterName = SelectedFilter?.DisplayName;

            var gameVersion = settings.GameVersion;
            bool isTombEngine = gameVersion == TRVersion.Game.TombEngine;

            _gameVersion = gameVersion;
            _hideInternalObjects = hideInternalObjects;

            // Fetch all objects upfront (these iterate wad files).
            var allMoveables = settings.WadGetAllMoveables();
            var allStatics = settings.WadGetAllStatics();

            // Build moveable and static items in parallel using thread-safe collections.
            // Each item construction is independent - name resolution, wad source lookup.
            // initials, and cache key are all pure/readonly operations.
            var moveableItems = new ConcurrentBag<AssetItemViewModel>();
            var staticItems = new ConcurrentBag<AssetItemViewModel>();

            var moveableTask = Task.Run(() =>
            {
                Parallel.ForEach(allMoveables, kvp =>
                {
                    var moveable = kvp.Value;
                    string name = moveable.ToString(gameVersion);

                    bool multiple;
                    var itemType = new ItemType(moveable.Id, settings);
                    var wad = settings.WadTryGetWad(itemType, out multiple);

                    string wadSource = wad != null ? Path.GetFileName(wad.Path) : "Unknown";
                    string catalogCategory = TrCatalog.GetMoveableCategory(gameVersion, moveable.Id.TypeId);

                    var item = new AssetItemViewModel(moveable, name, AssetCategory.Moveables, wadSource, multiple, catalogCategory);

                    // Add the primary catalog category.
                    if (!string.IsNullOrEmpty(catalogCategory))
                        item.EffectiveCategories.Add(catalogCategory);

                    moveableItems.Add(item);
                });
            });

            var staticTask = Task.Run(() =>
            {
                Parallel.ForEach(allStatics, kvp =>
                {
                    var staticMesh = kvp.Value;
                    string name = staticMesh.ToString(gameVersion);

                    bool multiple;
                    var itemType = new ItemType(staticMesh.Id, settings);
                    var wad = settings.WadTryGetWad(itemType, out multiple);
                    string wadSource = wad != null ? Path.GetFileName(wad.Path) : "Unknown";

                    string catalogCategory = TrCatalog.GetStaticCategory(gameVersion, staticMesh.Id.TypeId);

                    var item = new AssetItemViewModel(staticMesh, name, AssetCategory.Statics, wadSource, multiple, catalogCategory);

                    // Add the primary catalog category.
                    if (!string.IsNullOrEmpty(catalogCategory))
                        item.EffectiveCategories.Add(catalogCategory);

                    // Determine if this static is shatterable.
                    // WadStatic.Shatter flag takes priority for TombEngine, otherwise fall back to TrCatalog.IsStaticShatterable.
                    bool isShatterable = TrCatalog.IsStaticShatterable(gameVersion, staticMesh.Id.TypeId);

                    if (isTombEngine && staticMesh.Shatter)
                        isShatterable = true;

                    if (isShatterable && !item.EffectiveCategories.Contains("Shatterable"))
                        item.EffectiveCategories.Add("Shatterable");

                    // Also tolerate catalog category string "Shatterable" even if no shatterable flag.
                    if (string.Equals(catalogCategory, "Shatterable", StringComparison.OrdinalIgnoreCase) && !item.EffectiveCategories.Contains("Shatterable"))
                        item.EffectiveCategories.Add("Shatterable");

                    staticItems.Add(item);
                });
            });

            // Build imported geometry items (lightweight - no parallelization needed).
            var geoItems = new List<AssetItemViewModel>();
            foreach (var geo in settings.ImportedGeometries)
            {
                if (geo.LoadException != null)
                    continue;

                string name = string.IsNullOrEmpty(geo.Info.Name) ? Path.GetFileNameWithoutExtension(geo.Info.Path) ?? "Unnamed" : geo.Info.Name;
                string wadSource = !string.IsNullOrEmpty(geo.Info.Path) ? Path.GetFileName(geo.Info.Path) : "Inline";

                geoItems.Add(new AssetItemViewModel(geo, name, AssetCategory.ImportedGeometry, wadSource, false));
            }

            // Wait for parallel moveable and static building to complete.
            Task.WaitAll(moveableTask, staticTask);

            // Batch-populate the ObservableCollection (must happen on UI thread).
            AllItems.Clear();
            foreach (var item in moveableItems)
                AllItems.Add(item);
            foreach (var item in staticItems)
                AllItems.Add(item);
            foreach (var item in geoItems)
                AllItems.Add(item);

            // Track whether any WADs are loaded.
            HasLoadedWads = AllItems.Count > 0;

            // Build category list from all moveable and static items.
            var allCategories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in AllItems)
            {
                foreach (var cat in item.EffectiveCategories)
                {
                    if (!string.IsNullOrEmpty(cat))
                        allCategories.Add(cat);
                }
            }

            // Rebuild filter options.
            FilterOptions.Clear();
            FilterOptions.Add(AllFilter);
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.Moveables, "Moveables"));
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.Statics, "Statics"));
            FilterOptions.Add(FilterOption.CreateTypeFilter(AssetCategory.ImportedGeometry, "Imported Geometry"));

            if (allCategories.Count > 0)
            {
                FilterOptions.Add(FilterOption.CreateSplitter());
                foreach (var cat in allCategories)
                    FilterOptions.Add(FilterOption.CreateCategoryFilter(cat));
            }

            // Restore previous filter selection or default to All.
            SelectedFilter = FilterOptions.FirstOrDefault(f => f.DisplayName == previousFilterName && !f.IsSplitter) ?? AllFilter;

            FilteredItems.Refresh();
            OnPropertyChanged(nameof(ItemCount));

            // Try to restore previous selection.
            if (previousSelection != null)
            {
                SelectedItem = AllItems.FirstOrDefault(i => ReferenceEquals(i.WadObject, previousSelection))
                    ?? AllItems.FirstOrDefault(i => i.WadObject.Id != null && previousSelection.Id != null
                        && i.WadObject.Id.GetType() == previousSelection.Id.GetType()
                        && i.WadObject.Id.CompareTo(previousSelection.Id) == 0);
            }

            // Apply cached thumbnails and request rendering for uncached items.
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
            if (thumbnail == null)
                return;

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
        /// Requests a drag-drop operation for a list of selected items.
        /// </summary>
        public void RequestDragDrop(IReadOnlyList<AssetItemViewModel> items)
        {
            if (items.Count > 0)
                DragDropRequested?.Invoke(this, items);
        }

        /// <summary>
        /// Requests locating a specific item in the level (Alt+click).
        /// </summary>
        public void RequestLocateItem(AssetItemViewModel item)
        {
            LocateItemRequested?.Invoke(this, item);
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        [RelayCommand]
        private void LocateItem()
        {
            if (SelectedItem != null)
                LocateItemRequested?.Invoke(this, SelectedItem);
        }

        [RelayCommand]
        private void AddItem()
        {
            if (SelectedItem != null)
                AddItemRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void AddWad()
        {
            AddWadRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not AssetItemViewModel item)
                return false;

            // Hide internal/engine-only moveables when configured
            if (_hideInternalObjects &&
                item.Category == AssetCategory.Moveables &&
                item.WadObject is WadMoveable mov &&
                TrCatalog.IsHidden(_gameVersion, mov.Id.TypeId))
                return false;

            var filter = SelectedFilter;

            // Apply type or category filter
            if (filter != null && !filter.IsSplitter)
            {
                if (filter.IsTypeFilter)
                {
                    // Type filter: filter by asset type (All shows everything)
                    if (filter.TypeFilter != AssetCategory.All && item.Category != filter.TypeFilter)
                        return false;
                }
                else
                {
                    // Category filter: show all items matching this category regardless of type
                    if (!item.EffectiveCategories.Contains(filter.CategoryFilter, StringComparer.OrdinalIgnoreCase))
                        return false;
                }
            }

            // Text search
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                // Exact substring match
                if (item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;
                if (item.WadSource.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;

                // Fuzzy match using Levenshtein distance (for queries with 3+ chars)
                if (SearchText.Length >= 3)
                {
                    int endIndex;
                    int distance = Levenshtein.DistanceSubstring(item.Name.ToLowerInvariant(), SearchText.ToLowerInvariant(), out endIndex);
                    if (distance < 2)
                        return true;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Comparer for sorting asset items by type (CategoryOrder) then by name using
        /// natural numeric ordering so that e.g. (2) sorts before (10).
        /// </summary>
        private sealed class AssetItemComparer : IComparer
        {
            public int Compare(object? x, object? y)
            {
                if (x is not AssetItemViewModel a || y is not AssetItemViewModel b)
                    return 0;

                int catCmp = a.CategoryOrder.CompareTo(b.CategoryOrder);
                if (catCmp != 0)
                    return catCmp;

                return NaturalComparer.Do(a.Name, b.Name);
            }
        }
    }
}