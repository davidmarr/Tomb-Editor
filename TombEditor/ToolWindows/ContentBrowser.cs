using DarkUI.Docking;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using TombEditor.ViewModels;
using TombLib.Controls;
using TombLib.GeometryIO;
using TombLib.LevelData;
using TombLib.Utils;
using TombLib.Wad;

namespace TombEditor.ToolWindows
{
    public partial class ContentBrowser : DarkToolWindow
    {
        private readonly Editor _editor;
        private readonly ContentBrowserViewModel _viewModel;
        private OffscreenItemRenderer _renderer;

        private Timer _thumbnailTimer;
        private List<AssetItemViewModel> _thumbnailQueue;
        private int _thumbnailQueueIndex;

        private bool _suppressEditorSync;

        private const int ThumbnailBatchSize = 10;

        public ContentBrowser()
        {
            InitializeComponent();

            _editor = Editor.Instance;
            _viewModel = new ContentBrowserViewModel();

            // Set the WPF view's DataContext.
            contentBrowserView.DataContext = _viewModel;

            _viewModel.SelectedItemsChanged += ViewModel_SelectedItemsChanged;
            _viewModel.DragDropRequested += ViewModel_DragDropRequested;
            _viewModel.ThumbnailRenderRequested += ViewModel_ThumbnailRenderRequested;
            _viewModel.LocateItemRequested += ViewModel_LocateItemRequested;
            _viewModel.AddItemRequested += ViewModel_AddItemRequested;
            _viewModel.AddWadRequested += ViewModel_AddWadRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Load saved tile width from configuration.
            _viewModel.TileWidth = _editor.Configuration.ContentBrowser_TileWidth;

            // Timer for batched/deferred thumbnail rendering (50ms between batches).
            _thumbnailTimer = new Timer { Interval = 50 };
            _thumbnailTimer.Tick += ThumbnailTimer_Tick;

            // Accept file drops from Windows Explorer (WAD files and 3D geometry files).
            AllowDrop = true;
            contentBrowserView.FilesDropped += ContentBrowserView_FilesDropped;

            // Subscribe to editor events.
            _editor.EditorEventRaised += EditorEventRaised;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _editor.EditorEventRaised -= EditorEventRaised;
                _viewModel.SelectedItemsChanged -= ViewModel_SelectedItemsChanged;
                _viewModel.DragDropRequested -= ViewModel_DragDropRequested;
                _viewModel.ThumbnailRenderRequested -= ViewModel_ThumbnailRenderRequested;
                _viewModel.LocateItemRequested -= ViewModel_LocateItemRequested;
                _viewModel.AddItemRequested -= ViewModel_AddItemRequested;
                _viewModel.AddWadRequested -= ViewModel_AddWadRequested;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

                contentBrowserView.FilesDropped -= ContentBrowserView_FilesDropped;

                _thumbnailTimer?.Stop();
                _thumbnailTimer?.Dispose();
                _thumbnailTimer = null;

                _renderer?.Dispose();
                _renderer = null;
            }

            if (disposing && components != null)
                components.Dispose();

            base.Dispose(disposing);
        }

        // Delegates drag-drop to WinForms host. Single item passes IWadObject directly.
        // multiple items are wrapped in an IWadObject[] array.
        private void ViewModel_DragDropRequested(object sender, IReadOnlyList<AssetItemViewModel> items)
        {
            if (items == null || items.Count == 0)
                return;

            if (items.Count == 1)
            {
                // Single item: pass IWadObject directly for backward compatibility.
                if (items[0].WadObject != null)
                    DoDragDrop(items[0].WadObject, DragDropEffects.Copy);
            }
            else
            {
                // Multiple items: pass as IWadObject array.
                var wadObjects = items.Where(i => i.WadObject != null).Select(i => i.WadObject).ToArray();

                if (wadObjects.Length > 0)
                    DoDragDrop(wadObjects, DragDropEffects.Copy);
            }
        }

        private void ViewModel_LocateItemRequested(object sender, AssetItemViewModel item)
        {
            if (item == null)
                return;

            if (item.WadObject is ImportedGeometry geo)
            {
                EditorActions.FindImportedGeometry(geo);
            }
            else if (item.WadObject is WadMoveable || item.WadObject is WadStatic)
            {
                if (item.WadObject is WadMoveable moveable)
                    _editor.ChosenItem = new ItemType(moveable.Id, _editor?.Level?.Settings);
                else if (item.WadObject is WadStatic staticMesh)
                    _editor.ChosenItem = new ItemType(staticMesh.Id, _editor?.Level?.Settings);

                EditorActions.FindItem();
            }

            // Scroll to make the item visible in the list.
            contentBrowserView.ScrollToItem(item);
        }

