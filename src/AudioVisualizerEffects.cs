using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Windows.Foundation;
using Windows.UI;

namespace SSPlayer;

public enum VisualizerMode
{
    SimpleBars,
    DotMatrix, Circular, NeonWave, PeakDots, RadialPoly, SpectrumFloor, MirrorBars, DNAHelix, DNACells,
    Orbitals, CyberGrid, Vortex, GhostEcho, FractalTree,
    LiquidMercury, ParticleStorm, HexagonShield, PulseRings, StarBurst,
    NeonGridWay, Supernova, BlackHole, RetroSun, WarpDrive,
    CyberTunnel, Pulsar, NebulaFog, VHSGlitch, SatelliteDish,
    SynthWaveMountain, CometTail, EventHorizon, AsteroidBelt, RadarSweep,
    DataStream, BinaryStar, PlasmaCloud, VoidEye, GalacticSpiral,
    CassetteTape, HydraulicPress, ScanningLaser, FluxCapacitor,
    NeonPalmTrees, VectorMountain, WireframeCity, DataHighway, VHSInterference,
    CosmicStrings, QuasarBeam, WormholeTravel, StarMap, NebulaGlow,
    PistonArray, GearTrain, CircuitFlow, PressureGauge,
    VHSVerticalScan, CyberWeb, BokehWaterfall, RetroRadar, AnamorphicFlares,
    HydraulicBars, NeonClockwork, StarfieldWarp, DataRainfall, NeonPulseRings,
    OutrunSunGlow, MechanicalWave, CyberRibbon, BokehTunnel, PrismShatter, NeonCircuit,
    SpaceDust, ElectricFence, GlowGears, DigitalHorizon,
    SolarFlare, CrystalCave, TeslaCoil, HurricaneEye, AuroraBorealis, QuantumBubbles, MagneticField, SolarSail,
    IceCrystals, Thunderstorm, RainbowBridge, FireworksDisplay, Sandstorm, TornadoAlley, BioluminescentBay,
    SolarEclipse, MoonPhases, StainedGlass, Kaleidoscope, LavaLamp, HologramProjector,
    AtomicNucleus, SolarPanelArray, WindTurbine, DamWaterfall, ClockworkMechanism, MorseCodeBeacon
}

public partial class AudioEngine
{
    private readonly object _lockdraw = new object();
    private float[] _visualizerValues = new float[1024];
    private const float SmoothingFactor = 0.15f;
    private float[] _lowPassBuffer = new float[32];
    private float[] _falloff = new float[32];
    private float[] _visualizerPeaks = new float[32];
    private float[] _falloffSpeed = new float[32];
    private const float Sensitivity = 1.15f;
    private const float DecayRate = 0.92f;
    private int _debugCounter = 0;
    private float _rotationAngle = 0;
    private float _hueOffset = 0;
    private List<(Vector2 Pos, Vector2 Vel, float Life)> _explosions = new();

    private CanvasGradientStop[] gradientStops = new CanvasGradientStop[]
    {
        new CanvasGradientStop { Position = 0.0f, Color = Colors.DodgerBlue },
        new CanvasGradientStop { Position = 0.5f, Color = Colors.LightGreen },
        new CanvasGradientStop { Position = 1.0f, Color = Colors.Magenta }
    };

