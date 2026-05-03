using Microsoft.UI.Xaml.Controls;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using WinRT;

namespace SSPlayer;

public partial class AudioEngine
{
    private float[] _audioBuffer = new float[1024];
    public VisualizerMode Mode { get; set; } = VisualizerMode.PulseRings;
    private AudioGraph _audioGraph;
    private MediaSourceAudioInputNode _fileInputNode;
    private AudioDeviceOutputNode _deviceOutputNode;
    private AudioFrameOutputNode _frameOutputNode;
    public AudioGraph GetAudioGraph() => _audioGraph;
    public MediaSourceAudioInputNode GetFileInputNode() => _fileInputNode;
    private float[] _sharedBuffer = new float[1024];
    private readonly object _lock = new object();
    private Thread _processingThread;
    private bool _isRunning = true;
    private int _quantaProcessed = 0;
    private const int WarmupThreshold = 20;
    private float _targetVolume = 1.0f;
    private AudioSubmixNode _submixNode;
    private EqualizerEffectDefinition _eqEffect;
    private readonly double[] _eqFrequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public bool wasPaused { get; private set; }
    private MediaPlayer mediaPlayer;
    public TimeSpan NaturalDuration => _fileInputNode?.Duration ?? TimeSpan.Zero;
    public TimeSpan Position => _fileInputNode?.Position ?? TimeSpan.Zero;
    public AudioEngine()
    {
    }
    public void Dispose()
    {
        _isRunning = false;

        if (_audioGraph != null)
        {
            _audioGraph.QuantumStarted -= QuantumStarted;
            _audioGraph.Stop();
        }

        _fileInputNode?.Dispose();
        _deviceOutputNode?.Dispose();
        _submixNode?.Dispose();
        _frameOutputNode?.Dispose();

        foreach (var node in _eqNodes) try { node?.Dispose(); } catch { }
        _eqNodes.Clear();
        _eqChain.Clear();

        _fileInputNode = null;
        _deviceOutputNode = null;
        _submixNode = null;
        _frameOutputNode = null;

        _audioGraph?.Dispose();
        _audioGraph = null;
    }
    public async Task<bool> SetupAudioEngine(StorageFile file, MediaPlayerElement _player, double volumeValue, EqualizerSettings equalizer)
    {
        try
        {
            if (_audioGraph != null)
            {
                _audioGraph.QuantumStarted -= QuantumStarted;
                _audioGraph.Stop();

                _fileInputNode?.Dispose();
                _deviceOutputNode?.Dispose();
                _submixNode?.Dispose();
                _frameOutputNode?.Dispose();

                _fileInputNode = null;
                _deviceOutputNode = null;
                _submixNode = null;
                _frameOutputNode = null;

                _audioGraph.ResetAllNodes();
                _audioGraph.Dispose();
                _audioGraph = null;
            }

            mediaPlayer = _player.MediaPlayer;
            _targetVolume = (float)(volumeValue / 100.0);

            var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);
            settings.QuantumSizeSelectionMode = QuantumSizeSelectionMode.SystemDefault;
            await Task.Delay(100);
            var result = await AudioGraph.CreateAsync(settings);

            if (result.Status != AudioGraphCreationStatus.Success) return false;

            _audioGraph = result.Graph;
            _audioGraph.ResetAllNodes();

            var outputResult = await _audioGraph.CreateDeviceOutputNodeAsync();
            _deviceOutputNode = outputResult.DeviceOutputNode;
            var hardwareProps = _deviceOutputNode.EncodingProperties;

            _submixNode = _audioGraph.CreateSubmixNode(hardwareProps);
            _frameOutputNode = _audioGraph.CreateFrameOutputNode(hardwareProps);
            var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(MediaSource.CreateFromStorageFile(file));

            if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success) return false;

            _fileInputNode = inputResult.Node;
            _fileInputNode.AddOutgoingConnection(_submixNode);
            _submixNode.AddOutgoingConnection(_deviceOutputNode);
            _submixNode.AddOutgoingConnection(_frameOutputNode);

            SetupEqualizer(equalizer);
            _audioGraph.QuantumStarted += QuantumStarted;