        private void ViewModel_AddItemRequested(object sender, EventArgs e)
        {
            var selected = _viewModel.SelectedItem;
            if (selected == null)
                return;

            if (selected.WadObject is ImportedGeometry)
                _editor.Action = new EditorActionPlace(false, (l, r) => new ImportedGeometryInstance());
            else
                CommandHandler.GetCommand("AddItem").Execute?.Invoke(new CommandArgs { Editor = _editor, Window = FindForm() });

            // If the action was not set (e.g. validation failed), restore the tile animation immediately.
            if (_editor.Action is not EditorActionPlace)
                contentBrowserView.RestoreLastAnimation();
        }

        private void ViewModel_AddWadRequested(object sender, EventArgs e)
        {
            EditorActions.AddWad(this, null);
        }

        // Handles files dropped from Windows Explorer: WAD files are loaded as object archives.
        // 3D geometry files are added as imported geometry.
        private void ContentBrowserView_FilesDropped(object sender, string[] files)
        {
            if (files == null || files.Length == 0)
                return;

            var wadFiles = files
                .Where(f => Wad2.FileExtensions.Matches(f))
                .Select(f => _editor.Level.Settings.MakeRelative(f, VariableType.LevelDirectory))
                .ToList();

            if (wadFiles.Count > 0)
                EditorActions.AddWad(this, wadFiles);

            foreach (var file in files.Where(f => BaseGeometryImporter.FileExtensions.Matches(f)))
                EditorActions.AddImportedGeometry(this, _editor.Level.Settings.MakeRelative(file, VariableType.LevelDirectory));
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ContentBrowserViewModel.TileWidth))
                _editor.Configuration.ContentBrowser_TileWidth = _viewModel.TileWidth;
        }

        private void ViewModel_ThumbnailRenderRequested(object sender, EventArgs e)
        {
            // Build/rebuild the queue from items that still need thumbnails.
            _thumbnailQueue = _viewModel.GetItemsNeedingThumbnails().ToList();
            _thumbnailQueueIndex = 0;

            if (_thumbnailQueue.Count > 0)
            {
                _thumbnailTimer?.Stop();
                _thumbnailTimer?.Start();
            }
        }

        private void ThumbnailTimer_Tick(object sender, EventArgs e)
        {
            if (_thumbnailQueue == null || _thumbnailQueueIndex >= _thumbnailQueue.Count)
            {
                // All items rendered; stop timer and clean up.
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
                _renderer?.GarbageCollect();
                return;
            }

            RenderThumbnailBatch();
        }

        // Renders a small batch of thumbnails (up to ThumbnailBatchSize).
        // Called from the timer tick so D3D11 rendering stays on the UI thread.
        private void RenderThumbnailBatch()
        {
            try
            {
                if (_renderer == null)
                    _renderer = new OffscreenItemRenderer();

                int thumbPixelSize = Math.Max(64, (int)(_viewModel.ThumbSize * 2));
                int end = Math.Min(_thumbnailQueueIndex + ThumbnailBatchSize, _thumbnailQueue.Count);

                for (int i = _thumbnailQueueIndex; i < end; i++)
                {
                    var item = _thumbnailQueue[i];
                    try
                    {
                        // Skip items that already got a thumbnail (e.g. from cache restore).
                        if (item.Thumbnail != null)
                            continue;

                        // Apply Lara skin substitution for moveables (shared with ItemBrowser).
                        var renderObject = WadObjectRenderHelper.GetRenderObject(item.WadObject, _editor.Level.Settings);

                        var image = _renderer.RenderThumbnail(renderObject, _editor.Level.Settings.GameVersion, _editor.Configuration.UI_ColorScheme.Color3DBackground);
                        var bitmapSource = AssetItemViewModel.ImageCToBitmapSource(image);
                        _viewModel.SetThumbnail(item, bitmapSource);
                    }
                    catch
                    {
                        // Silently skip items that fail to render (e.g. missing meshes).
                    }
                }

                _thumbnailQueueIndex = end;
            }
            catch
            {
                // If renderer creation fails, stop rendering.
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
                _renderer?.Dispose();
                _renderer = null;
            }
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            // Refresh asset list when wads, geometries, or game version change.
            if (obj is Editor.LoadedWadsChangedEvent ||
                obj is Editor.LoadedImportedGeometriesChangedEvent ||
                obj is Editor.GameVersionChangedEvent ||
                obj is Editor.LevelChangedEvent)
            {
                // Invalidate thumbnail cache when wads change since meshes may differ.
                if (obj is Editor.LoadedWadsChangedEvent ||
                    obj is Editor.LoadedImportedGeometriesChangedEvent)
                {
                    // Stop any in-progress rendering.
                    _thumbnailTimer?.Stop();
                    _thumbnailQueue = null;
                    _thumbnailQueueIndex = 0;

                    _viewModel.InvalidateThumbnailCache();
                    _renderer?.Dispose();
                    _renderer = null;
                }

                RefreshAssets();
            }

            // Also refresh on configuration change (game version display may change).
            if (obj is Editor.ConfigurationChangedEvent)
            {
                RefreshAssets();
            }

            // Sync selection when item is chosen from other tool windows.
            if (obj is Editor.ChosenItemChangedEvent itemChanged && !_suppressEditorSync)
            {
                if (itemChanged.Current.HasValue)
                    SyncSelectionFromEditor(itemChanged.Current.Value);
            }

            if (obj is Editor.ChosenImportedGeometryChangedEvent geoChanged && !_suppressEditorSync)
            {
                if (geoChanged.Current != null)
                    SyncImportedGeometrySelection(geoChanged.Current);
            }

            // Update keyboard shortcuts.
            if (obj is Editor.ConfigurationChangedEvent configChanged)
            {
                if (configChanged.UpdateKeyboardShortcuts)
                    CommandHandler.AssignCommandsToControls(_editor, this, toolTip, true);
            }

            // Restore tile animation when the place action ends (object placed or action canceled).
            if (obj is Editor.ActionChangedEvent actionEvent)
            {
                if (actionEvent.Previous is EditorActionPlace && actionEvent.Current is not EditorActionPlace)
                    contentBrowserView.RestoreLastAnimation();
            }

            // Activate default control.
            if (obj is Editor.DefaultControlActivationEvent activationEvent)
            {
                if (DockPanel != null && activationEvent.ContainerName == GetType().Name)
                    MakeActive();
            }

            // Initial load.
            if (obj is Editor.InitEvent)
                RefreshAssets();
        }

        private void RefreshAssets()
        {
            LevelSettings settings = _editor?.Level?.Settings;
            if (settings == null)
                return;

            _viewModel.RefreshAssets(settings, _editor.Configuration.RenderingItem_HideInternalObjects);
        }

        private void ViewModel_SelectedItemsChanged(object sender, IReadOnlyList<AssetItemViewModel> items)
        {
            // When the user clears the ContentBrowser selection, leave ChosenItem/ChosenImportedGeometry unchanged.
            // so the legacy item browser and imported geometry browser still show the last chosen item.

            if (items.Count == 0)
                return;

            var itemTypes = new List<ItemType>();
            ImportedGeometry singleGeo = null;

            foreach (var vm in items)
            {
                if (vm.WadObject is WadMoveable moveable)
                    itemTypes.Add(new ItemType(moveable.Id, _editor?.Level?.Settings));
                else if (vm.WadObject is WadStatic staticMesh)
                    itemTypes.Add(new ItemType(staticMesh.Id, _editor?.Level?.Settings));
                else if (vm.WadObject is ImportedGeometry geo && items.Count == 1)
                    singleGeo = geo;
            }

            _suppressEditorSync = true;

            // Preserve existing singular selections to maintain legacy workflow.
            if (itemTypes.Count > 0)
                _editor.ChosenItems = itemTypes;
            if (singleGeo != null)
                _editor.ChosenImportedGeometry = singleGeo;

            _suppressEditorSync = false;

            contentBrowserView.ScrollToItem(items[0]);
        }

        private void SyncSelectionFromEditor(ItemType item)
        {
            if (item.IsStatic)
            {
                var staticObj = _editor.Level.Settings.WadTryGetStatic(item.StaticId);
                if (staticObj != null)
                    SelectWadObject(staticObj);
            }
            else
            {
                var moveable = _editor.Level.Settings.WadTryGetMoveable(item.MoveableId);
                if (moveable != null)
                    SelectWadObject(moveable);
            }
        }

        private void SyncImportedGeometrySelection(ImportedGeometry geo)
        {
            SelectWadObject(geo);
        }

        private void SelectWadObject(IWadObject wadObject)
        {
            // Temporarily detach event to avoid feedback loop.
            _viewModel.SelectedItemsChanged -= ViewModel_SelectedItemsChanged;

            var item = _viewModel.AllItems.FirstOrDefault(i => ReferenceEquals(i.WadObject, wadObject));

            _viewModel.SelectedItem = item;
            contentBrowserView.SetSelectionSilently(item);
            contentBrowserView.ScrollToItem(item);

            _viewModel.SelectedItemsChanged += ViewModel_SelectedItemsChanged;
        }
    }
}