    private List<Vector2> _particles = new List<Vector2>();
    private Random _rng = new Random();
    private float _totalVolume = 0;
    private float _glitchOffset = 0;
    private List<(Vector2 Pos, Vector2 Vel, float Accel, float Life)> _starfield = new();
    private float _waveOffset = 0;
    private List<Vector3> _matrixRain = new();
    private float[] _lerpedPeaks = new float[16];
    private float _pulseScale = 1.0f;
    int _enableVisualizer = 0;
    private Color GetDynamicColor(float offset = 0, byte alpha = 255)
    {
        float hue = (_hueOffset + offset) % 360;
        return ColorFromHue(hue, alpha);
    }
    public void EnableDisableVisualizer(bool state) => Interlocked.Exchange(ref _enableVisualizer, state ? 1 : 0);
    private Color ColorFromHue(float hue, byte a = 255)
    {
        float s = 0.8f;
        float v = 0.9f + (_totalVolume * 0.1f);
        float c = v * s;
        float x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        float m = v - c;

        (float r, float g, float b) = hue switch
        {
            >= 0 and < 60 => ((float)c, x, 0f),
            >= 60 and < 120 => (x, c, 0f),
            >= 120 and < 180 => (0f, c, x),
            >= 180 and < 240 => (0f, x, c),
            >= 240 and < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };

        return Color.FromArgb(
            a,
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)
        );
    }

    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        lock (_lockdraw)
        {
            float decay = DecayRate;
            _totalVolume = 0;

            for (int i = 0; i < _visualizerPeaks.Length; i++)
            {
                _visualizerPeaks[i] *= decay;
                _totalVolume += _visualizerPeaks[i];
            }
            _totalVolume /= _visualizerPeaks.Length;

            _rotationAngle += 0.005f + (_totalVolume * 0.04f);
            _hueOffset = (_hueOffset + 0.5f + (_totalVolume * 2f)) % 360;
            _waveOffset += 0.02f + (_totalVolume * 0.1f);

            UpdateStarfield(sender);

            if (_totalVolume > 0.8f) _glitchOffset = (float)(_rng.NextDouble() - 0.5) * 25f;
            else _glitchOffset *= 0.85f;

            UpdateParticles(sender);
        }
    }

    private void UpdateStarfield(ICanvasAnimatedControl sender)
    {
        var pool = ArrayPool<(Vector2 Pos, Vector2 Vel, float Accel, float Life)>.Shared;
        int count = 0;
        (Vector2 Pos, Vector2 Vel, float Accel, float Life)[] rentedArray = null;

        lock (_lockdraw)
        {
            count = _starfield.Count;
            if (count > 0)
            {
                rentedArray = pool.Rent(count);
                _starfield.CopyTo(0, rentedArray, 0, count);
                _starfield.Clear();
            }

            if (_totalVolume > 0.4f && count < 150)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float speed = 1f + (_totalVolume * 12f);
                _starfield.Add((
                    new Vector2((float)sender.Size.Width / 2, (float)sender.Size.Height / 2),
                    new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed),
                    1.03f,
                    1.0f
                ));
            }
        }

        if (rentedArray != null)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var s = rentedArray[i];

                    s.Pos += s.Vel;
                    s.Vel *= s.Accel;
                    s.Life -= 0.015f;

                    if (s.Life > 0)
                    {
                        lock (_lockdraw)
                        {
                            _starfield.Add(s);
                        }
                    }
                }
            }
            finally
            {
                pool.Return(rentedArray);
            }
        }
    }

    private void UpdateParticles(ICanvasAnimatedControl sender)
    {
        if (_totalVolume > 0.05f && _particles.Count < 80)
        {
            _particles.Add(new Vector2((float)_rng.NextDouble() * (float)sender.Size.Width, (float)sender.Size.Height + 10));
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Y -= 0.5f + (_totalVolume * 15f);
            p.X += (float)Math.Sin(p.Y / 30 + _rotationAngle) * 1.2f;
            _particles[i] = p;
            if (p.Y < -20) _particles.RemoveAt(i);
        }
    }

    public void Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _enableVisualizer, 0, 0) == 0) return;

        float w = (float)sender.Size.Width;
        float h = (float)sender.Size.Height;
        if (w <= 0 || h <= 0) return;

        lock (_lockdraw)
        {
            args.DrawingSession.Clear(Colors.Transparent);
            var ds = args.DrawingSession;
            DrawStarfield(ds);

            switch (Mode)
            {
                case VisualizerMode.SimpleBars:
                    DrawBars(ds, w, h * 0.8f, _visualizerPeaks.Length, false);
                    break;
                case VisualizerMode.DotMatrix:
                    DrawDots(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonWave:
                    DrawNeonWave(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Circular:
                    DrawCircular(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.MirrorBars:
                    DrawMirrorBars(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PeakDots:
                    DrawPeakDots(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.RadialPoly:
                    DrawRadialPoly(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SpectrumFloor:
                    DrawSpectrumFloor(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DNAHelix:
                    DrawDNAHelix(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DNACells:
                    DrawHumanDNA(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Orbitals:
                    DrawOrbitals(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CyberGrid:
                    DrawCyberGrid(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Vortex:
                    DrawVortex(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.GhostEcho:
                    DrawGhostEcho(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.FractalTree:
                    DrawFractalTree(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.LiquidMercury:
                    DrawLiquidMercury(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.ParticleStorm:
                    DrawParticleStorm(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.HexagonShield:
                    DrawHexagonShield(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PulseRings:
                    DrawPulseRings(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.StarBurst:
                    DrawStarBurst(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonGridWay:
                    DrawNeonGridWay(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Supernova:
                    DrawSupernova(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.BlackHole:
                    DrawBlackHole(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.RetroSun:
                    DrawRetroSun(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.WarpDrive:
                    DrawWarpDrive(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CyberTunnel:
                    DrawCyberTunnel(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Pulsar:
                    DrawPulsar(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NebulaFog:
                    DrawNebulaFog(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.VHSGlitch:
                    DrawVHSGlitch(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SatelliteDish:
                    DrawSatelliteDish(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SynthWaveMountain:
                    DrawSynthWaveMountain(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CometTail:
                    DrawCometTail(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.EventHorizon:
                    DrawEventHorizon(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.AsteroidBelt:
                    DrawAsteroidBelt(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.RadarSweep:
                    DrawRadarSweep(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DataStream:
                    DrawDataStream(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.BinaryStar:
                    DrawBinaryStar(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PlasmaCloud:
                    DrawPlasmaCloud(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.VoidEye:
                    DrawVoidEye(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.GalacticSpiral:
                    DrawGalacticSpiral(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CassetteTape:
                    DrawCassetteTape(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.HydraulicPress:
                    DrawHydraulicPress(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.ScanningLaser:
                    DrawScanningLaser(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.FluxCapacitor:
                    DrawFluxCapacitor(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonPalmTrees:
                    DrawNeonPalmTrees(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.VectorMountain:
                    DrawVectorMountain(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.WireframeCity:
                    DrawWireframeCity(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DataHighway:
                    DrawDataHighway(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.VHSInterference:
                    DrawVHSInterference(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CosmicStrings:
                    DrawCosmicStrings(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.QuasarBeam:
                    DrawQuasarBeam(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.WormholeTravel:
                    DrawWormholeTravel(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.StarMap:
                    DrawStarMap(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NebulaGlow:
                    DrawNebulaGlow(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PistonArray:
                    DrawPistonArray(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.GearTrain:
                    DrawGearTrain(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CircuitFlow:
                    DrawCircuitFlow(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PressureGauge:
                    DrawPressureGauge(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.VHSVerticalScan:
                    DrawVHSVerticalScan(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CyberWeb:
                    DrawCyberWeb(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.BokehWaterfall:
                    DrawBokehWaterfall(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.RetroRadar:
                    DrawRetroRadar(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.AnamorphicFlares:
                    DrawAnamorphicFlares(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.HydraulicBars:
                    DrawHydraulicBars(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonClockwork:
                    DrawNeonClockwork(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.StarfieldWarp:
                    DrawStarfieldWarp(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DataRainfall:
                    DrawDataRainfall(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonPulseRings:
                    DrawNeonPulseRings(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.OutrunSunGlow:
                    DrawOutrunSunGlow(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.MechanicalWave:
                    DrawMechanicalWave(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CyberRibbon:
                    DrawCyberRibbon(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.BokehTunnel:
                    DrawBokehTunnel(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.PrismShatter:
                    DrawPrismShatter(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.NeonCircuit:
                    DrawNeonCircuit(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SpaceDust:
                    DrawSpaceDust(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.ElectricFence:
                    DrawElectricFence(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.GlowGears:
                    DrawGlowGears(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DigitalHorizon:
                    DrawDigitalHorizon(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SolarFlare:
                    DrawSolarFlare(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.CrystalCave:
                    DrawCrystalCave(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.TeslaCoil:
                    DrawTeslaCoil(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.HurricaneEye:
                    DrawHurricaneEye(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.AuroraBorealis:
                    DrawAuroraBorealis(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.QuantumBubbles:
                    DrawQuantumBubbles(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.MagneticField:
                    DrawMagneticField(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SolarSail:
                    DrawSolarSail(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.IceCrystals:
                    DrawIceCrystals(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Thunderstorm:
                    DrawThunderstorm(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.RainbowBridge:
                    DrawRainbowBridge(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.FireworksDisplay:
                    DrawFireworksDisplay(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Sandstorm:
                    DrawSandstorm(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.TornadoAlley:
                    DrawTornadoAlley(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.BioluminescentBay:
                    DrawBioluminescentBay(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SolarEclipse:
                    DrawSolarEclipse(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.MoonPhases:
                    DrawMoonPhases(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.StainedGlass:
                    DrawStainedGlass(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.Kaleidoscope:
                    DrawKaleidoscope(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.LavaLamp:
                    DrawLavaLamp(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.HologramProjector:
                    DrawHologramProjector(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.AtomicNucleus:
                    DrawAtomicNucleus(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.SolarPanelArray:
                    DrawSolarPanelArray(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.WindTurbine:
                    DrawWindTurbine(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.DamWaterfall:
                    DrawDamWaterfall(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.ClockworkMechanism:
                    DrawClockworkMechanism(ds, w, h, _visualizerPeaks.Length);
                    break;
                case VisualizerMode.MorseCodeBeacon:
                    DrawMorseCodeBeacon(ds, w, h, _visualizerPeaks.Length);
                    break;

            }

            DrawGlobalOverlays(ds);
        }
    }
    public void DrawHumanDNA(CanvasDrawingSession ds, float width, float height, int targetBarCount)
    {
        Vector2 center = new Vector2(width / 2, height / 2);
        float helixHeight = height * 0.8f;
        float step = helixHeight / targetBarCount;

        // Split audio spectrum: Bass (0-4) and Treble (12-15)
        float bassIntensity = (_visualizerPeaks[0] + _visualizerPeaks[1] + _visualizerPeaks[2]) / 3f;
        float trebleIntensity = (_visualizerPeaks[13] + _visualizerPeaks[14] + _visualizerPeaks[15]) / 3f;

        float amplitude = (width * 0.15f) + (bassIntensity * 100f); // Bass expands the helix
        float frequency = 0.5f;

        for (int i = 0; i < targetBarCount; i++)
        {
            // Calculate vertical position (centered)
            float y = (center.Y - (helixHeight / 2)) + (i * step);

            // Rotation math - Treble increases the "spin" vibration
            float rotation = (i * frequency) + _waveOffset + (trebleIntensity * 2f);

            // Calculate X positions for the two strands
            float x1 = center.X + (float)Math.Sin(rotation) * amplitude;
            float x2 = center.X + (float)Math.Sin(rotation + Math.PI) * amplitude;
            float z1 = (float)Math.Cos(rotation);
            float z2 = (float)Math.Cos(rotation + Math.PI);
            float scale1 = 1.0f + (z1 * 0.4f);
            float scale2 = 1.0f + (z2 * 0.4f);

            Color col1 = GetDynamicColor(i * 8, (byte)(150 + (z1 * 100)));
            Color col2 = GetDynamicColor(i * 8 + 180, (byte)(150 + (z2 * 100)));

            float rungAlpha = Math.Clamp(0.3f + bassIntensity, 0, 1);
            ds.DrawLine(x1, y, x2, y, Color.FromArgb((byte)(rungAlpha * 255), 200, 200, 255), 1.5f);
            ds.FillCircle(x1, y, (4 * scale1) + (trebleIntensity * 10), col1);
            ds.FillCircle(x1, y, 1.5f * scale1, Colors.White); // Inner core glow
            ds.FillCircle(x2, y, (4 * scale2) + (trebleIntensity * 10), col2);
            ds.FillCircle(x2, y, 1.5f * scale2, Colors.White);
        }
    }
    public void DrawDNAHelix(CanvasDrawingSession ds, float width, float height, int targetBarCount)
    {
        float centerY = height / 2;
        float sectionWidth = width / (targetBarCount + 1);
        float helixWidth = height * 0.3f;

        for (int i = 0; i < targetBarCount; i++)
        {
            float x = (i + 1) * sectionWidth;
            float angleOffset = i * 0.5f + _waveOffset;
            float val = _visualizerPeaks[i] * height * 0.4f * Sensitivity;
            float y1 = centerY + (float)Math.Sin(angleOffset) * (helixWidth + val);
            float y2 = centerY + (float)Math.Sin(angleOffset + Math.PI) * (helixWidth + val);

            Color col1 = GetDynamicColor(i * 10);
            Color col2 = GetDynamicColor(i * 10 + 180);

            ds.DrawLine(x, y1, x, y2, Color.FromArgb(100, 255, 255, 255), 1f);
            ds.FillCircle(x, y1, 5 + (_totalVolume * 5), col1);
            ds.FillCircle(x, y2, 5 + (_totalVolume * 5), col2);
            ds.FillCircle(x, y1, 2, Colors.White);
            ds.FillCircle(x, y2, 2, Colors.White);
        }
    }
    public void DrawNeonWave(CanvasDrawingSession ds, float width, float height, int targetBarCount)
    {
        float centerY = height / 2;
        float sectionWidth = targetBarCount > 1 ? width / (targetBarCount - 1) : width;
        Color accentColor = GetDynamicColor();
        using var builder = new CanvasPathBuilder(ds);
        builder.BeginFigure(0, centerY);

        for (int i = 0; i < targetBarCount; i++)
        {
            float audioSwing = _visualizerPeaks[i] * (height * 0.45f) * Sensitivity;
            float x = i * sectionWidth;
            float y = centerY - audioSwing - (float)Math.Sin(_waveOffset + (i * 0.5f)) * 20f;
            builder.AddLine(x, y);
        }

        for (int i = targetBarCount - 1; i >= 0; i--)
        {
            float audioSwing = _visualizerPeaks[i] * (height * 0.45f) * Sensitivity;
            float x = i * sectionWidth;
            float y = centerY + audioSwing + (float)Math.Sin(_waveOffset + (i * 0.5f)) * 20f;
            builder.AddLine(x, y);
        }

        builder.EndFigure(CanvasFigureLoop.Closed);
        using var geometry = CanvasGeometry.CreatePath(builder);
        ds.DrawGeometry(geometry, Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B), 8f);
        ds.DrawGeometry(geometry, Colors.White, 2.5f);
        ds.FillGeometry(geometry, Color.FromArgb((byte)(40 + _totalVolume * 60), accentColor.R, accentColor.G, accentColor.B));
    }

    public void DrawCircular(CanvasDrawingSession ds, float width, float height, int targetBarCount)
    {
        var center = new Vector2(width / 2, height / 2);
        float baseRadius = Math.Min(width, height) * 0.25f + (_totalVolume * 30f);

        for (int i = 0; i < targetBarCount; i++)
        {
            float angle = (i * (float)Math.PI * 2 / targetBarCount) + _rotationAngle;
            float p = _visualizerPeaks[i];
            float magnitude = p * height * 0.4f * Sensitivity;

            var start = center + new Vector2((float)Math.Cos(angle) * baseRadius, (float)Math.Sin(angle) * baseRadius);
            var end = center + new Vector2((float)Math.Cos(angle) * (baseRadius + magnitude), (float)Math.Sin(angle) * (baseRadius + magnitude));

            Color col = GetDynamicColor(i * 10);
            ds.DrawLine(start, end, Color.FromArgb(100, col.R, col.G, col.B), 6f);
            ds.DrawLine(start, end, Colors.White, 2f);
        }
    }

    public void DrawBars(CanvasDrawingSession ds, float width, float height, int targetBarCount, bool isReflection)
    {
        float barWidth = width / targetBarCount;
        float gap = 2f;

        for (int i = 0; i < targetBarCount; i++)
        {
            float val = _visualizerPeaks[i] * height * Sensitivity;
            float x = (i * barWidth) + gap;
            float w = barWidth - (gap * 2);
            Rect rect = new Rect(x, height - val, w, val);
            Color barCol = GetDynamicColor(i * 5);

            using var brush = new CanvasLinearGradientBrush(ds, Colors.White, barCol)
            {
                StartPoint = new Vector2(x, height - val),
                EndPoint = new Vector2(x, height)
            };

            ds.FillRoundedRectangle(new Rect(x, height - val, w, val), 4, 4, brush);

            if (val > height * 0.5f)
            {
                ds.FillRectangle(new Rect(x, height - val, w, 4), Colors.White);
            }
        }
    }

    private void DrawDots(CanvasDrawingSession ds, float width, float height, int targetBarCount)
    {
        int rows = 20;
        float dotSize = Math.Min(width / targetBarCount, height / rows) * 0.7f;
        float xStep = width / targetBarCount;
        float yStep = height / rows;

        for (int c = 0; c < targetBarCount; c++)
        {
            float val = _visualizerPeaks[c] * rows * Sensitivity;
            float x = c * xStep + (xStep / 2);

            for (int r = 0; r < rows; r++)
            {
                float y = height - (r * yStep) - (yStep / 2);
                float opacity = (r < val) ? 1.0f : 0.1f;
                Color col = GetDynamicColor(c * 10 + r * 5, (byte)(opacity * 255));
                ds.FillCircle(x, y, dotSize / 2, col);

                if (r < val && r > rows * 0.8f)
                    ds.FillCircle(x, y, dotSize / 4, Colors.White);
            }
        }
    }

    private void DrawMirrorBars(CanvasDrawingSession ds, float w, float h, int count)
    {
        float barWidth = (w * 0.9f) / count;
        float startX = (w - (barWidth * count)) / 2;
        float midY = h / 2;

        for (int i = 0; i < count; i++)
        {
            float val = _visualizerPeaks[i] * (h * 0.45f) * Sensitivity;
            float x = startX + (i * barWidth) + _glitchOffset;
            Color col = GetDynamicColor(i * 15);

            ds.FillRoundedRectangle(new Rect(x, midY - val, barWidth - 3, val), 2, 2, col);
            ds.FillRoundedRectangle(new Rect(x, midY, barWidth - 3, val), 2, 2, Color.FromArgb(180, col.R, col.G, col.B));
        }
    }

    private void DrawSpectrumFloor(CanvasDrawingSession ds, float w, float h, int count)
    {
        float barWidth = w / count;
        Vector2 vanishingPoint = new Vector2(w / 2, h * 0.4f);
        Color floorCol = GetDynamicColor();

        for (int i = 0; i < count; i++)
        {
            float x = i * barWidth + (barWidth / 2);
            float val = _visualizerPeaks[i] * (h * 0.4f);
            var start = new Vector2(x, h);
            var end = Vector2.Lerp(new Vector2(x, h), vanishingPoint, 0.6f);
            end.Y -= val;

            ds.DrawLine(start, end, GetDynamicColor(i * 5), 3f + (val / 10f));
        }
    }

    private void DrawRadialPoly(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2[] points = new Vector2[count];
        Vector2 center = new Vector2(w / 2, h / 2);
        Color col = GetDynamicColor();

        for (int i = 0; i < count; i++)
        {
            float angle = (float)(i * Math.PI * 2 / count) + _rotationAngle;
            float r = (h * 0.2f) + (_visualizerPeaks[i] * h * 0.4f * Sensitivity);
            points[i] = center + new Vector2((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r);
        }

        using var poly = CanvasGeometry.CreatePolygon(ds, points);
        ds.DrawGeometry(poly, col, 4f);
        ds.FillGeometry(poly, Color.FromArgb(60, col.R, col.G, col.B));
        ds.DrawCircle(center, (h * 0.15f) + (_totalVolume * 40), Color.FromArgb(100, 255, 255, 255), 2f);
    }

    private void DrawPeakDots(CanvasDrawingSession ds, float w, float h, int count)
    {
        float barWidth = w / count;
        for (int i = 0; i < count; i++)
        {
            float x = (i * barWidth) + (barWidth / 2);
            float y = h - (_visualizerPeaks[i] * h * 0.85f * Sensitivity) - 20;
            Color col = GetDynamicColor(i * 20);

            ds.FillCircle(x, y, 6 + (_totalVolume * 10), col);
            ds.FillCircle(x, y, 3, Colors.White);
            ds.DrawCircle(x, y, 12 + (_totalVolume * 15), Color.FromArgb(50, col.R, col.G, col.B), 2f);
        }
    }

    private void DrawStarfield(CanvasDrawingSession ds)
    {
        var pool = ArrayPool<(Vector2 Pos, Vector2 Vel, float Accel, float Life)>.Shared;

        int count = 0;
        (Vector2 Pos, Vector2 Vel, float Accel, float Life)[] rentedArray = null;

        lock (_lockdraw)
        {
            count = _starfield.Count;

            if (count > 0)
            {
                rentedArray = pool.Rent(count);
                _starfield.CopyTo(0, rentedArray, 0, count);
            }
        }

        if (count > 0 && rentedArray != null)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var s = rentedArray[i];
                    float size = (1.0f - s.Life) * 6f;
                    Color col = GetDynamicColor(s.Life * 100, (byte)(s.Life * 255));

                    ds.FillCircle(s.Pos, size, col);
                    ds.DrawLine(s.Pos, s.Pos - (s.Vel * 1.5f),
                        Color.FromArgb((byte)(s.Life * 120), col.R, col.G, col.B), 1.5f);
                }
            }
            finally
            {
                pool.Return(rentedArray);
            }
        }
    }
    private void DrawGlobalOverlays(CanvasDrawingSession ds)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            float size = 1f + (_totalVolume * 5f);
            ds.FillCircle(p, size, Color.FromArgb(180, 255, 255, 255));
        }
    }
    private void DrawOrbitals(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float bass = _visualizerPeaks[0] * 50;
        ds.FillCircle(center, 15 + bass, GetDynamicColor(0, 150));

        for (int i = 0; i < Math.Min(count, 8); i++)
        {
            float dist = 45 + (i * 25);
            float angle = _rotationAngle * (1 + i * 0.2f);
            float p = _visualizerPeaks[i] * 30;
            Vector2 pos = center + new Vector2((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist);

            ds.DrawCircle(center, dist, Colors.WhiteSmoke, 1f);
            ds.FillCircle(pos, 4 + p, GetDynamicColor(i * 45));
        }
    }

    private void DrawCyberGrid(CanvasDrawingSession ds, float w, float h, int count)
    {
        float midX = w / 2;
        float horizon = h * 0.4f;
        Color lineCol = GetDynamicColor();

        for (int i = -10; i <= 10; i++)
        {
            float x = midX + (i * w * 0.15f);
            ds.DrawLine(new Vector2(midX, horizon), new Vector2(x, h), Color.FromArgb(100, lineCol.R, lineCol.G, lineCol.B), 2f);
        }
        for (int i = 0; i < 10; i++)
        {
            float progress = (float)i / 10;
            float y = horizon + (progress * progress) * (h - horizon);
            byte alpha = (byte)(_totalVolume * 255 * (1 - progress));
            ds.DrawLine(0, y, w, y, Color.FromArgb(alpha, lineCol.R, lineCol.G, lineCol.B), 1.5f);
        }
    }

    private void DrawVortex(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 64; i++)
        {
            float angle = i * 0.2f + _rotationAngle;
            float r = i * (2 + _totalVolume * 12);
            float size = 2 + _visualizerPeaks[i % count] * 15;
            Vector2 pos = center + new Vector2((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r);
            ds.FillCircle(pos, size, GetDynamicColor(i * 5, (byte)Math.Clamp(255 - (i * 3), 0, 255)));
        }
    }

    private void DrawGhostEcho(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int j = 1; j < 5; j++)
        {
            float scale = 1.0f - (j * 0.15f);
            DrawBars(ds, w * scale, h * scale, count, false);
        }
    }
    private void DrawFractalTree(CanvasDrawingSession ds, float w, float h, int count)
    {
        DrawBranch(ds, new Vector2(w / 2, h), -90, 60 + (_visualizerPeaks[0] * 100), 0);
    }
    private void DrawBranch(CanvasDrawingSession ds, System.Numerics.Vector2 start, float angle, float len, int depth)
    {
        if (depth > 6 || len < 2) return;

        float rad = angle * 0.0174533f;
        Vector2 end = start + new Vector2((float)Math.Cos(rad) * len, (float)Math.Sin(rad) * len);
        ds.DrawLine(start, end, GetDynamicColor(depth * 30), 8 - depth);
        float nextLen = len * 0.75f;
        float spread = 20 + (_totalVolume * 40);
        DrawBranch(ds, end, angle - spread, nextLen, depth + 1);
        DrawBranch(ds, end, angle + spread, nextLen, depth + 1);
    }
    private void DrawLiquidMercury(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        using var pb = new CanvasPathBuilder(ds);
        pb.BeginFigure(center.X, center.Y - 80);

        for (float a = 0; a < Math.PI * 2; a += 0.2f)
        {
            int idx = (int)((a / (Math.PI * 2)) * (count - 1));
            float r = 80 + _visualizerPeaks[idx] * 100;
            pb.AddLine(center.X + (float)Math.Cos(a) * r, center.Y + (float)Math.Sin(a) * r);
        }

        pb.EndFigure(CanvasFigureLoop.Closed);
        ds.FillGeometry(CanvasGeometry.CreatePath(pb), GetDynamicColor(0, 200));
    }

    private void DrawParticleStorm(CanvasDrawingSession ds, float w, float h, int count)
    {
        foreach (var s in _starfield)
        {
            float speedMod = 1.0f + (_totalVolume * 10f);
            ds.DrawLine(s.Pos, s.Pos - (s.Vel * speedMod), GetDynamicColor(s.Life * 100), 2f);
        }
    }

    private void DrawHexagonShield(CanvasDrawingSession ds, float w, float h, int count)
    {
        float size = 35;

        for (float x = 0; x < w + size; x += size * 1.5f)
        {
            for (float y = 0; y < h + size; y += size)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(w / 2, h / 2));
                float p = _visualizerPeaks[(int)(dist / 40) % count];
                if (p > 0.15f) ds.DrawCircle(x, y, size * p, GetDynamicColor(dist * 0.1f), 2f);
            }
        }
    }

    private void DrawPulseRings(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 6; i++)
        {
            float r = ((_waveOffset * 40 + i * 60) % 300) * (0.5f + _totalVolume);
            byte alpha = (byte)Math.Clamp(255 - (r / 300 * 255), 0, 255);
            ds.DrawCircle(center, r, GetDynamicColor(i * 30, alpha), 4f);
        }
    }

    private void DrawStarBurst(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < count; i++)
        {
            float angle = (i * (float)Math.PI * 2 / count) + _rotationAngle;
            float len = 40 + _visualizerPeaks[i] * (h * 0.8f);
            ds.DrawLine(center, center + new Vector2((float)Math.Cos(angle) * len, (float)Math.Sin(angle) * len), GetDynamicColor(i * 10), 2f);
        }
    }
    private void DrawNeonGridWay(CanvasDrawingSession ds, float w, float h, int count)
    {
        float horizon = h * 0.5f;
        Vector2 vp = new Vector2(w / 2, horizon);
        Color col = GetDynamicColor();

        for (int i = -15; i <= 15; i++)
        {
            float x = (w / 2) + (i * w * 0.1f);
            ds.DrawLine(vp, new Vector2(x, h), Color.FromArgb(100, col.R, col.G, col.B), 2f);
        }

        for (int i = 0; i < 10; i++)
        {
            float z = ((i + _waveOffset) % 10) / 10f;
            float y = horizon + (z * z) * (h - horizon);
            float lineW = 1f + (z * 10f * _totalVolume);
            ds.DrawLine(0, y, w, y, GetDynamicColor(i * 20, (byte)(z * 255)), lineW);
        }
    }

    private void DrawSupernova(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float coreRadius = 20 + (_totalVolume * 60);
        ds.FillCircle(center, coreRadius, Colors.White);

        for (int i = 0; i < 100; i++)
        {
            float ang = (float)(_rng.NextDouble() * Math.PI * 2);
            float dist = coreRadius + (float)_rng.NextDouble() * (_visualizerPeaks[i % count] * h);
            Vector2 pos = center + new Vector2((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist);
            ds.DrawLine(center, pos, GetDynamicColor(i, 50), 1f);
        }
    }

    private void DrawBlackHole(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float holeSize = 50 + (_totalVolume * 20);
        ds.FillCircle(center, holeSize, Colors.Black);
        ds.DrawCircle(center, holeSize + 2, GetDynamicColor(), 3f);

        for (int i = 0; i < count; i++)
        {
            float angle = (i * (float)Math.PI * 2 / count) + _rotationAngle;
            float p = _visualizerPeaks[i];
            float r1 = holeSize + 10;
            float r2 = r1 + (p * h * 0.3f);

            Vector2 s = center + new Vector2((float)Math.Cos(angle) * r1, (float)Math.Sin(angle) * r1);
            Vector2 e = center + new Vector2((float)Math.Cos(angle + 0.5f) * r2, (float)Math.Sin(angle + 0.5f) * r2);
            ds.DrawLine(s, e, GetDynamicColor(i * 10), 2f);
        }
    }

    private void DrawRetroSun(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float radius = h * 0.3f;

        for (float y = -radius; y < radius; y += 10)
        {
            float sliceW = (float)Math.Sqrt(radius * radius - y * y) * 2;
            float gap = Math.Max(2, (y + radius) / radius * 8 * (1 - _totalVolume));
            ds.FillRectangle(center.X - sliceW / 2, center.Y + y, sliceW, 10 - gap, GetDynamicColor(y));
        }
    }

    private void DrawWarpDrive(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];

            for (int j = 0; j < 3; j++)
            {
                float ang = (i * 6.28f / count) + (j * 0.1f);
                float startDist = 10 + (p * 50);
                float endDist = w * (0.1f + p);
                ds.DrawLine(
                    center + new Vector2((float)Math.Cos(ang) * startDist, (float)Math.Sin(ang) * startDist),
                    center + new Vector2((float)Math.Cos(ang) * endDist, (float)Math.Sin(ang) * endDist),
                    Colors.White, 1f + p * 5);
            }
        }
    }

    private void DrawCyberTunnel(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        for (int i = 0; i < 10; i++)
        {
            float scale = ((i + _waveOffset) % 10) / 10f;
            float rw = w * scale;
            float rh = h * scale;
            ds.DrawRectangle(center.X - rw / 2, center.Y - rh / 2, rw, rh, GetDynamicColor(i * 20), 2f + _visualizerPeaks[i % count] * 10);
        }
    }

    private void DrawPulsar(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            float angle = (i * 6.28f / count);
            float len = p * h * 0.5f;
            ds.DrawLine(center, center + new Vector2((float)Math.Cos(angle) * len, (float)Math.Sin(angle) * len), GetDynamicColor(i * 15), 1f);
            ds.FillCircle(center + new Vector2((float)Math.Cos(angle) * len, (float)Math.Sin(angle) * len), 2 + p * 8, Colors.White);
        }
    }

    private void DrawNebulaFog(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 20; i++)
        {
            float x = (float)_rng.NextDouble() * w;
            float y = (float)_rng.NextDouble() * h;
            float size = 50 + _visualizerPeaks[i % count] * 200;
            ds.FillCircle(x, y, size, GetDynamicColor(i * 10, 30));
        }
    }

    private void DrawVHSGlitch(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            if (p > 0.6f)
            {
                float y = (float)_rng.NextDouble() * h;
                ds.DrawLine(0, y, w, y, GetDynamicColor(i, 180), p * 20);
                ds.DrawText("SIGNAL LOST", (float)_rng.NextDouble() * w, y, Colors.White);
            }
        }
    }

    private void DrawSatelliteDish(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h * 0.8f);
        ds.DrawCircle(center, 40, Colors.Gray, 2f);
        for (int i = 0; i < 5; i++)
        {
            float r = (i * 50 + _waveOffset * 50) % 250;
            ds.DrawCircle(center, r, GetDynamicColor(i * 20, (byte)(255 - r)), 2f + _totalVolume * 5);
        }
    }

    private void DrawSynthWaveMountain(CanvasDrawingSession ds, float w, float h, int count)
    {
        using var pb = new CanvasPathBuilder(ds);
        pb.BeginFigure(0, h);

        for (int i = 0; i < count; i++)
        {
            float divisor = count > 1 ? count - 1 : 1;
            float x = (w / divisor) * i;
            float peak = i < _visualizerPeaks.Length ? _visualizerPeaks[i] : 0;
            float y = (h * 0.7f) - (peak * h * 0.4f);
            pb.AddLine(x, y);
        }

        pb.AddLine(w, h);
        pb.EndFigure(CanvasFigureLoop.Closed);

        using var geometry = CanvasGeometry.CreatePath(pb);
        ds.FillGeometry(geometry, GetDynamicColor(0, 100));
        ds.DrawGeometry(geometry, Colors.Cyan, 2f);
    }

    private void DrawCometTail(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 head = new Vector2((_waveOffset * 100) % w, h * 0.3f + (float)Math.Sin(_waveOffset) * 100);

        for (int i = 0; i < 20; i++)
        {
            float p = _visualizerPeaks[i % count];
            ds.FillCircle(head - new Vector2(i * 10, 0), 10 - i / 2f + p * 10, GetDynamicColor(i * 10, (byte)(255 - i * 10)));
        }
    }

    private void DrawEventHorizon(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < count; i++)
        {
            float angle = (i * 6.28f / count) + _rotationAngle;
            float rad = 100 + (float)Math.Sin(_waveOffset + i) * 50;
            Vector2 p = center + new Vector2((float)Math.Cos(angle) * rad, (float)Math.Sin(angle) * rad);
            ds.DrawCircle(p, _visualizerPeaks[i] * 40, GetDynamicColor(i * 10), 2f);
        }
    }

    private void DrawAsteroidBelt(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 30; i++)
        {
            float ang = i * 0.2f + _rotationAngle;
            float r = 150 + _visualizerPeaks[i % count] * 50;
            ds.FillCircle(center.X + (float)Math.Cos(ang) * r, center.Y + (float)Math.Sin(ang) * r, 3 + (float)_rng.NextDouble() * 5, Colors.Gray);
        }
    }

    private void DrawRadarSweep(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float radius = h * 0.4f;
        ds.DrawCircle(center, radius, Colors.LimeGreen, 2f);
        float sweepAng = _rotationAngle % 6.28f;
        ds.DrawLine(center, center + new Vector2((float)Math.Cos(sweepAng) * radius, (float)Math.Sin(sweepAng) * radius), Colors.LimeGreen, 3f);

        for (int i = 0; i < count; i++)
        {
            if (_visualizerPeaks[i] > 0.7f)
            {
                float a = i * 6.28f / count;
                ds.FillCircle(center + new Vector2((float)Math.Cos(a) * radius * 0.8f, (float)Math.Sin(a) * radius * 0.8f), 5f, Colors.Red);
            }
        }
    }

    private void DrawDataStream(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float streamH = _visualizerPeaks[i] * h;
            ds.DrawLine(x, h, x, h - streamH, GetDynamicColor(i * 10), 2f);
            if (_visualizerPeaks[i] > 0.5f)
                ds.DrawText(_rng.Next(0, 2).ToString(), x - 5, h - streamH - 20, Colors.White);
        }
    }

    private void DrawBinaryStar(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float d = 100 + _totalVolume * 50;
        Vector2 s1 = center + new Vector2((float)Math.Cos(_rotationAngle) * d, (float)Math.Sin(_rotationAngle) * d);
        Vector2 s2 = center + new Vector2((float)Math.Cos(_rotationAngle + 3.14f) * d, (float)Math.Sin(_rotationAngle + 3.14f) * d);
        ds.FillCircle(s1, 20 + _visualizerPeaks[0] * 30, Colors.Orange);
        ds.FillCircle(s2, 15 + _visualizerPeaks[8 % count] * 30, Colors.DeepSkyBlue);
        ds.DrawLine(s1, s2, Color.FromArgb(50, 255, 255, 255), 1f);
    }

    private void DrawPlasmaCloud(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            ds.FillCircle((float)_rng.NextDouble() * w, (float)_rng.NextDouble() * h, p * 100, GetDynamicColor(i * 10, 40));
        }
    }

    private void DrawVoidEye(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        ds.FillCircle(center, 40, Colors.Black);
        for (int i = 0; i < count; i++)
        {
            float a = (i * 6.28f / count) + (float)Math.Sin(_waveOffset);
            float r = 50 + _visualizerPeaks[i] * 100;
            ds.DrawLine(center, center + new Vector2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r), GetDynamicColor(i * 5), 2f);
        }
    }

    private void DrawGalacticSpiral(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 100; i++)
        {
            float a = i * 0.1f + _rotationAngle;
            float r = i * (3 + _totalVolume * 5);
            ds.FillCircle(center.X + (float)Math.Cos(a) * r, center.Y + (float)Math.Sin(a) * r, 1 + _visualizerPeaks[i % count] * 5, GetDynamicColor(i * 2));
        }
    }
    private void DrawCassetteTape(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float tapeW = 300, tapeH = 180;
        ds.DrawRoundedRectangle(center.X - tapeW / 2, center.Y - tapeH / 2, tapeW, tapeH, 10, 10, Colors.DimGray, 4f);

        // Two spools reacting to bass and treble
        float leftSpool = _visualizerPeaks[0] * 40;
        float rightSpool = _visualizerPeaks[count / 2] * 40;

        ds.DrawCircle(center.X - 70, center.Y, 30 + leftSpool, GetDynamicColor(0), 2f);
        ds.DrawCircle(center.X + 70, center.Y, 30 + rightSpool, GetDynamicColor(180), 2f);
    }

    private void DrawHydraulicPress(CanvasDrawingSession ds, float w, float h, int count)
    {
        float pressPos = _totalVolume * h * 0.4f;
        Color metal = Colors.Silver;
        ds.FillRectangle(w * 0.2f, 0, w * 0.6f, pressPos, metal);
        ds.FillRectangle(w * 0.2f, h - 40, w * 0.6f, 40, metal);

        for (int i = 0; i < count; i++)
        {
            float barH = _visualizerPeaks[i] * (h - pressPos - 40);
            ds.FillRectangle((w * 0.2f) + (i * (w * 0.6f / count)), h - 40 - barH, (w * 0.6f / count) - 2, barH, GetDynamicColor(i * 10));
        }
    }
    private void DrawScanningLaser(CanvasDrawingSession ds, float w, float h, int count)
    {
        float scanY = (float)(Math.Sin(_waveOffset) * 0.5 + 0.5) * h;
        ds.DrawLine(0, scanY, w, scanY, GetDynamicColor(), 2f);

        for (int i = 0; i < count; i++)
        {
            float val = _visualizerPeaks[i] * h * 0.2f;
            ds.FillCircle((w / count) * i, scanY, val, GetDynamicColor(i * 10, 100));
        }
    }

    private void DrawFluxCapacitor(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 3; i++)
        {
            float angle = (i * 120) * 0.0174533f;
            Vector2 dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            float len = 50 + _totalVolume * 150;
            ds.DrawLine(center + dir * 20, center + dir * len, Colors.LightBlue, 4f);

            // Sparking dots
            for (int j = 0; j < 5; j++)
            {
                float p = _visualizerPeaks[(i * 3 + j) % count];
                ds.FillCircle(center + dir * (20 + j * (len / 5)), 2 + p * 10, Colors.White);
            }
        }
    }

    private void DrawNeonPalmTrees(CanvasDrawingSession ds, float w, float h, int count)
    {
        float groundY = h * 0.8f;
        ds.DrawLine(0, groundY, w, groundY, Colors.Magenta, 2f);

        for (int i = 0; i < 3; i++)
        {
            float x = (w * 0.25f) * (i + 1);
            float trunkH = 100 + _visualizerPeaks[i] * 100;
            ds.DrawLine(x, groundY, x, groundY - trunkH, Colors.SaddleBrown, 5f);

            for (int j = 0; j < 5; j++) // Fronds
            {
                float ang = (j * 40 - 80) * 0.0174533f;
                ds.DrawLine(x, groundY - trunkH, x + (float)Math.Cos(ang) * 50, (groundY - trunkH) + (float)Math.Sin(ang) * 50, Colors.Lime, 3f);
            }
        }
    }

    private void DrawVectorMountain(CanvasDrawingSession ds, float w, float h, int count)
    {
        using var pb = new CanvasPathBuilder(ds);
        pb.BeginFigure(0, h * 0.8f);

        for (int i = 0; i < count; i++)
        {
            float divisor = count > 1 ? count - 1 : 1;
            float x = (w / divisor) * i;
            float y = (h * 0.8f) - (_visualizerPeaks[i] * h * 0.5f);
            pb.AddLine(x, y);
        }

        pb.AddLine(w, h * 0.8f);
        pb.EndFigure(CanvasFigureLoop.Open);

        using var geo = CanvasGeometry.CreatePath(pb);
        ds.DrawGeometry(geo, Colors.Cyan, 2f);
        for (int i = 0; i < count; i++) ds.DrawLine((w / count) * i, h * 0.8f, (w / count) * i, h, Color.FromArgb(50, 0, 255, 255), 1f);
    }

    private void DrawWireframeCity(CanvasDrawingSession ds, float w, float h, int count)
    {
        int half = Math.Max(1, count / 2);
        float bWidth = w / half;

        for (int i = 0; i < count / 2; i++)
        {
            float bH = _visualizerPeaks[i] * h * 0.6f;
            Rect r = new Rect(i * bWidth, h - bH, bWidth - 5, bH);
            ds.DrawRectangle(r, GetDynamicColor(i * 10), 1f);

            for (float wy = h - bH + 10; wy < h; wy += 20)
                ds.DrawLine(i * bWidth, wy, (i + 1) * bWidth - 5, wy, Color.FromArgb(40, 255, 255, 255), 1f);
        }
    }
    private void DrawDataHighway(CanvasDrawingSession ds, float w, float h, int count)
    {
        float midX = w / 2;
        for (int i = 0; i < count; i++)
        {
            float z = ((i * 0.1f + _waveOffset) % 1.0f);
            float x = midX + (float)(Math.Sin(i) * w * 0.4f * z);
            float y = h * (1 - z);
            ds.FillCircle(x, y, 2 + _visualizerPeaks[i] * 20, GetDynamicColor(i * 5));
        }
    }
    private void DrawVHSInterference(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 5; i++)
        {
            float y = (float)_rng.NextDouble() * h;
            float strength = _visualizerPeaks[_rng.Next(count)];
            ds.DrawLine(0, y, w, y + (_rng.Next(-10, 10)), GetDynamicColor(0, (byte)(strength * 255)), 1f);
        }
    }
    private void DrawCosmicStrings(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float vib = (float)Math.Sin(_waveOffset + i) * (_visualizerPeaks[i] * 50);
            ds.DrawLine(x, 0, x + vib, h, GetDynamicColor(i * 10, 150), 1f);
        }
    }

    private void DrawQuasarBeam(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        ds.FillCircle(center, 30 + _totalVolume * 20, Colors.White);
        float beamW = _visualizerPeaks[0] * 100;
        ds.DrawLine(0, h / 2, w, h / 2, GetDynamicColor(0, 100), beamW);
        ds.DrawLine(w / 2, 0, w / 2, h, GetDynamicColor(180, 100), beamW);
    }
    private void DrawWormholeTravel(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 15; i++)
        {
            float r = ((i * 40 + _waveOffset * 100) % 600);
            float p = _visualizerPeaks[i % count];
            ds.DrawCircle(center, r + p * 50, GetDynamicColor(i * 20, (byte)(255 - (r / 600) * 255)), 2f);
        }
    }
    private void DrawStarMap(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count - 1; i++)
        {
            Vector2 p1 = new Vector2((w / count) * i, h - _visualizerPeaks[i] * h);
            Vector2 p2 = new Vector2((w / count) * (i + 1), h - _visualizerPeaks[i + 1] * h);
            ds.DrawLine(p1, p2, GetDynamicColor(i * 10), 1f);
            ds.FillCircle(p1, 3f, Colors.White);
        }
    }
    private void DrawNebulaGlow(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 8; i++)
        {
            float x = w * (0.1f + i * 0.1f);
            float y = h / 2 + (float)Math.Sin(_waveOffset + i) * 100;
            ds.FillCircle(x, y, 50 + _visualizerPeaks[i] * 150, GetDynamicColor(i * 40, 40));
        }
    }

    private void DrawPistonArray(CanvasDrawingSession ds, float w, float h, int count)
    {
        float pW = w / count;

        for (int i = 0; i < count; i++)
        {
            float travel = _visualizerPeaks[i] * h * 0.4f;
            ds.FillRectangle(i * pW + 5, h * 0.1f + travel, pW - 10, 40, Colors.DarkGray);
            ds.DrawLine(i * pW + pW / 2, 0, i * pW + pW / 2, h * 0.1f + travel, Colors.Silver, 4f);
        }
    }

    private void DrawGearTrain(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 3; i++)
        {
            float r = 60 + _visualizerPeaks[i * 4] * 40;
            Vector2 pos = new Vector2(w * 0.25f * (i + 1), h / 2);
            ds.DrawCircle(pos, r, Colors.Goldenrod, 5f);

            for (int t = 0; t < 12; t++)
            {
                float ang = (t * 30) * 0.0174533f + (i % 2 == 0 ? _rotationAngle : -_rotationAngle);
                ds.DrawLine(pos, pos + new Vector2((float)Math.Cos(ang) * (r + 15), (float)Math.Sin(ang) * (r + 15)), Colors.Goldenrod, 8f);
            }
        }
    }
    private void DrawCircuitFlow(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 10; i++)
        {
            float y = (h / 10) * i;
            ds.DrawLine(0, y, w, y, Color.FromArgb(50, 0, 255, 0), 1f);
            float x = (_waveOffset * 200 + i * 100) % w;
            ds.FillCircle(x, y, 4 + _visualizerPeaks[i % count] * 10, Colors.Lime);
        }
    }
    private void DrawPressureGauge(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h * 0.7f);
        ds.DrawCircle(center, 100, Colors.White, 4f);
        float angle = -180 + (_totalVolume * 180); // Needle moves with volume
        float rad = angle * 0.0174533f;
        ds.DrawLine(center, center + new Vector2((float)Math.Cos(rad) * 90, (float)Math.Sin(rad) * 90), Colors.Red, 3f);
    }
    private void DrawVHSVerticalScan(CanvasDrawingSession ds, float w, float h, int count)
    {
        float scanY = (_waveOffset * 300) % h;
        ds.FillRectangle(0, scanY, w, 20, GetDynamicColor(0, 40));
        ds.DrawLine(0, scanY, w, scanY, Colors.Cyan, 1f);

        for (int i = 0; i < 15; i++)
        {
            float p = _visualizerPeaks[i % count];
            float x = (w / 15) * i;
            ds.FillCircle(x, scanY + (float)Math.Sin(i) * 10, 5 + p * 20, GetDynamicColor(i * 10, 100));
        }
    }

    private void DrawCyberWeb(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2[] points = new Vector2[count];
        for (int i = 0; i < count; i++)
            points[i] = new Vector2((w / count) * i, h - (_visualizerPeaks[i] * h * 0.5f));

        for (int i = 0; i < count - 1; i++)
        {
            for (int j = i + 1; j < Math.Min(i + 4, count); j++)
            {
                float dist = Vector2.Distance(points[i], points[j]);
                ds.DrawLine(points[i], points[j], GetDynamicColor(i * 5, (byte)Math.Max(0, 150 - dist)), 1f);
            }
        }
    }

    private void DrawBokehWaterfall(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            float x = (w / count) * i;
            float yBase = h - (p * h * 0.6f);
            for (int j = 0; j < 3; j++)
            {
                float offset = ((_waveOffset * 5 + j) % 1.0f) * (h - yBase);
                float range = h - yBase;
                byte alpha = range > 0 ? (byte)(255 - (offset / range * 255)) : (byte)255;
                ds.FillCircle(x, yBase + offset, 4 + p * 10, GetDynamicColor(i * 10, alpha));
            }
        }
    }

    private void DrawRetroRadar(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float angle = _waveOffset * 2f;
        ds.DrawCircle(center, h * 0.4f, Colors.Green, 1f);
        ds.DrawLine(center, center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (h * 0.4f), Colors.Lime, 2f);

        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            float r = (h * 0.4f) * (i / (float)count);
            ds.DrawCircle(center, r, GetDynamicColor(0, (byte)(p * 100)), 1f);
        }
    }

    private void DrawAnamorphicFlares(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 5; i++)
        {
            float y = (h / 6) * (i + 1);
            float p = _visualizerPeaks[Math.Min(i * Math.Max(1, count / 5), count - 1)];
            float flareW = p * w;
            ds.DrawLine(w / 2 - flareW / 2, y, w / 2 + flareW / 2, y, GetDynamicColor(i * 40, 150), 2f + p * 10);
            ds.FillCircle(w / 2, y, 10 + p * 30, Colors.White);
        }
    }

    private void DrawHydraulicBars(CanvasDrawingSession ds, float w, float h, int count)
    {
        float barW = w / count;

        for (int i = 0; i < count; i++)
        {
            float val = _visualizerPeaks[i] * h * 0.7f;
            ds.FillRectangle(i * barW + 2, h - val, barW - 4, val, Colors.DarkSlateGray);
            ds.DrawRectangle(i * barW + 2, h - val, barW - 4, val, Colors.Silver, 1f);
            // Steam puff
            ds.FillCircle(i * barW + barW / 2, h - val, _totalVolume * 40, Color.FromArgb(50, 255, 255, 255));
        }
    }

    private void DrawNeonClockwork(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 4; i++)
        {
            float rot = _waveOffset * (i % 2 == 0 ? 1 : -1);
            float r = 50 + i * 40;
            float p = _visualizerPeaks[i * 2 % count];
            ds.DrawCircle(center, r + p * 20, GetDynamicColor(i * 30), 2f);
            // Ticks
            for (int j = 0; j < 8; j++)
            {
                float a = rot + (j * 45) * 0.0174f;
                ds.DrawLine(center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r,
                            center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (r + 10), Colors.White, 2f);
            }
        }
    }

    private void DrawStarfieldWarp(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 50; i++)
        {
            float z = ((i * 0.02f + _waveOffset * 0.1f) % 1.0f);
            float p = _visualizerPeaks[i % count];
            float dist = z * (w / 2);
            float angle = i * 137.5f; // Golden angle
            Vector2 pos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * dist;
            ds.FillCircle(pos, 1 + z * 10 + p * 15, GetDynamicColor(i * 5, (byte)(z * 255)));
        }
    }

    private void DrawDataRainfall(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 20; i++)
        {
            float x = (w / 20) * i;
            float p = _visualizerPeaks[i % count];
            float y = (_waveOffset * 400 + i * 100) % h;
            ds.FillRoundedRectangle(x, y, 10, 20 + p * 50, 5, 5, GetDynamicColor(i * 12));
        }
    }

    private void DrawNeonPulseRings(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 5; i++)
        {
            float p = _visualizerPeaks[i * 3 % count];
            float r = 100 + (i * 60) + (p * 50);
            ds.DrawCircle(center, r, GetDynamicColor(i * 20, (byte)(255 - i * 40)), 4f);
        }
    }

    private void DrawOutrunSunGlow(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 sun = new Vector2(w / 2, h * 0.4f);
        ds.FillCircle(sun, 100 + _totalVolume * 50, GetDynamicColor(20, 100));

        for (int i = 0; i < count; i++)
        {
            float p = _visualizerPeaks[i];
            ds.DrawLine((w / count) * i, h * 0.7f, (w / count) * i, h * 0.7f + p * 100, GetDynamicColor(0, 150), 2f);
        }
    }

    private void DrawMechanicalWave(CanvasDrawingSession ds, float w, float h, int count)
    {
        float pW = w / count;

        for (int i = 0; i < count; i++)
        {
            float y = h / 2 - (_visualizerPeaks[i] * h * 0.4f);
            ds.FillRectangle(i * pW, y, pW - 2, 20, Colors.Silver);
            if (i > 0)
            {
                float prevY = h / 2 - (_visualizerPeaks[i - 1] * h * 0.4f);
                ds.DrawLine((i - 1) * pW + pW / 2, prevY + 10, i * pW + pW / 2, y + 10, Colors.DimGray, 2f);
            }
        }
    }

    private void DrawCyberRibbon(CanvasDrawingSession ds, float w, float h, int count)
    {
        if (count < 2) return;

        using (var pb = new CanvasPathBuilder(ds))
        {
            pb.BeginFigure(0, h / 2);

            for (int i = 0; i < count; i++)
            {
                float divisor = count > 1 ? count - 1 : 1;
                float x = (w / divisor) * i;
                float peak = i < _visualizerPeaks.Length ? _visualizerPeaks[i] : 0;
                float y = h / 2 + (float)Math.Cos(_waveOffset + i * 0.5f) * (peak * 200);
                pb.AddLine(x, y);
            }

            pb.EndFigure(CanvasFigureLoop.Open);

            using (var geo = CanvasGeometry.CreatePath(pb))
            {
                ds.DrawGeometry(geo, GetDynamicColor(), 8f);
                ds.DrawGeometry(geo, Colors.White, 2f);
            }
        }
    }

    private void DrawBokehTunnel(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < 20; i++)
        {
            float scale = ((i + _waveOffset) % 20) / 20f;
            float p = _visualizerPeaks[i % count];
            ds.DrawCircle(center, scale * w * 0.5f + p * 50, GetDynamicColor(i * 10, (byte)(scale * 255)), 2f);
        }
    }

    private void DrawPrismShatter(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 10; i++)
        {
            float p = _visualizerPeaks[i % count];
            Vector2 pos = new Vector2((w / 10) * i, h / 2 - p * 200);
            float rot = _waveOffset + i;
            ds.DrawText("△", pos.X, pos.Y, GetDynamicColor(i * 20), new CanvasTextFormat { FontSize = 20 + p * 60 });
        }
    }

    private void DrawNeonCircuit(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 5; i++)
        {
            float y = (h / 6) * (i + 1);
            ds.DrawLine(0, y, w, y, Color.FromArgb(40, 255, 255, 255), 1f);
            float pulseX = (_waveOffset * 300 + i * 200) % w;
            float p = _visualizerPeaks[i % count];
            ds.FillCircle(pulseX, y, 5 + p * 20, GetDynamicColor(i * 50));
        }
    }

    private void DrawSpaceDust(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 100; i++)
        {
            float x = (float)(_rng.NextDouble() * w);
            float y = (float)(_rng.NextDouble() * h);
            float p = _visualizerPeaks[i % count];
            ds.FillCircle(x, y, 1 + p * 5, Color.FromArgb((byte)(p * 255), 255, 255, 255));
        }
    }

    private void DrawGlowGears(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float r = 100 + _totalVolume * 50;
        ds.DrawCircle(center, r, Colors.Cyan, 5f);

        for (int i = 0; i < 12; i++)
        {
            float angle = (i * 30 + _waveOffset * 50) * 0.0174f;
            Vector2 p1 = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * r;
            Vector2 p2 = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (r + 20);
            ds.DrawLine(p1, p2, Colors.Cyan, 10f);
        }
    }

    private void DrawDigitalHorizon(CanvasDrawingSession ds, float w, float h, int count)
    {
        float horizonY = h * 0.6f;

        for (int i = 0; i < 10; i++)
        {
            float p = _visualizerPeaks[i % count];
            float y = horizonY + (i * 40 + _waveOffset * 20) % (h - horizonY);
            ds.DrawLine(0, y, w, y, GetDynamicColor(i * 20, (byte)(p * 255)), 1f + p * 4);
        }
    }
    private void DrawSolarFlare(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float intensity = _totalVolume * 2f;
        ds.FillCircle(center, 30 + intensity * 20, Colors.Orange);
        ds.FillCircle(center, 20 + intensity * 10, Colors.Yellow);

        for (int i = 0; i < count; i++)
        {
            float angle = (i * 6.28318f / count) + _rotationAngle;
            float flareLen = 50 + _visualizerPeaks[i] * h * 0.6f;
            float flareWidth = 5 + _visualizerPeaks[i] * 20;

            Vector2 flareEnd = center + new Vector2((float)Math.Cos(angle) * flareLen, (float)Math.Sin(angle) * flareLen);
            ds.DrawLine(center, flareEnd, GetDynamicColor(i * 15, (byte)(200 * _visualizerPeaks[i])), flareWidth);
        }
    }

    private void DrawCrystalCave(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float crystalHeight = _visualizerPeaks[i] * h * 0.7f;
            float crystalWidth = (w / count) * 0.6f;

            Vector2[] crystal = new Vector2[]
            {
                new Vector2(x + crystalWidth / 2, h - crystalHeight),
                new Vector2(x + crystalWidth, h),
                new Vector2(x, h)
            };

            using (var geo = CanvasGeometry.CreatePolygon(ds, crystal))
            {
                ds.FillGeometry(geo, GetDynamicColor(i * 20, 180));
                ds.DrawGeometry(geo, Colors.White, 1f);
            }
        }
    }

    private void DrawTeslaCoil(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 basePos = new Vector2(w / 2, h * 0.8f);
        float sparkHeight = _visualizerPeaks[0] * h * 0.5f;

        ds.DrawLine(basePos, new Vector2(w / 2, h * 0.2f), Colors.Silver, 10f);

        for (int i = 0; i < 20; i++)
        {
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float length = sparkHeight * (0.5f + (float)_rng.NextDouble());
            Vector2 sparkEnd = new Vector2(w / 2, h * 0.2f) + new Vector2((float)Math.Cos(angle) * length, (float)-Math.Sin(angle) * length);
            ds.DrawLine(new Vector2(w / 2, h * 0.2f), sparkEnd, Colors.Cyan, 2f);
        }
    }

    private void DrawHurricaneEye(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        ds.FillCircle(center, 20 + _totalVolume * 30, Colors.White);

        for (int i = 0; i < 360; i += 10)
        {
            float angle = i * 0.0174533f + _rotationAngle;
            float radius = 30 + (i / 360f) * (h * 0.4f);
            radius += _visualizerPeaks[(i / 10) % count] * 50;

            Vector2 point = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
            ds.FillCircle(point, 3 + _totalVolume * 8, GetDynamicColor(i));
        }
    }

    private void DrawVolcanoEruption(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 volcanoBase = new Vector2(w / 2, h);
        Vector2 crater = new Vector2(w / 2, h * 0.7f);

        ds.DrawLine(new Vector2(w * 0.3f, h), crater, Colors.DarkGray, 20f);
        ds.DrawLine(new Vector2(w * 0.7f, h), crater, Colors.DarkGray, 20f);

        for (int i = 0; i < 50; i++)
        {
            if (_visualizerPeaks[i % count] > 0.3f)
            {
                float angle = -90 + (float)(_rng.NextDouble() * 60 - 30);
                float distance = _visualizerPeaks[i % count] * h * 0.5f * (float)_rng.NextDouble();
                Vector2 particle = crater + new Vector2((float)Math.Cos(angle * 0.0174533f) * distance, -(float)Math.Sin(angle * 0.0174533f) * distance);
                ds.FillCircle(particle, 4 + _visualizerPeaks[i % count] * 10, Colors.Orange);
            }
        }
    }

    private void DrawAuroraBorealis(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float waveHeight = (float)Math.Sin(_waveOffset + i * 0.1f) * 50;
            float intensity = _visualizerPeaks[i] * h * 0.5f;

            using (var pb = new CanvasPathBuilder(ds))
            {
                pb.BeginFigure(x, h * 0.5f + waveHeight);
                pb.AddLine(x + (w / count), h * 0.5f + (float)Math.Sin(_waveOffset + (i + 1) * 0.1f) * 50);
                pb.AddLine(x + (w / count), h * 0.5f + waveHeight - intensity);
                pb.AddLine(x, h * 0.5f + waveHeight - intensity);
                pb.EndFigure(CanvasFigureLoop.Closed);

                using (var geo = CanvasGeometry.CreatePath(pb))
                {
                    ds.FillGeometry(geo, GetDynamicColor(i * 10, 100));
                }
            }
        }
    }

    private void DrawQuantumBubbles(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 30; i++)
        {
            float bubbleSize = 10 + _visualizerPeaks[i % count] * 60;
            float x = (float)_rng.NextDouble() * w;
            float y = (float)_rng.NextDouble() * h;

            ds.DrawCircle(x, y, bubbleSize, GetDynamicColor(i * 12), 2f);
            if (_visualizerPeaks[i % count] > 0.5f)
            {
                ds.FillCircle(x, y, bubbleSize * 0.3f, Colors.White);
            }
        }
    }

    private void DrawMagneticField(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 north = new Vector2(w * 0.4f, h * 0.3f);
        Vector2 south = new Vector2(w * 0.6f, h * 0.7f);

        for (int i = 0; i < count; i++)
        {
            float curve = _visualizerPeaks[i] * 100;
            using (var pb = new CanvasPathBuilder(ds))
            {
                pb.BeginFigure(north);
                pb.AddQuadraticBezier(new Vector2(w / 2 + curve, h / 2), south);
                pb.EndFigure(CanvasFigureLoop.Open);

                using (var geo = CanvasGeometry.CreatePath(pb))
                {
                    ds.DrawGeometry(geo, GetDynamicColor(i * 15), 2f);
                }
            }
        }

        ds.FillCircle(north, 15, Colors.Red);
        ds.FillCircle(south, 15, Colors.Blue);
    }

    private void DrawSolarSail(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        for (int i = 0; i < count; i++)
        {
            float angle = (i * 6.28318f / count) + _rotationAngle;
            float radius = 50 + _visualizerPeaks[i] * 80;
            Vector2 point = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);

            Vector2[] sail = new Vector2[]
            {
                center,
                point,
                center + new Vector2((float)Math.Cos(angle + 0.3f) * radius, (float)Math.Sin(angle + 0.3f) * radius)
            };

            using (var geo = CanvasGeometry.CreatePolygon(ds, sail))
            {
                ds.FillGeometry(geo, GetDynamicColor(i * 10, 80));
                ds.DrawGeometry(geo, Colors.White, 1f);
            }
        }
    }

    private void DrawIceCrystals(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float y = h - _visualizerPeaks[i] * h * 0.8f;
            float crystalSize = 10 + _visualizerPeaks[i] * 30;

            for (int arm = 0; arm < 6; arm++)
            {
                float angle = arm * 60 * 0.0174533f;
                Vector2 armEnd = new Vector2(x + (float)Math.Cos(angle) * crystalSize, y + (float)Math.Sin(angle) * crystalSize);
                ds.DrawLine(new Vector2(x, y), armEnd, Colors.Cyan, 2f);
                Vector2 branch = armEnd + new Vector2((float)Math.Cos(angle + 0.5f) * crystalSize * 0.5f, (float)Math.Sin(angle + 0.5f) * crystalSize * 0.5f);
                ds.DrawLine(armEnd, branch, Colors.LightBlue, 1f);
            }
        }
    }

    private void DrawThunderstorm(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < 5; i++)
        {
            ds.FillCircle(w * (0.2f + i * 0.15f), h * 0.2f, 40, Colors.DarkGray);
        }

        for (int i = 0; i < count; i++)
        {
            if (_visualizerPeaks[i] > 0.6f)
            {
                float startX = w * (0.3f + (float)_rng.NextDouble() * 0.4f);
                using (var pb = new CanvasPathBuilder(ds))
                {
                    pb.BeginFigure(startX, h * 0.2f);
                    pb.AddLine(startX + (float)_rng.Next(-20, 20), h * 0.4f);
                    pb.AddLine(startX + (float)_rng.Next(-20, 20), h * 0.6f);
                    pb.AddLine(startX + (float)_rng.Next(-20, 20), h * 0.8f);
                    pb.EndFigure(CanvasFigureLoop.Open);

                    using (var geo = CanvasGeometry.CreatePath(pb))
                    {
                        ds.DrawGeometry(geo, Colors.Yellow, 3f);
                    }
                }
            }
        }
    }

    private void DrawRainbowBridge(CanvasDrawingSession ds, float w, float h, int count)
    {
        float archHeight = h * 0.5f;

        for (int i = 0; i < 7; i++)
        {
            float thickness = 10 + _visualizerPeaks[i % count] * 20;

            using (var pb = new CanvasPathBuilder(ds))
            {
                pb.BeginFigure(0, h);
                pb.AddQuadraticBezier(new Vector2(w / 2, archHeight + i * 15), new Vector2(w, h));
                pb.EndFigure(CanvasFigureLoop.Open);

                using (var geo = CanvasGeometry.CreatePath(pb))
                {
                    Color[] rainbowColors = { Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue, Colors.Indigo, Colors.Violet };
                    ds.DrawGeometry(geo, rainbowColors[i % 7], thickness);
                }
            }
        }
    }

    private void DrawFireworksDisplay(CanvasDrawingSession ds, float w, float h, int count)
    {
        if (_totalVolume > 0.7f && _rng.NextDouble() < 0.1f)
        {
            _explosions.Add((new Vector2((float)_rng.NextDouble() * w, (float)_rng.NextDouble() * h * 0.5f), new Vector2(0, 0), 1.0f));
        }

        for (int i = _explosions.Count - 1; i >= 0; i--)
        {
            var exp = _explosions[i];
            exp.Life -= 0.02f;

            for (int j = 0; j < 20; j++)
            {
                float angle = j * 18 * 0.0174533f;
                float radius = (1 - exp.Life) * 100;
                Vector2 particle = exp.Pos + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                ds.FillCircle(particle, 3 * exp.Life, GetDynamicColor(j * 18, (byte)(exp.Life * 255)));
            }

            _explosions[i] = exp;
            if (exp.Life <= 0) _explosions.RemoveAt(i);
        }
    }

    private void DrawSandstorm(CanvasDrawingSession ds, float w, float h, int count)
    {
        float intensity = _totalVolume * 100;

        for (int i = 0; i < 200; i++)
        {
            float x = ((_waveOffset * 200 + i * 7) % w);
            float y = (float)_rng.NextDouble() * h;
            float size = 2 + intensity * (float)_rng.NextDouble();
            ds.FillCircle(x, y, size, Color.FromArgb((byte)(intensity * 2), 210, 180, 140));
        }
    }

    private void DrawTornadoAlley(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h);

        for (int i = 0; i < count; i++)
        {
            float y = i * (h / count);
            float radius = 20 + _visualizerPeaks[i] * 100;
            radius *= (1 - (y / h));
            ds.DrawEllipse(center.X, y, radius, 10, Colors.Gray, 2f);
        }

        // Debris
        for (int i = 0; i < 30; i++)
        {
            if (_totalVolume > 0.5f)
            {
                float x = w / 2 + (float)(_rng.NextDouble() - 0.5) * _totalVolume * 200;
                float y = (float)_rng.NextDouble() * h;
                ds.FillCircle(x, y, 3, Colors.Brown);
            }
        }
    }

    private void DrawBioluminescentBay(CanvasDrawingSession ds, float w, float h, int count)
    {
        // Dark background assumed
        for (int i = 0; i < count * 2; i++)
        {
            float x = (float)_rng.NextDouble() * w;
            float y = (float)_rng.NextDouble() * h;
            float glow = _visualizerPeaks[i % count] * 0.8f + 0.2f;

            ds.FillCircle(x, y, 2 + _visualizerPeaks[i % count] * 10,
                          Color.FromArgb((byte)(glow * 255), 0, 255, 100));
        }

        // Bioluminescent waves
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float y = h * 0.7f + (float)Math.Sin(_waveOffset + i * 0.2f) * 30;
            y += _visualizerPeaks[i] * 50;

            ds.FillCircle(x, y, 5 + _visualizerPeaks[i] * 15, Color.FromArgb(200, 0, 200, 150));
        }
    }

    private void DrawSolarEclipse(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        // Sun
        ds.FillCircle(center, 100, Colors.Orange);

        // Moon (moving with audio)
        float moonOffset = _totalVolume * 100;
        Vector2 moonPos = new Vector2(w / 2 + moonOffset, h / 2);
        ds.FillCircle(moonPos, 95, Colors.Black);

        // Corona (visible during eclipse)
        for (int i = 0; i < count; i++)
        {
            float angle = (i * 6.28318f / count) + _rotationAngle;
            float coronaLength = 30 + _visualizerPeaks[i] * 80;
            Vector2 coronaEnd = center + new Vector2((float)Math.Cos(angle) * coronaLength, (float)Math.Sin(angle) * coronaLength);
            ds.DrawLine(center, coronaEnd, Colors.White, 2f);
        }
    }

    private void DrawMoonPhases(CanvasDrawingSession ds, float w, float h, int count)
    {
        for (int i = 0; i < Math.Min(count, 8); i++)
        {
            float x = w * (0.1f + i * 0.1f);
            float y = h * 0.5f;
            float phase = _visualizerPeaks[i];

            ds.DrawCircle(x, y, 30, Colors.White, 2f);

            // Shadow representing phase
            ds.FillRectangle(x - 30 + (phase * 60), y - 30, 60 - (phase * 60), 60, Colors.Black);
            ds.DrawText($"{(phase * 100):F0}%", x - 15, y + 40, Colors.White);
        }
    }

    private void DrawStainedGlass(CanvasDrawingSession ds, float w, float h, int count)
    {
        int cols = 8;
        int rows = 8;
        float cellW = w / cols;
        float cellH = h / rows;

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                int index = (x + y) % count;
                float brightness = _visualizerPeaks[index];

                Rect cell = new Rect(x * cellW, y * cellH, cellW, cellH);
                ds.FillRectangle(cell, GetDynamicColor((x * 45 + y * 45) % 360, (byte)(brightness * 200 + 55)));
                ds.DrawRectangle(cell, Colors.Black, 2f);
            }
        }
    }

    private void DrawKaleidoscope(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        int segments = 12;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = (i * 360f / segments) * 0.0174533f;
            float angle2 = ((i + 1) * 360f / segments) * 0.0174533f;
            float radius = 100 + _visualizerPeaks[i % count] * 150;

            Vector2 p1 = center + new Vector2((float)Math.Cos(angle1) * radius, (float)Math.Sin(angle1) * radius);
            Vector2 p2 = center + new Vector2((float)Math.Cos(angle2) * radius, (float)Math.Sin(angle2) * radius);

            using (var pb = new CanvasPathBuilder(ds))
            {
                pb.BeginFigure(center);
                pb.AddLine(p1);
                pb.AddLine(p2);
                pb.EndFigure(CanvasFigureLoop.Closed);

                using (var geo = CanvasGeometry.CreatePath(pb))
                {
                    ds.FillGeometry(geo, GetDynamicColor(i * 30, 150));
                    ds.DrawGeometry(geo, Colors.White, 1f);
                }
            }
        }
    }

    private void DrawLavaLamp(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        // Lamp body
        ds.DrawRoundedRectangle(center.X - 80, center.Y - 150, 160, 300, 40, 40, Colors.Gray, 3f);

        // Lava blobs
        for (int i = 0; i < 10; i++)
        {
            float y = center.Y - 100 + (float)Math.Sin(_waveOffset + i) * 80;
            y += _visualizerPeaks[i % count] * 60;
            float size = 20 + _visualizerPeaks[i % count] * 30;

            ds.FillCircle(center.X, y, size, Color.FromArgb(200, 255, 80, 40));
            ds.FillCircle(center.X, y, size * 0.6f, Colors.Orange);
        }
    }

    private void DrawElectricFence(CanvasDrawingSession ds, float w, float h, int count)
    {
        // Fence posts
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            ds.DrawLine(x, h * 0.3f, x, h, Colors.Silver, 4f);
        }

        // Electric arcs between posts
        for (int i = 0; i < count - 1; i++)
        {
            if (_visualizerPeaks[i] > 0.4f)
            {
                float x1 = (w / count) * i;
                float x2 = (w / count) * (i + 1);
                float y = h * 0.5f + (float)Math.Sin(_waveOffset * 20) * 20;

                using (var pb = new CanvasPathBuilder(ds))
                {
                    pb.BeginFigure(x1, y);
                    pb.AddLine(x1 + (x2 - x1) * 0.3f, y + (float)_rng.Next(-30, 30));
                    pb.AddLine(x1 + (x2 - x1) * 0.7f, y + (float)_rng.Next(-30, 30));
                    pb.AddLine(x2, y);
                    pb.EndFigure(CanvasFigureLoop.Open);

                    using (var geo = CanvasGeometry.CreatePath(pb))
                    {
                        ds.DrawGeometry(geo, Colors.Cyan, 3f);
                    }
                }
            }
        }
    }

    private void DrawHologramProjector(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 projectorBase = new Vector2(w / 2, h * 0.8f);
        ds.FillRoundedRectangle(projectorBase.X - 40, projectorBase.Y - 20, 80, 40, 10, 10, Colors.DarkGray);
        ds.FillCircle(projectorBase.X, projectorBase.Y - 20, 20, Colors.Gray);

        // Hologram beam
        for (int i = 0; i < count; i++)
        {
            float angle = (float)Math.Sin(_waveOffset + i * 0.2f) * 0.5f;
            float height = _visualizerPeaks[i] * h * 0.5f;
            float x = projectorBase.X + angle * 50;
            float y = projectorBase.Y - 20 - height;

            ds.DrawLine(new Vector2(projectorBase.X, projectorBase.Y - 20), new Vector2(x, y),
                        GetDynamicColor(i * 15, (byte)(_visualizerPeaks[i] * 200)), 3f);

            // Hologram object (sphere)
            ds.DrawCircle(x, y, 10 + _visualizerPeaks[i] * 20, GetDynamicColor(i * 15, 150), 2f);
        }
    }

    private void DrawAtomicNucleus(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);

        // Nucleus
        ds.FillCircle(center, 20 + _totalVolume * 30, Colors.Red);
        ds.FillCircle(center, 10, Colors.Orange);

        // Electron orbits
        for (int i = 0; i < 3; i++)
        {
            float radius = 60 + _visualizerPeaks[i * 5] * 80;
            ds.DrawEllipse(center.X, center.Y, radius, radius * 0.5f + (float)Math.Sin(_rotationAngle) * 20,
                           GetDynamicColor(i * 120), 2f);

            // Electron
            float electronAngle = _rotationAngle * (i + 1) * 2;
            Vector2 electron = center + new Vector2((float)Math.Cos(electronAngle) * radius,
                                                      (float)Math.Sin(electronAngle) * radius * 0.5f);
            ds.FillCircle(electron, 5 + _visualizerPeaks[i * 5] * 8, Colors.White);
        }
    }

    private void DrawSolarPanelArray(CanvasDrawingSession ds, float w, float h, int count)
    {
        int cols = 5;
        int rows = 4;
        float panelW = w / cols;
        float panelH = h / rows;

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                int index = (x * rows + y) % count;
                float efficiency = _visualizerPeaks[index];

                Rect panel = new Rect(x * panelW + 5, y * panelH + 5, panelW - 10, panelH - 10);
                ds.FillRectangle(panel, Color.FromArgb((byte)(efficiency * 255), 0, 100, 200));
                ds.DrawRectangle(panel, Colors.White, 1f);
                ds.DrawLine((float)(panel.Left + panelW / 3), (float)(panel.Top), (float)(panel.Left + panelW / 3), (float)panel.Bottom, Colors.White, 1f);
                ds.DrawLine((float)(panel.Left + panelW * 2 / 3), (float)panel.Top, (float)(panel.Left + panelW * 2 / 3), (float)panel.Bottom, Colors.White, 1f);
            }
        }
    }

    private void DrawWindTurbine(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 towerBase = new Vector2(w / 2, h);
        Vector2 hub = new Vector2(w / 2, h * 0.3f);

        ds.DrawLine(towerBase, hub, Colors.Silver, 8f);

        for (int i = 0; i < 3; i++)
        {
            float angle = _rotationAngle * 2 + i * 120 * 0.0174533f;
            float bladeLength = 80 + _visualizerPeaks[i % count] * 60;
            Vector2 bladeEnd = hub + new Vector2((float)Math.Cos(angle) * bladeLength, (float)Math.Sin(angle) * bladeLength);

            ds.DrawLine(hub, bladeEnd, Colors.White, 5f);
        }

        ds.FillCircle(hub, 10, Colors.Gray);
    }

    private void DrawDamWaterfall(CanvasDrawingSession ds, float w, float h, int count)
    {
        ds.FillRectangle(0, h * 0.3f, w, h * 0.2f, Colors.DarkGray);

        float flowRate = _totalVolume * 2;
        for (int i = 0; i < count; i++)
        {
            float x = (w / count) * i;
            float flowWidth = 2 + _visualizerPeaks[i] * 10;

            for (int j = 0; j < 10; j++)
            {
                float yOffset = (float)Math.Sin(_waveOffset * 5 + i) * 10;
                ds.DrawLine(x, h * 0.5f + yOffset, x, h, Colors.Cyan, flowWidth);
            }
        }

        for (int i = 0; i < 20; i++)
        {
            float splashY = h - (float)_rng.NextDouble() * 20;
            ds.FillCircle(w * (float)_rng.NextDouble(), splashY, 2 + flowRate * 2, Colors.LightBlue);
        }
    }
    private void DrawClockworkMechanism(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 center = new Vector2(w / 2, h / 2);
        float mainGearAngle = _rotationAngle * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float angle = mainGearAngle + (i * 6.28318f / count);
            float radius = 60 + _visualizerPeaks[i] * 40;
            Vector2 tooth = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
            ds.FillCircle(tooth, 5, Colors.Gold);
        }

        ds.DrawCircle(center, 50, Colors.Gold, 3f);

        for (int g = 0; g < 4; g++)
        {
            float gearAngle = _rotationAngle * (g + 1) * -0.3f;
            Vector2 gearPos = center + new Vector2((float)Math.Cos(gearAngle + g) * 100, (float)Math.Sin(gearAngle + g) * 80);
            ds.DrawCircle(gearPos, 20, Colors.DodgerBlue, 2f);

            for (int i = 0; i < 8; i++)
            {
                float toothAngle = gearAngle + (i * 45 * 0.0174533f);
                Vector2 tooth = gearPos + new Vector2((float)Math.Cos(toothAngle) * 25, (float)Math.Sin(toothAngle) * 25);
                ds.FillCircle(tooth, 3, Colors.LimeGreen);
            }
        }
    }

    private void DrawMorseCodeBeacon(CanvasDrawingSession ds, float w, float h, int count)
    {
        Vector2 beaconPos = new Vector2(w / 2, h * 0.7f);
        ds.DrawLine(beaconPos, new Vector2(w / 2, h), Colors.Silver, 4f);
        ds.FillRectangle(beaconPos.X - 15, beaconPos.Y - 10, 30, 20, Colors.DarkGray);
        bool isFlashing = _totalVolume > 0.5f;
        float flashAlpha = isFlashing ? Math.Clamp(Math.Abs((float)Math.Sin(_waveOffset * 20)) * 255, 0, 255) : 50;

        for (int i = 0; i < 360; i += 15)
        {
            float angle = i * 0.0174533f;
            float beamLength = 50 + (isFlashing ? 100 : 20);
            Vector2 beamEnd = beaconPos + new Vector2((float)Math.Cos(angle) * beamLength, (float)Math.Sin(angle) * beamLength);
            ds.DrawLine(beaconPos, beamEnd, Color.FromArgb((byte)flashAlpha, 255, 255, 100), 2f);
        }

        ds.FillCircle(beaconPos, 8, Colors.Yellow);
        string pattern = _totalVolume > 0.6f ? "● ● ●   ● ●   ● ● ●" : "●   ●   ●";
        ds.DrawText(pattern, beaconPos.X - 50, beaconPos.Y - 40, Colors.White);
    }
}