using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;

namespace WPF_Version
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private int? selectedAsioDriverIndex;
        private float currentSampleValue;
        private double peakFrequency;
        private const int sampleRate = 44100;
        private const int fftLength = 4096 * 2;

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string pName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(pName));
        }

        public List<string> AsioDrivers { get; set; }
        public int? SelectedAsioDriverIndex { get => selectedAsioDriverIndex; set { selectedAsioDriverIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); } }
        public bool CanStart { get => selectedAsioDriverIndex != null; }

        public float CurrentSampleValue { get => currentSampleValue; set { currentSampleValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndicatorWidth)); } }
        public float IndicatorWidth { get => currentSampleValue * 256f; }

        public double PeakFrequency { get => peakFrequency; set { peakFrequency = value; OnPropertyChanged(); OnPropertyChanged(nameof(PeakFrequencyString)); } }
        public string PeakFrequencyString { get => $"{peakFrequency} hz"; }

        private SampleAggregator sampleAggregator = new SampleAggregator(fftLength) { PerformFFT = true };

        public MainViewModel()
        {
            AsioDrivers = AsioOut.GetDriverNames().ToList();
            if (AsioDrivers.Count > 0) SelectedAsioDriverIndex = 0;
            sampleAggregator.FftCalculated += SampleAggregator_FftCalculated;
        }

        private PointCollection wavePoints;
        public PointCollection WavePoints { get => wavePoints; set { wavePoints = value; OnPropertyChanged(); } }

        private PointCollection ftPoints;
        public PointCollection FTPoints { get => ftPoints; set { ftPoints = value; OnPropertyChanged(); } }

        private const int peakCount = 10;
        private void SampleAggregator_FftCalculated(object sender, FftEventArgs e)
        {
            var pc = new PointCollection(e.Result.Select((c, x) => new System.Windows.Point(x * sampleRate / fftLength, ModSquared(c) * fftLength * 5)));
            pc.Freeze();
            FTPoints = pc;

            double peakFreq = 0;
            double peakAmp = 0;
            foreach(System.Windows.Point p in pc)
            {
                if (p.X >= fftLength / 2)
                    break;
                if (p.Y > 10 && p.X > 60)
                {
                    if (p.Y > peakAmp)
                    {
                        peakAmp = p.Y;
                        peakFreq = p.X;
                    }
                    else
                        break;
                }
            }

            PeakFrequency = peakFreq * 2;
            //Points = e.Result.Select(p => DoubleComplex.FromFloatComplex(p)).ToList();
            //foreach (Complex cmplx in e.Result) System.Diagnostics.Debug.WriteLine($"{cmplx.X} + {cmplx.Y}i");
        }

        double ModSquared(Complex c) => Math.Pow(c.X, 2) + Math.Pow(c.Y, 2);

        private AsioOut @asio;
        public void Start()
        {
            if (@asio == null)
            {
                @asio = new AsioOut(selectedAsioDriverIndex ?? 0) { AutoStop = false };
                @asio.AudioAvailable += asio_AudioAvailable;
                @asio.InitRecordAndPlayback(null, @asio.DriverInputChannelCount, sampleRate);
                @asio.Play();
            }
            else
                Stop();
        }

        public void Stop()
        {
            @asio.Stop();
            @asio.Dispose();
        }

        private float[] samples;
        private void asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            var sampleCount = e.SamplesPerBuffer * @asio.DriverInputChannelCount;
            samples = new float[sampleCount];
            e.GetAsInterleavedSamples(samples);
            /*foreach(float sample in samples)
            {
                sampleAggregator.Add(sample);
            }*/

            var wpc = new PointCollection(samples.Select((y, x) => new System.Windows.Point(x, y * 100)));
            wpc.Freeze();
            WavePoints = wpc;

            var DFTResult = DFT.Compute(samples); 
            var ftpc = new PointCollection(DFTResult.Select((c, x) => new System.Windows.Point(x * sampleRate / samples.Length, ModSquared(c) * 48 / samples.Length)));
            ftpc.Freeze();
            FTPoints = ftpc;

            double peakFreq = 0;
            double peakAmp = 0;
            foreach (System.Windows.Point p in ftpc)
            {
                if (p.X >= samples.Length / 2)
                    break;
                if (p.Y > 10 && p.X > 60)
                {
                    if (p.Y > peakAmp)
                    {
                        peakAmp = p.Y;
                        peakFreq = p.X;
                    }
                    else
                        break;
                }
            }

            PeakFrequency = peakFreq * 2;

        }
    }
}
