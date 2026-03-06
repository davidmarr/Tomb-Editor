using DarkUI.Docking;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using TombLib.Controls;
using TombLib.GeometryIO;
using TombLib.LevelData;
using TombLib.Utils;
using TombLib.Wad;
using TombEditor.ViewModels;

namespace TombEditor.ToolWindows
{
    /// <summary>
    /// Content Browser tool window — provides a grid-based asset explorer
    /// for all IWadObject assets (Moveables, Statics, ImportedGeometry).
    /// Hosts a WPF ContentBrowserView via ElementHost inside a DarkToolWindow.
    /// </summary>
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

            // Set the WPF view's DataContext
            contentBrowserView.DataContext = _viewModel;

            _viewModel.SelectedItemsChanged += ViewModel_SelectedItemsChanged;
            _viewModel.DragDropRequested += ViewModel_DragDropRequested;
            _viewModel.ThumbnailRenderRequested += ViewModel_ThumbnailRenderRequested;
            _viewModel.LocateItemRequested += ViewModel_LocateItemRequested;
            _viewModel.AddItemRequested += ViewModel_AddItemRequested;
            _viewModel.AddWadRequested += ViewModel_AddWadRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Load saved tile width from configuration
            _viewModel.TileWidth = _editor.Configuration.ContentBrowser_TileWidth;

            // Timer for batched/deferred thumbnail rendering (50ms between batches)
            _thumbnailTimer = new Timer { Interval = 50 };
            _thumbnailTimer.Tick += ThumbnailTimer_Tick;

            // Accept file drops from Windows Explorer (WAD files and 3D geometry files)
            AllowDrop = true;
            contentBrowserView.FilesDropped += ContentBrowserView_FilesDropped;

            // Subscribe to editor events
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

        /// <summary>
        /// Handles drag-drop requests from the WPF view.
        /// Uses WinForms Control.DoDragDrop which is compatible with
        /// Panel3D's OnDragDrop handler (expects IWadObject via GetFormats()[0]).
        /// For multiple items, wraps them in an IWadObject[] array.
        /// </summary>
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

        /// <summary>
        /// Handles locate item requests from Alt+click in the WPF view.
        /// </summary>
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

