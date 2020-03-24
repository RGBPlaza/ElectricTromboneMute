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
        private double peakFrequency;
        private const int sampleRate = 44100;
        private List<int> MidiPortNumbers;
        private int currentMidiValue;
        private int currentCentDifference;

        public int CurrentMidiValue { get => currentMidiValue; set { currentMidiValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentNote)); } }
        public string CurrentNote { get => notes[currentMidiValue % 12]; }

        public int CurrentCentDifference { get => currentCentDifference; set { currentCentDifference = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentCentDiffString)); } }
        public string CurrentCentDiffString { get => $"+{currentCentDifference} cents"; }

        public List<string> AsioDrivers { get; set; }
        public List<string> MidiPorts { get; set; }
        public int? SelectedAsioDriverIndex { get => selectedAsioDriverIndex; set { selectedAsioDriverIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); } }
        public int? SelectedMidiPortIndex { get => selectedMidiPortIndex; set { selectedMidiPortIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); } }

        public bool CanStart { get => selectedAsioDriverIndex != null && selectedMidiPortIndex != null; }

        public float CurrentSampleValue { get => currentSampleValue; set { currentSampleValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndicatorWidth)); } }
        public float IndicatorWidth { get => currentSampleValue * 256f; }

        public double PeakFrequency { get => peakFrequency; set { peakFrequency = value; OnPropertyChanged(); OnPropertyChanged(nameof(PeakFrequencyString)); } }
        public string PeakFrequencyString { get => $"{peakFrequency:000} hz"; }

        public bool IsRunning { get => isRunning; set { isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(PowerImageSource)); } }
        public string PowerImageSource { get => isRunning ? "Assets/ActivatedPower.png" : "Assets/DeactivatedPower.png"; }


        private PitchTracker pitchTracker;

        public MainViewModel()
        {
            AsioDrivers = AsioOut.GetDriverNames().ToList();
            MidiPorts = new List<string>();
            MidiPortNumbers = new List<int>();
            for(int i = 0; i < MidiOut.NumberOfDevices; i++)
            {
                var device = MidiOut.DeviceInfo(i);
                if(device.Technology == MidiOutTechnology.MidiPort)
                {
                    MidiPorts.Add(device.ProductName);
                    MidiPortNumbers.Add(i);
                }
            }
            if (AsioDrivers.Count > 0) SelectedAsioDriverIndex = 0;
            if (MidiPorts.Count > 0) SelectedMidiPortIndex = 0;
            pitchTracker = new PitchTracker() { SampleRate = sampleRate };
            pitchTracker.PitchDetected += PitchTracker_PitchDetected;
        }

        private void PitchTracker_PitchDetected(PitchTracker sender, PitchTracker.PitchRecord pitchRecord)
        {
            double peakFreq = pitchRecord.Pitch * 2;
            PeakFrequency = peakFreq;
            if (peakFreq == 0)
            {
                @midi.Reset();
                previousNoteNumber = 0;
                return;
            }

            int noteNumber = (int)Math.Floor(12 * Math.Log2(peakFreq / 440d) + 69);
            double closestNoteFreq = Math.Pow(2, (noteNumber - 69) / 12d) * 440;
            double centDifference = 1200 * Math.Log2(peakFreq / closestNoteFreq);

            if (centDifference != previousCentDifference)
            {
                CurrentCentDifference = (int)centDifference;
                var pitchBendEvent = new PitchWheelChangeEvent(0, 1, 0x2000 + (int)(centDifference / 100d * 0x0200));
                @midi.Send(pitchBendEvent.GetAsShortMessage());
                previousCentDifference = centDifference;
            }

            if (noteNumber != previousNoteNumber)
            {
                CurrentMidiValue = noteNumber;
                var noteEvent = new NoteOnEvent(0, 1, noteNumber, 100, 0);
                @midi.Reset();
                @midi.Send(noteEvent.GetAsShortMessage());
                previousNoteNumber = noteNumber;
            }
        }

        private PointCollection wavePoints;
        public PointCollection WavePoints { get => wavePoints; set { wavePoints = value; OnPropertyChanged(); } }

        private PointCollection ftPoints;
        public PointCollection FTPoints { get => ftPoints; set { ftPoints = value; OnPropertyChanged(); } }

        private AsioOut @asio;
        private MidiOut @midi;
        public void Start()
        {
            if (!isRunning)
            {
                @asio = new AsioOut(selectedAsioDriverIndex ?? 0) { AutoStop = false };
                @asio.AudioAvailable += asio_AudioAvailable;
                @asio.InitRecordAndPlayback(null, @asio.DriverInputChannelCount, sampleRate);
                @asio.Play();
                @midi = new MidiOut(MidiPortNumbers[SelectedMidiPortIndex ?? 0]);
                IsRunning = true;
            }
        }

        public void Stop()
        {
            if (isRunning)
            {
                @asio.Stop();
                @asio.Dispose();
                @midi?.Reset();
                @midi?.Dispose();
                WavePoints = null;
                FTPoints = null;
                IsRunning = false;
            }
        }

        private float[] samples;
        private void asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            var sampleCount = e.SamplesPerBuffer * @asio.DriverInputChannelCount;
            samples = new float[sampleCount];
            e.GetAsInterleavedSamples(samples);

            pitchTracker.ProcessBuffer(samples);

            var wpc = new PointCollection(samples.Select((y, x) => new System.Windows.Point(x, y * 100)));
            wpc.Freeze();
            WavePoints = wpc;

        }

        private int previousNoteNumber = 0;
        private double previousCentDifference = 0;

        private string[] notes = new string[12] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };

    }
}
