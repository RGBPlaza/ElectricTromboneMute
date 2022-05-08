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
using NAudio.Midi;
using Pitch;
using NAudio.Wave.SampleProviders;
using System.Threading.Tasks;

namespace BruhWave_Deco
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string pName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(pName));
        }

        private bool isRunning;
        private int? selectedAsioDriverIndex;
        private int? selectedMidiPortIndex;
        private float currentSampleValue;
        private float peakFrequency;
        public const int sampleRate = 44100;
        private List<int> MidiPortNumbers;
        private int currentMidiNoteNumber;
        private int currentCentDifference;

        public int CurrentMidiNoteNumber { get => currentMidiNoteNumber; set { currentMidiNoteNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentNote)); } }
        public string CurrentNote { get => CurrentMidiNoteNumber == 0 ? "Ø" : notes[currentMidiNoteNumber % 12]; }

        public int CurrentCentDifference { get => currentCentDifference; set { currentCentDifference = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentCentDiffString)); OnPropertyChanged(nameof(CurrentCentSignString)); } }
        public string CurrentCentSignString { get => currentCentDifference >= 0 ? "+" : "-"; }
        public string CurrentCentDiffString { get => $" {Math.Abs(currentCentDifference):00} Cents"; }

        public List<string> AsioDrivers { get; set; }
        public List<string> MidiPorts { get; set; }
        public int? SelectedAsioDriverIndex { get => selectedAsioDriverIndex; set { selectedAsioDriverIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); } }
        public int? SelectedMidiPortIndex { get => selectedMidiPortIndex; set { selectedMidiPortIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); } }

        public bool CanStart { get => selectedAsioDriverIndex != null && (selectedMidiPortIndex != null || !midi_mode); }

        public float CurrentSampleValue { get => currentSampleValue; set { currentSampleValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndicatorWidth)); } }
        public float IndicatorWidth { get => currentSampleValue * 256f; }

        public float PeakFrequency { get => peakFrequency; set { peakFrequency = value; OnPropertyChanged(); OnPropertyChanged(nameof(PeakFrequencyString)); } }
        public string PeakFrequencyString { get => $"{peakFrequency:000} Hz"; }

        public bool IsRunning { get => isRunning; set { isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(PowerImageSource)); } }
        public string PowerImageSource { get => isRunning ? "Assets/ActivatedPower.png" : "Assets/DeactivatedPower.png"; }

        // Switch Between MIDI and WAVE mode
        private const bool midi_mode = false;


        //private PitchTracker pitchTracker;

        public MainViewModel()
        {
            AsioDrivers = AsioOut.GetDriverNames().ToList();
            MidiPorts = new List<string>();
            MidiPortNumbers = new List<int>();
            for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            {
                var device = MidiOut.DeviceInfo(i);
                if (device.Technology == MidiOutTechnology.MidiPort)
                {
                    MidiPorts.Add(device.ProductName);
                    MidiPortNumbers.Add(i);
                }
            }
            if (AsioDrivers.Count > 0) SelectedAsioDriverIndex = 0;
            if (MidiPorts.Count > 0) SelectedMidiPortIndex = 0;
            //pitchTracker = new PitchTracker() { SampleRate = sampleRate, PitchRecordsPerSecond = 150 };
            //pitchTracker.PitchDetected += PitchTracker_PitchDetected;
        }

        private int previousNoteNumber = 0;
        private float previousCentDifferenceFromProxy = 0;
        private int proxyNoteNumber = 0;

        private void PitchTracker_PitchDetected(PitchTracker sender, PitchTracker.PitchRecord pitchRecord)
        {
            float pitchFreq = pitchRecord.Pitch * 2;
            // moved elsewhere
        }

        private PointCollection wavePoints;
        public PointCollection WavePoints { get => wavePoints; set { wavePoints = value; OnPropertyChanged(); } }

        private AsioOut asio;
        private MidiOut midi;
        private ISampleProvider sampleProvider;
        public void Start()
        {
            if (!isRunning)
            {
                asio = new AsioOut(selectedAsioDriverIndex ?? 0) { AutoStop = false };

                if (midi_mode)
                {
                    asio.InitRecordAndPlayback(null, 1, sampleRate);
                    asio.AudioAvailable += asio_AudioAvailable;
                    midi = new MidiOut(MidiPortNumbers[SelectedMidiPortIndex ?? 0]);
                    asio.Play();
                }
                else
                {
                    //asio.AudioAvailable += (s, e) => { System.Diagnostics.Debug.WriteLine("Hi"); };
                    sampleProvider = new SineWaveProvider(asio);
                    asio.InitRecordAndPlayback(new SampleToWaveProvider16(sampleProvider), 1, sampleRate);
                    asio.Play();
                }

                IsRunning = true;
            }
        }

        public void Stop()
        {
            if (isRunning)
            {
                asio.Stop();
                asio.Dispose();
                midi?.Reset();
                midi?.Dispose();
                sampleProvider = null;
                WavePoints = null;
                IsRunning = false;
            }
        }

        private float[] samples;
        private void asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            var sampleCount = e.SamplesPerBuffer * asio.DriverInputChannelCount;
            samples = new float[sampleCount];
            e.GetAsInterleavedSamples(samples);
            //pitchTracker.ProcessBuffer(samples);

            float pitchFreq = NWaves.Features.Pitch.FromAutoCorrelation(samples, sampleRate); PeakFrequency = pitchFreq;
            if (pitchFreq == 0)
            {
                CurrentMidiNoteNumber = 0;
                CurrentCentDifference = 0;
                var stopEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOff, proxyNoteNumber, 0);
                midi.Send(stopEvent.GetAsShortMessage());
                var revertPitchBendEvent = new PitchWheelChangeEvent(0, 1, 0x2000);
                midi.Send(revertPitchBendEvent.GetAsShortMessage());

                previousNoteNumber = 0;
                return;
            }

            int actualNoteNumber = (int)MathF.Round(12 * MathF.Log2(pitchFreq / 440f) + 69);
            float actualNoteFreq = MathF.Pow(2, (actualNoteNumber - 69) / 12f) * 440;
            float centDifferenceFromActual = 1200 * MathF.Log2(pitchFreq / actualNoteFreq);

            if (actualNoteNumber != previousNoteNumber)
            {
                CurrentMidiNoteNumber = actualNoteNumber;
                if (previousNoteNumber == 0)
                {
                    proxyNoteNumber = actualNoteNumber;
                    var noteEvent = new NoteOnEvent(0, 1, proxyNoteNumber, 100, 0);
                    midi.Send(noteEvent.GetAsShortMessage());
                }
                previousNoteNumber = actualNoteNumber;
            }

            //int proxyNoteNumber = smooshFactor*(actualNoteNumber/smooshFactor);
            float proxyNoteFreq = MathF.Pow(2, (proxyNoteNumber - 69) / 12f) * 440;
            float centDifferenceFromProxy = 1200 * MathF.Log2(pitchFreq / proxyNoteFreq);

            if (centDifferenceFromProxy != previousCentDifferenceFromProxy)
            {
                if (centDifferenceFromProxy > 1599 || centDifferenceFromProxy < -1599)
                {
                    var stopEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOff, proxyNoteNumber, 0);
                    midi.Send(stopEvent.GetAsShortMessage());

                    proxyNoteNumber = actualNoteNumber;
                    var noteEvent = new NoteOnEvent(0, 1, proxyNoteNumber, 100, 0);
                    midi.Send(noteEvent.GetAsShortMessage());

                    proxyNoteFreq = MathF.Pow(2, (proxyNoteNumber - 69) / 12f) * 440;
                    centDifferenceFromProxy = 1200 * MathF.Log2(pitchFreq / proxyNoteFreq);
                }
                CurrentCentDifference = (int)centDifferenceFromActual;
                var pitchBendEvent = new PitchWheelChangeEvent(0, 1, 0x2000 + (int)(centDifferenceFromProxy / 100d * 0x200));
                midi.Send(pitchBendEvent.GetAsShortMessage());
                previousCentDifferenceFromProxy = centDifferenceFromProxy;
            }

            //var wpc = new PointCollection(samples.Select((y, x) => new System.Windows.Point(x, y * 100)));
            //wpc.Freeze();
            //WavePoints = wpc;

        }

        private string[] notes = new string[12] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };

    }

    class SampleProvider : ISampleProvider
    {
        private readonly WaveFormat waveformat;

        const int max_freq = 2000;
        private NWaves.Signals.Builders.Base.SignalBuilder[] builders = new NWaves.Signals.Builders.Base.SignalBuilder[max_freq];

        private const int count = 2048;
        //private int internal_offset = 0;

        //private float[] sigSamples = new float[count];
        private bool sound_off = false;

        public SampleProvider(AsioOut asio)
        {
            asio.AudioAvailable += Asio_AudioAvailable;
            waveformat = WaveFormat.CreateIeeeFloatWaveFormat(MainViewModel.sampleRate, 1);
            for (int i = 0; i < max_freq; i++)
            {
                builders[i] = new NWaves.Signals.Builders.SquareWaveBuilder()
                    .SetParameter("freq", i)
                    .SampledAt(MainViewModel.sampleRate);
            }
            //sigSamples = builder.Build().Samples;
        }

        public WaveFormat WaveFormat { get => waveformat; }
        private int freq = 440;
        private int oldFreq = 440;

        public int Read(float[] buffer, int offset, int count)
        {
            // Offset is always 0
            if (sound_off)
                return 0;
            else
            {
                /*
                if (freq != oldFreq)
                {
                    builders[freq].SetParameter("freq", freq);
                    oldFreq = freq;
                }*/

                int c = 0;
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = builders[freq].NextSample();

                    c++;
                }

                return c;
            };
        }

        private void Asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            {
                var sampleCount = e.SamplesPerBuffer * ((AsioOut)sender).DriverInputChannelCount;
                //System.Diagnostics.Debug.WriteLine(sampleCount);
                float[] samples = new float[sampleCount];
                e.GetAsInterleavedSamples(samples);


                float pitchFreq = NWaves.Features.Pitch.FromAutoCorrelation(samples, MainViewModel.sampleRate);

                float max = float.NegativeInfinity;
                float min = float.PositiveInfinity;
                foreach (float sample in samples)
                {
                    if (sample > max)
                        max = sample;
                    if (sample < min)
                        min = sample;
                }

                freq = (int)MathF.Round(pitchFreq);

                /*if (pitchFreq <= 0)
                    sound_off = true;
                else
                {
                    builder = builder
                        .SetParameter("frequency", pitchFreq);
                    sound_off = false;
                }*/
            }
        }

    }

    // Shit
    class QueueSampleProvider : ISampleProvider
    {
        private readonly WaveFormat waveformat;

        private NWaves.Signals.Builders.Base.SignalBuilder builder;

        private const int count = 2048;


        public QueueSampleProvider(AsioOut asio)
        {
            asio.AudioAvailable += Asio_AudioAvailable;
            waveformat = WaveFormat.CreateIeeeFloatWaveFormat(MainViewModel.sampleRate, 1);
            builder = new NWaves.Signals.Builders.SineBuilder()
                .SampledAt(MainViewModel.sampleRate);
            bufferQueue = new Queue<float[]>();
            //sigSamples = builder.Build().Samples;
        }

        public WaveFormat WaveFormat { get => waveformat; }
        private readonly Queue<float[]> bufferQueue;

        public int Read(float[] buffer, int offset, int count)
        {
            if (bufferQueue.TryDequeue(out float[] samples))
            {
                Buffer.BlockCopy(samples, offset, buffer, 0, count);
                return count;
            }
            else
                return 0;
        }

        private void Asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            {
                var sampleCount = e.SamplesPerBuffer * ((AsioOut)sender).DriverInputChannelCount;
                //System.Diagnostics.Debug.WriteLine(sampleCount);
                float[] samples = new float[sampleCount];
                e.GetAsInterleavedSamples(samples);


                float pitchFreq = NWaves.Features.Pitch.FromAutoCorrelation(samples, MainViewModel.sampleRate);

                float max = float.NegativeInfinity;
                float min = float.PositiveInfinity;
                foreach (float sample in samples)
                {
                    if (sample > max)
                        max = sample;
                    if (sample < min)
                        min = sample;
                }

                if (pitchFreq > 0)
                {
                    builder = builder
                        .SetParameter("frequency", pitchFreq);
                    float[] newSamples = new float[count];
                    for (int i = 0; i < count; i++)
                        newSamples[i] = builder.NextSample();
                    bufferQueue.Enqueue(newSamples);
                }
            }

        }

    }

    class SineWaveProvider : ISampleProvider
    {
        private float[] waveTable;
        private double phase;
        private double currentPhaseStep;
        private double targetPhaseStep;
        private double frequency;
        private double phaseStepDelta;
        private bool seekFreq;

        public SineWaveProvider(AsioOut asio)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(MainViewModel.sampleRate, 1);
            waveTable = new float[MainViewModel.sampleRate];
            for (int index = 0; index < MainViewModel.sampleRate; ++index)
                waveTable[index] = (float)Math.Sin(2 * Math.PI * (double)index / MainViewModel.sampleRate);
            // For sawtooth instead of sine: waveTable[index] = (float)index / sampleRate;
            Frequency = 440f;
            Volume = 1f;
            PortamentoTime = 0.002; // thought this was in seconds, but glide seems to take a bit longer
            asio.AudioAvailable += Asio_AudioAvailable;
        }

        public double PortamentoTime { get; set; }

        public double Frequency
        {
            get
            {
                return frequency;
            }
            set
            {
                frequency = value;
                seekFreq = true;
            }
        }

        public float Volume { get; set; }

        public WaveFormat WaveFormat { get; private set; }

        public int Read(float[] buffer, int offset, int count)
        {
            if (seekFreq) // process frequency change only once per call to Read
            {
                targetPhaseStep = waveTable.Length * (frequency / WaveFormat.SampleRate);

                phaseStepDelta = (targetPhaseStep - currentPhaseStep) / (WaveFormat.SampleRate * PortamentoTime);
                seekFreq = false;
            }
            var vol = Volume; // process volume change only once per call to Read
            for (int n = 0; n < count; ++n)
            {
                int waveTableIndex = (int)phase % waveTable.Length;
                buffer[n + offset] = this.waveTable[waveTableIndex] * vol;
                phase += currentPhaseStep;
                if (this.phase > (double)this.waveTable.Length)
                    this.phase -= (double)this.waveTable.Length;
                if (currentPhaseStep != targetPhaseStep)
                {
                    currentPhaseStep += phaseStepDelta;
                    if (phaseStepDelta > 0.0 && currentPhaseStep > targetPhaseStep)
                        currentPhaseStep = targetPhaseStep;
                    else if (phaseStepDelta < 0.0 && currentPhaseStep < targetPhaseStep)
                        currentPhaseStep = targetPhaseStep;
                }
            }
            return count;
        }



        private void Asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            {
                var sampleCount = e.SamplesPerBuffer * ((AsioOut)sender).DriverInputChannelCount;
                //System.Diagnostics.Debug.WriteLine(sampleCount);
                float[] samples = new float[sampleCount];
                e.GetAsInterleavedSamples(samples);


                float pitchFreq = NWaves.Features.Pitch.FromAutoCorrelation(samples, MainViewModel.sampleRate);

                float max = float.NegativeInfinity;
                float min = float.PositiveInfinity;
                foreach (float sample in samples)
                {
                    if (sample > max)
                        max = sample;
                    if (sample < min)
                        min = sample;
                }

                if (pitchFreq > 0)
                    Frequency = pitchFreq;

                /*if (pitchFreq <= 0)
                    sound_off = true;
                else
                {
                    builder = builder
                        .SetParameter("frequency", pitchFreq);
                    sound_off = false;
                }*/


            }
        }


    }
}