            await Task.Delay(50);
            SetVolume(volumeValue);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is TaskCanceledException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                Log.Print("Warning : Token cancellation notification.");
            }
        }

        return false;
    }

    public void Seek(TimeSpan position)
    {
        if (_fileInputNode == null) return;

        TimeSpan duration = _fileInputNode.Duration;

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        else if (position > duration)
        {
            position = duration;
        }

        try
        {
            _fileInputNode.Seek(position);
        }
        catch (Exception ex)
        {
            Log.Print($"Seek failed: {ex.Message}");
        }
    }

    public void SetBandGain(int globalIdx, double gain, EqualizerSettings settings)
    {
        try
        {
            int chainIdx = globalIdx / 4;
            int bandIdx = globalIdx % 4;

            if (_eqChain == null || chainIdx >= _eqChain.Count) return;
            if (bandIdx >= _eqChain[chainIdx].Bands.Count) return;

            if (double.IsNaN(gain) || double.IsInfinity(gain)) gain = 1.0;
            gain = Math.Clamp(gain, _minLinearGain, _maxLinearGain);
            _eqChain[chainIdx].Bands[bandIdx].Gain = gain;

            if (settings != null && globalIdx < settings.BandGains.Count)
                settings.BandGains[globalIdx] = 20.0 * Math.Log10(gain);
        }
        catch (Exception ex) { Log.Print("SetBandGainError " + ex.Message); }
    }
    public void Start()
    {
        _audioGraph?.Start();
    }
    public void Pause()
    {
        _audioGraph?.Stop();
    }

    private int _processingFrame = 0;
    private void QuantumStarted(AudioGraph s, object e)
    {
        try
        {
            if (_frameOutputNode == null) return;
            AudioFrame frame;
            try { frame = _frameOutputNode.GetFrame(); }
            catch { return; }

            if (frame == null) return;

            if (Interlocked.CompareExchange(ref _processingFrame, 1, 0) == 0)
            {
                try { ProcessFrame(frame); }
                finally { Interlocked.Exchange(ref _processingFrame, 0); }
            }
            else
            {
                frame.Dispose();
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log.Print("SetBandGainError" + ex.Message); }
    }
    public void SetVolume(double volume)
    {
        if (_submixNode == null) return;

        float vol = (float)Math.Clamp(volume / 100.0, 0, 1);
        _submixNode.OutgoingGain = vol;
    }
    public double GetVolume() => _submixNode != null ? _submixNode.OutgoingGain : 0.0;
    public void ToggleMute(bool isMuted)
    {
        if (_deviceOutputNode != null)
        {
            _deviceOutputNode.ConsumeInput = !isMuted;
        }
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IMemoryBufferReference))]
    public unsafe void ProcessFrame(AudioFrame frame)
    {
        try
        {
            using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Read))
            using (IMemoryBufferReference reference = buffer.CreateReference())
            {
                var byteAccess = reference.As<IMemoryBufferByteAccess>();
                if (byteAccess == null) return;

                byteAccess.GetBuffer(out byte* dataInBytes, out uint capacity);
                float* dataInFloat = (float*)dataInBytes;
                int count = (int)capacity / sizeof(float);

                if (count == 0) return;

                lock (_lock)
                {
                    int currentBars = _visualizerPeaks.Length;
                    if (currentBars == 0) return;
                    int samplesPerBar = Math.Max(1, count / currentBars);

                    for (int b = 0; b < currentBars; b++)
                    {
                        float peak = 0;

                        for (int i = 0; i < samplesPerBar; i++)
                        {
                            int index = (b * samplesPerBar) + i;

                            if (index < count)
                            {
                                float sample = Math.Abs(dataInFloat[index]);
                                if (sample > peak) peak = sample;
                            }
                        }

                        if (peak > _visualizerPeaks[b])
                            _visualizerPeaks[b] = peak;
                    }
                }
            }

            frame.Dispose();
        }
        catch (ObjectDisposedException) { }
        catch(Exception exx)
        {
            Log.Print(exx.Message);
        }
    }

    private List<EqualizerEffectDefinition> _eqChain = new List<EqualizerEffectDefinition>();
    private List<AudioSubmixNode> _eqNodes = new List<AudioSubmixNode>();
    public void SetupEqualizer(EqualizerSettings savedSettings)
    {
        try
        {
            double[] freqs = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
            _eqChain.Clear();
            _eqNodes.Clear();

            _submixNode.RemoveOutgoingConnection(_deviceOutputNode);
            _submixNode.RemoveOutgoingConnection(_frameOutputNode);

            AudioSubmixNode lastNode = _submixNode;

            int bandsPerNode = new EqualizerEffectDefinition(_audioGraph).Bands.Count;
            int nodeCount = (int)Math.Ceiling((double)freqs.Length / bandsPerNode);

            for (int i = 0; i < nodeCount; i++)
            {
                var eqNode = _audioGraph.CreateSubmixNode();
                _eqNodes.Add(eqNode);

                var eqEffect = new EqualizerEffectDefinition(_audioGraph);

                for (int b = 0; b < eqEffect.Bands.Count; b++)
                {
                    int globalIdx = (i * (int)eqEffect.Bands.Count) + b;
                    if (globalIdx < freqs.Length)
                    {
                        eqEffect.Bands[b].FrequencyCenter = freqs[globalIdx];
                        eqEffect.Bands[b].Bandwidth = 0.8;

                        if (savedSettings != null && globalIdx < savedSettings.BandGains.Count)
                        {
                            double savedDb = savedSettings.BandGains[globalIdx];

                            // Guard: clamp dB to a sane range before Math.Pow — extreme values
                            // (e.g. NaN/Infinity smuggled in via JSON) would produce Infinity or 0
                            // which crashes the EQ effect or silences audio entirely.
                            if (double.IsNaN(savedDb) || double.IsInfinity(savedDb))
                                savedDb = 0.0;
                            savedDb = Math.Clamp(savedDb, -30.0, 30.0); // mirrors slider Min/Maximum

                            double linearGain = Math.Pow(10, savedDb / 20.0); // denominator is literal 20, never 0

                            // Guard: Math.Pow can still return NaN/Infinity if the runtime has
                            // a FPU edge case, and a 0 gain would hard-silence the band.
                            if (double.IsNaN(linearGain) || double.IsInfinity(linearGain) || linearGain <= 0.0)
                                linearGain = 1.0; // 1.0 = 0 dB = flat/neutral

                            eqEffect.Bands[b].Gain = Math.Clamp(linearGain, _minLinearGain, _maxLinearGain);
                        }
                        else
                            eqEffect.Bands[b].Gain = 1.0;
                    }
                }

                eqNode.EffectDefinitions.Add(eqEffect);
                _eqChain.Add(eqEffect);

                lastNode.AddOutgoingConnection(eqNode);
                lastNode = eqNode;
            }

            lastNode.AddOutgoingConnection(_deviceOutputNode);
            lastNode.AddOutgoingConnection(_frameOutputNode);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    public void SetEqBand(int bandIndex, double gainInDb)
    {
        if (_eqChain == null || _eqChain.Count == 0 || bandIndex < 0) return;

        if (double.IsNaN(gainInDb) || double.IsInfinity(gainInDb))
            gainInDb = 0.0;
        gainInDb = Math.Clamp(gainInDb, -30.0, 30.0);

        int bandsPerEffect = (int)_eqChain[0].Bands.Count;
        if (bandsPerEffect == 0) return; 

        int effectIdx = bandIndex / bandsPerEffect;
        int localIdx = bandIndex % bandsPerEffect;

        if (effectIdx >= _eqChain.Count) return;

        var band = _eqChain[effectIdx].Bands[localIdx];

        double linearGain = Math.Pow(10, gainInDb / 20.0);

        if (double.IsNaN(linearGain) || double.IsInfinity(linearGain) || linearGain <= 0.0)
            linearGain = 1.0;

        double safeGain = Math.Clamp(linearGain, _minLinearGain, _maxLinearGain);

        try { band.Gain = safeGain; }
        catch
        {
            Log.Print($"EQ Crash Prevented: (Val: {safeGain})");
            try { band.Gain = 1.0; } catch { }
        }
    }
    private double _minLinearGain = 0.13; 
    private double _maxLinearGain = 7.93;
    public double GetEqBandGain(int bandIndex)
    {
        if (_eqChain == null || _eqChain.Count == 0) return 0.0;
        int bandsPerEffect = _eqChain[0].Bands.Count;
        if (bandsPerEffect == 0) return 0.0;
        int effectIdx = bandIndex / bandsPerEffect;
        int localIdx = bandIndex % bandsPerEffect;
        if (effectIdx >= _eqChain.Count) return 0.0;
        return _eqChain[effectIdx].Bands[localIdx].Gain;
    }
    public async Task<bool> ExportAudioAs(StorageFile destinationFile, MediaEncodingProfile profile, Action<bool> onComplete, TimeSpan? start = null, TimeSpan? end = null)
    {
        if (_audioGraph == null || _fileInputNode == null) return false;

        bool wasError = false;
        var fileOutputResult = await _audioGraph.CreateFileOutputNodeAsync(destinationFile, profile);

        if (fileOutputResult.Status != AudioFileNodeCreationStatus.Success) return false;

        AudioFileOutputNode fileOutputNode = fileOutputResult.FileOutputNode;
        AudioSubmixNode finalNode = (_eqNodes != null && _eqNodes.Count > 0) ? _eqNodes[^1] : _submixNode;

        try
        {
            finalNode.AddOutgoingConnection(fileOutputNode);

            TimeSpan startTime = start ?? TimeSpan.Zero;
            TimeSpan endTime = end ?? _fileInputNode.Duration;

            _fileInputNode.Seek(startTime);
            _audioGraph.Start();

            var exportTimeout = DateTime.UtcNow.AddMinutes(30);
            while (_fileInputNode.Position < endTime)
            {
                if (DateTime.UtcNow > exportTimeout || _audioGraph == null) break;
                await Task.Delay(15);
            }

            _audioGraph.Stop();

            var finalizeResult = await fileOutputNode.FinalizeAsync();
            return finalizeResult == TranscodeFailureReason.None;
        }
        catch (Exception ex)
        {
            Log.Print($"Export Error: {ex.Message}");
            wasError = true;
            return false;
        }
        finally
        {
            onComplete?.Invoke(wasError);

            try
            {
                if (finalNode != null && fileOutputNode != null && _audioGraph != null)
                    finalNode.RemoveOutgoingConnection(fileOutputNode);
            }
            catch { } // Swallow disposal-related errors

            fileOutputNode?.Dispose();

            try
            {
                if (_fileInputNode != null && _audioGraph != null)
                    _fileInputNode.Seek(start ?? TimeSpan.Zero);
            }
            catch { }

            try
            {
                Seek(mediaPlayer?.TimelineController?.Position ?? TimeSpan.Zero);
            }
            catch { }
        }
    }
}