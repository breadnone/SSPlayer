using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static SSPlayer.Win32;

namespace SSPlayer;

public sealed partial class MainWindow
{
    public void SetPlaylistPanelAlwaysOnTop() => Canvas.SetZIndex(_playlistPanel, 999);

    public async Task SkipNext()
    {
        try
        {
            if (_playlistCollection.Count == 0) return;

            int nextIndex = _currentPlaylistIndex + 1;

            // Safety & Loop Check
            if (nextIndex >= _playlistCollection.Count)
            {
                if (_currentSettings.RepeatPlaylist)
                    nextIndex = 0;
                else
                    return; // End of playlist
            }

            await PlayPlaylistIndex(nextIndex);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    public async Task SkipPrevious()
    {
        try
        {
            if (_playlistCollection.Count == 0) return;

            if (_player.MediaPlayer.PlaybackSession.Position.TotalSeconds > 3)
            {
                SeekUnified(TimeSpan.Zero);
                return;
            }

            int prevIndex = _currentPlaylistIndex - 1;

            if (prevIndex < 0)
            {
                if (_currentSettings.RepeatPlaylist)
                    prevIndex = _playlistCollection.Count - 1;
                else
                    prevIndex = 0;
            }

            await PlayPlaylistIndex(prevIndex);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    private async void OnImportPlaylist(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".sspl");
        var file = await picker.PickSingleFileAsync();
        if (MainTokenSource.IsCancellationRequested) return;

        if (file != null)
        {
            var items = JsonSerializer.Deserialize(await FileIO.ReadTextAsync(file), AppJsonContext.Default.ListPlaylistItem);
            if (MainTokenSource.IsCancellationRequested) return;

            if (items != null)
            {
                _playlistCollection.Clear();
                foreach (var item in items)
                    if (File.Exists(item.Path))
                        _playlistCollection.Add(item);
                _lastPlaylistPath = file.Path;

                try { File.WriteAllText(_lastPlaylistPathFile, _lastPlaylistPath); } catch { }
            }
        }
    }

    private async Task OnOpenMultipleFiles()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            foreach (var ext in _supportedExtensions) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0 || MainTokenSource.IsCancellationRequested) return;
            string firstpath = string.Empty;

            foreach (var file in files)
            {
                if (file == null) continue;

                if (string.IsNullOrEmpty(firstpath))
                {
                    firstpath = file.Path;
                }

                var au = await file.Properties.GetMusicPropertiesAsync();
                if (MainTokenSource.IsCancelled()) return;
                var vid = await file.Properties.GetVideoPropertiesAsync();
                if (MainTokenSource.IsCancelled()) return;
                var img = await file.Properties.GetImagePropertiesAsync();
                if (MainTokenSource.IsCancelled()) return;

                if (vid != null || au != null || img != null)
                {
                    await AddToPlaylist(file);
                    if (MainTokenSource.IsCancelled()) return;
                }
                else
                {
                    continue;
                }
            }
            if (string.IsNullOrEmpty(_lastPlaylistPath))
            {
                // 1. Get the path to %AppData%/Local
                string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(localFolder, "SSPlayer");
                Directory.CreateDirectory(appFolder);
                _lastPlaylistPath = Path.Combine(appFolder, "autosave.sspl");
                Log.Print(_lastPlaylistPath);
            }
            try { File.WriteAllText(_lastPlaylistPathFile, _lastPlaylistPath); } catch { }
            AutoSavePlaylist();
            if (_player.Source == null)
            {
                await PlayItemByPath(firstpath, InternalPlayStatus.None);
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private void HandlePlaylistHover(PointerRoutedEventArgs e)
    {
        if (this.Content is not FrameworkElement root) return;
        var ptr = e.GetCurrentPoint(root);
        double mouseX = ptr.Position.X;
        if (mouseX <= 120) { if (!_isPlaylistVisible) ShowPlaylist(); }
        else if (_isPlaylistVisible && mouseX > PLAYLIST_WIDTH + 10) { HidePlaylist(); }
    }
    private void ShowPlaylist()
    {
        if (IsPointerInControlOverlay) return;

        if (_emptyStateText != null)
        {
            Canvas.SetZIndex(_emptyStateText, 5);
        }

        SetPlaylistPanelAlwaysOnTop();

        _isPlaylistVisible = true;
        _playlistPanel.Translation = new System.Numerics.Vector3(0, 0, 0);
        _playlistPanel.Opacity = 1;
        _controlOverlay.Visibility = Visibility.Collapsed;

        RefreshPlaylistUI();
    }
    private void HidePlaylist()
    {
        if (IsPointerInPlaylistOverlay) return;

        if (_emptyStateText != null)
            Canvas.SetZIndex(_emptyStateText, 500);

        _isPlaylistVisible = false;
        _playlistPanel.Translation = new System.Numerics.Vector3((float)-_playlistPanel.Width, 0, 0);
        _playlistPanel.Opacity = 0;
        _controlOverlay.Visibility = Visibility.Visible;
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlaylistItem))]
    private async Task AddToPlaylist(StorageFile file, TimeSpan knownDuration = default, bool clearPlayList = false)
    {
        if (clearPlayList)
            _playlistCollection.Clear();
        else
            if (_playlistCollection.Any(p => p.Path == file.Path)) return;

        var item = new PlaylistItem { Title = file.Name, Path = file.Path };

        if (knownDuration != default)
            item.Duration = knownDuration;
        else
        {
            try
            {
                var clip = await MediaClip.CreateFromFileAsync(file);
                if (MainTokenSource.IsCancellationRequested) return;
                item.Duration = clip.OriginalDuration;
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        }

        _playlistCollection.Add(item);
    }
    private async void OnExportPlaylist(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeChoices.Add("SSPlayer Playlist", new List<string> { ".sspl" });
            picker.SuggestedFileName = "playlist_" + DateTime.Now.ToString("yyyyMMdd");
            var file = await picker.PickSaveFileAsync();
            if (MainTokenSource.IsCancellationRequested) return;

            if (file != null)
            {
                await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(_playlistCollection.ToList(), AppJsonContext.Default.ListPlaylistItem));
                if (MainTokenSource.IsCancellationRequested) return;
                _lastPlaylistPath = file.Path;
                try { File.WriteAllText(_lastPlaylistPathFile, _lastPlaylistPath); } catch { }
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    private void AutoSavePlaylist()
    {
        if (string.IsNullOrEmpty(_lastPlaylistPath)) return;
        try
        {
            File.WriteAllText(_lastPlaylistPath, JsonSerializer.Serialize(_playlistCollection.ToList(), AppJsonContext.Default.ListPlaylistItem));
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private async Task LoadLastPlaylistAsync()
    {
        try
        {
            if (!File.Exists(_lastPlaylistPathFile)) return;

            string path = File.ReadAllText(_lastPlaylistPathFile).Trim();

            if (!File.Exists(path)) return;

            _lastPlaylistPath = path;
            var items = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.ListPlaylistItem);

            if (items != null)
            {
                _playlistCollection.Clear();

                foreach (var item in items)
                    if (item.Path.StartsWith("cdda:", StringComparison.OrdinalIgnoreCase) || File.Exists(item.Path))
                        _playlistCollection.Add(item);

                if (_playlistCollection.Count > 0)
                {
                    _currentPlaylistIndex = 0;
                    await PlayItemByPath(_playlistCollection[0].Path, InternalPlayStatus.None);
                    if (MainTokenSource.IsCancellationRequested) return;
                }
            }

            StartLazyThumbnailLoading();
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
}