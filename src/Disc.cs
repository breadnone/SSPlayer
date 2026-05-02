using Microsoft.UI.Xaml.Controls;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using static SSPlayer.Win32;

namespace SSPlayer;

public sealed partial class MainWindow
{

    private async Task LoadDvdFiles(string folderPath)
    {
        // DVDs store the main movie in VTS_XX_1.VOB, VTS_XX_2.VOB, etc.
        // We grab all VOBs that aren't the menu (usually larger than 10MB)
        var vobFiles = System.IO.Directory.GetFiles(folderPath, "*.VOB")
            .Select(f => new System.IO.FileInfo(f))
            .Where(f => f.Length > 10 * 1024 * 1024)
            .OrderBy(f => f.Name)
            .ToList();

        if (vobFiles.Any())
        {
            foreach (var vob in vobFiles)
            {
                var file = await StorageFile.GetFileFromPathAsync(vob.FullName);
                await AddToPlaylist(file);
            }

            await PlayItemByPath(vobFiles[0].FullName, InternalPlayStatus.None);

            Log.Print($"Loaded DVD Segment: {vobFiles[0].Name}");
        }
    }
    private async Task LoadAudioCdToPlaylist(string[] trackPaths)
    {
        string firstpath = string.Empty;

        for (int i = 0; i < trackPaths.Length; i++)
        {
            string cda = trackPaths[i];
            string driveLetter = System.IO.Path.GetPathRoot(cda).TrimEnd('\\'); // e.g. "D:"

            // Parse track number from filename — "Track01.cda" → 1
            string fileName = System.IO.Path.GetFileNameWithoutExtension(cda); // "Track01"
            string digits = new string(fileName.Where(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out int trackNumber) || trackNumber < 1)
                continue; // skip malformed filenames

            // WMF understands this URI format natively for CD audio
            string cdaUri = $"cdda:{driveLetter}/{trackNumber}";

            if (i == 0) firstpath = cdaUri;

            _playlistCollection.Add(new PlaylistItem
            {
                Title = $"CD Track {trackNumber}",
                Path = cdaUri,
                IsAudio = true
            });
        }

        if (!string.IsNullOrEmpty(firstpath))
            await PlayItemByPath(firstpath, InternalPlayStatus.None, isCd: true);
    }
    private async Task LoadPhysicalDiscAsync()
    {
        var drives = System.IO.DriveInfo.GetDrives();
        var driveHardware = drives.FirstOrDefault(d => d.DriveType == System.IO.DriveType.CDRom);

        if (driveHardware == null)
        {
            await ShowDiscErrorDialog("Hardware Missing", "No CD/DVD drive was found on this system.");
            return;
        }

        int attempts = 0;

        while (!driveHardware.IsReady && attempts < 14)
        {
            Log.Print($"Waiting for disc to be ready... attempt {attempts + 1}/14");
            await Task.Delay(1000); // Wait 1 second
            attempts++;
            driveHardware = System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.Name == driveHardware.Name);
        }

        if (!driveHardware.IsReady)
        {
            await ShowDiscErrorDialog("Empty Drive", "Please insert a disc into the drive.");
            return;
        }

        string drivePath = driveHardware.RootDirectory.FullName;
        string videoTsPath = System.IO.Path.Combine(drivePath, "VIDEO_TS");

        // 2. Identify Disc Type and Load
        if (System.IO.Directory.Exists(videoTsPath))
        {
            // It's a Video DVD
            await LoadDvdFiles(videoTsPath);
        }
        else
        {
            var cdaFiles = System.IO.Directory.GetFiles(drivePath, "*.cda");

            if (cdaFiles.Length > 0)
            {
                await LoadAudioCdToPlaylist(cdaFiles);
            }
            else
            {
                Log.Print("Disc format not recognized or empty.");
                await ShowDiscErrorDialog("Optical Disc Error", "Disc format not recognized or empty.");
            }
        }
    }
}