            // Scroll to make the item visible in the list
            contentBrowserView.ScrollToItem(item);
        }

        /// <summary>
        /// Handles add item requests from the WPF view (double-click / context menu).
        /// Reuses the existing "AddItem" command for moveables/statics.
        /// </summary>
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

        /// <summary>
        /// Handles add WAD requests from the empty state message.
        /// </summary>
        private void ViewModel_AddWadRequested(object sender, EventArgs e)
        {
            EditorActions.AddWad(this, null);
        }

        /// <summary>
        /// Handles files dropped from Windows Explorer onto the Content Browser.
        /// Accepts WAD files (loaded as object archives) and 3D geometry files
        /// (added as imported geometry), matching what ItemBrowser and
        /// ImportedGeometryBrowser support via the global drag-drop handler.
        /// </summary>
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

        /// <summary>
        /// Saves tile width to configuration when it changes.
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ContentBrowserViewModel.TileWidth))
                _editor.Configuration.ContentBrowser_TileWidth = _viewModel.TileWidth;
        }

        /// <summary>
        /// Queues thumbnail rendering when assets change.
        /// Builds the queue of items needing thumbnails and starts the batched timer.
        /// </summary>
        private void ViewModel_ThumbnailRenderRequested(object sender, EventArgs e)
        {
            // Build/rebuild the queue from items that still need thumbnails,
            _thumbnailQueue = _viewModel.GetItemsNeedingThumbnails().ToList();
            _thumbnailQueueIndex = 0;

            if (_thumbnailQueue.Count > 0)
            {
                _thumbnailTimer?.Stop();
                _thumbnailTimer?.Start();
            }
        }

        /// <summary>
        /// Renders the next batch of thumbnails on each timer tick.
        /// Keeps the UI responsive by only processing ThumbnailBatchSize items per tick.
        /// </summary>
        private void ThumbnailTimer_Tick(object sender, EventArgs e)
        {
            if (_thumbnailQueue == null || _thumbnailQueueIndex >= _thumbnailQueue.Count)
            {
                // All items rendered — stop timer and clean up.
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
                _renderer?.GarbageCollect();
                return;
            }

            RenderThumbnailBatch();
        }

        /// <summary>
        /// Renders a small batch of thumbnails (up to ThumbnailBatchSize).
        /// Called from the timer tick so D3D11 rendering stays on the UI thread.
        /// </summary>
        private void RenderThumbnailBatch()
        {
            try
            {
                if (_renderer == null)
                    _renderer = new OffscreenItemRenderer();

                _renderer.ClearColor = _editor.Configuration.UI_ColorScheme.Color3DBackground;

                int thumbPixelSize = Math.Max(64, (int)(_viewModel.ThumbSize * 2));
                int end = Math.Min(_thumbnailQueueIndex + ThumbnailBatchSize, _thumbnailQueue.Count);

                for (int i = _thumbnailQueueIndex; i < end; i++)
                {
                    var item = _thumbnailQueue[i];
                    try
                    {
                        // Skip items that already got a thumbnail (e.g., from cache restore)
                        if (item.Thumbnail != null)
                            continue;

                        // Apply Lara skin substitution for moveables (shared with ItemBrowser)
                        var renderObject = WadObjectRenderHelper.GetRenderObject(item.WadObject, _editor.Level.Settings);

                        var image = _renderer.RenderThumbnail(renderObject, thumbPixelSize);
                        var bitmapSource = AssetItemViewModel.ImageCToBitmapSource(image);
                        _viewModel.SetThumbnail(item, bitmapSource);
                    }
                    catch
                    {
                        // Silently skip items that fail to render (e.g., missing meshes)
                    }
                }

                _thumbnailQueueIndex = end;
            }
            catch
            {
                // If renderer creation fails, stop rendering
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
                _renderer?.Dispose();
                _renderer = null;
            }
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            // Refresh asset list when wads, geometries, or game version change
            if (obj is Editor.LoadedWadsChangedEvent ||
                obj is Editor.LoadedImportedGeometriesChangedEvent ||
                obj is Editor.GameVersionChangedEvent ||
                obj is Editor.LevelChangedEvent)
            {
                // Invalidate thumbnail cache when wads change since meshes may differ
                if (obj is Editor.LoadedWadsChangedEvent ||
                    obj is Editor.LoadedImportedGeometriesChangedEvent)
                {
                    // Stop any in-progress rendering
                    _thumbnailTimer?.Stop();
                    _thumbnailQueue = null;
                    _thumbnailQueueIndex = 0;

                    _viewModel.InvalidateThumbnailCache();
                    _renderer?.Dispose();
                    _renderer = null;
                }

                RefreshAssets();
            }

            // Also refresh on configuration change (game version display may change)
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

            // Update keyboard shortcuts
            if (obj is Editor.ConfigurationChangedEvent configChanged)
            {
                if (configChanged.UpdateKeyboardShortcuts)
                    CommandHandler.AssignCommandsToControls(_editor, this, toolTip, true);
            }

            // Restore tile animation when the place action ends (object placed or action canceled)
            if (obj is Editor.ActionChangedEvent actionEvent)
            {
                if (actionEvent.Previous is EditorActionPlace && actionEvent.Current is not EditorActionPlace)
                    contentBrowserView.RestoreLastAnimation();
            }

            // Activate default control
            if (obj is Editor.DefaultControlActivationEvent activationEvent)
            {
                if (DockPanel != null && activationEvent.ContainerName == GetType().Name)
                {
                    MakeActive();
                }
            }

            // Initial load
            if (obj is Editor.InitEvent)
            {
                RefreshAssets();
            }
        }

        private void RefreshAssets()
        {
            LevelSettings settings = _editor?.Level?.Settings;
            if (settings == null)
                return;

            _viewModel.RefreshAssets(settings, _editor.Configuration.RenderingItem_HideInternalObjects);
        }

        /// <summary>
        /// Updates the Editor's ChosenItems / ChosenImportedGeometry for every selection change
        /// (single item or multi-selection). This is the single authoritative path —
        /// the legacy SelectedItemChanged event is no longer subscribed.
        /// <para>
        /// Differentiation rules:
        /// <list type="bullet">
        ///   <item>One or more moveables/statics → set ChosenItems, clear ChosenImportedGeometry.</item>
        ///   <item>Exactly one ImportedGeometry → set ChosenImportedGeometry, clear ChosenItems.</item>
        ///   <item>Empty → no-op; ChosenItem/ChosenImportedGeometry keep their last value so that
        ///         the item browser never shows an empty selection after a deselect in ContentBrowser.</item>
        /// </list>
        /// Mixed selections (geo + moveables) populate ChosenItems from the moveable/static subset
        /// and clear ChosenImportedGeometry, matching the behaviour of the placement tools.
        /// </para>
        /// </summary>
        private void ViewModel_SelectedItemsChanged(object sender, IReadOnlyList<AssetItemViewModel> items)
        {
            // When the user clears the ContentBrowser selection, leave ChosenItem/ChosenImportedGeometry
            // unchanged so the legacy item browser and imported geometry browser still show the last
            // chosen item. The ContentBrowser itself shows no visual selection (empty), which is the
            // intended UX: the user deliberately deselected everything here.
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
            
            // HACK: Preserve existing singular selections to maintain legacy workflow.
            if (itemTypes.Count > 0)
                _editor.ChosenItems = itemTypes;
            if (singleGeo != null)
                _editor.ChosenImportedGeometry = singleGeo;

            _suppressEditorSync = false;

            contentBrowserView.ScrollToItem(items[0]);
        }

        /// <summary>
        /// Syncs the Content Browser selection when an item is chosen from
        /// another tool window (e.g., ItemBrowser).
        /// </summary>
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

        /// <summary>
        /// Syncs the Content Browser selection when an imported geometry is chosen
        /// from another tool window.
        /// </summary>
        private void SyncImportedGeometrySelection(ImportedGeometry geo)
        {
            SelectWadObject(geo);
        }

        private void SelectWadObject(IWadObject wadObject)
        {
            // Temporarily detach event to avoid feedback loop.
            _viewModel.SelectedItemsChanged -= ViewModel_SelectedItemsChanged;

            var item = _viewModel.AllItems
                .FirstOrDefault(i => ReferenceEquals(i.WadObject, wadObject));
            _viewModel.SelectedItem = item;
            contentBrowserView.SetSelectionSilently(item);
            contentBrowserView.ScrollToItem(item);

            _viewModel.SelectedItemsChanged += ViewModel_SelectedItemsChanged;
        }
    }
}
