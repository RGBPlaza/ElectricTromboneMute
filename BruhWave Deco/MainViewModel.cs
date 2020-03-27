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

        public bool CanStart { get => selectedAsioDriverIndex != null && selectedMidiPortIndex != null; }

        public float CurrentSampleValue { get => currentSampleValue; set { currentSampleValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndicatorWidth)); } }
        public float IndicatorWidth { get => currentSampleValue * 256f; }

        public double PeakFrequency { get => peakFrequency; set { peakFrequency = value; OnPropertyChanged(); OnPropertyChanged(nameof(PeakFrequencyString)); } }
        public string PeakFrequencyString { get => $"{peakFrequency:000} Hz"; }

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

        private int previousNoteNumber = 0;
        private double previousCentDifferenceFromProxy = 0;
        private int proxyNoteNumber = 0;

        private void PitchTracker_PitchDetected(PitchTracker sender, PitchTracker.PitchRecord pitchRecord)
        {
            double pitchFreq = pitchRecord.Pitch * 2;
            PeakFrequency = pitchFreq;
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

            int actualNoteNumber = (int)Math.Round(12 * Math.Log2(pitchFreq / 440d) + 69);
            double actualNoteFreq = Math.Pow(2, (actualNoteNumber - 69) / 12d) * 440;
            double centDifferenceFromActual = 1200 * Math.Log2(pitchFreq / actualNoteFreq);

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
            double proxyNoteFreq = Math.Pow(2, (proxyNoteNumber - 69) / 12d) * 440;
            double centDifferenceFromProxy = 1200 * Math.Log2(pitchFreq / proxyNoteFreq);

            if (centDifferenceFromProxy != previousCentDifferenceFromProxy)
            {
                if(centDifferenceFromProxy > 1599 || centDifferenceFromProxy < -1599)
                {
                    var stopEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOff, proxyNoteNumber, 0);
                    midi.Send(stopEvent.GetAsShortMessage());

                    proxyNoteNumber = actualNoteNumber;
                    var noteEvent = new NoteOnEvent(0, 1, proxyNoteNumber, 100, 0);
                    midi.Send(noteEvent.GetAsShortMessage());

                    proxyNoteFreq = Math.Pow(2, (proxyNoteNumber - 69) / 12d) * 440;
                    centDifferenceFromProxy = 1200 * Math.Log2(pitchFreq / proxyNoteFreq);
                }
                CurrentCentDifference = (int)centDifferenceFromActual;
                var pitchBendEvent = new PitchWheelChangeEvent(0, 1, 0x2000 + (int)(centDifferenceFromProxy / 100d * 0x0200));
                midi.Send(pitchBendEvent.GetAsShortMessage());
                previousCentDifferenceFromProxy = centDifferenceFromProxy;
            }
        }

        private PointCollection wavePoints;
        public PointCollection WavePoints { get => wavePoints; set { wavePoints = value; OnPropertyChanged(); } }

        private AsioOut asio;
        private MidiOut midi;
        public void Start()
        {
            if (!isRunning)
            {
                asio = new AsioOut(selectedAsioDriverIndex ?? 0) { AutoStop = false };
                asio.AudioAvailable += asio_AudioAvailable;
                asio.InitRecordAndPlayback(null, asio.DriverInputChannelCount, sampleRate);
                asio.Play();
                midi = new MidiOut(MidiPortNumbers[SelectedMidiPortIndex ?? 0]);
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
            pitchTracker.ProcessBuffer(samples);

            var wpc = new PointCollection(samples.Select((y, x) => new System.Windows.Point(x, y * 100)));
            wpc.Freeze();
            WavePoints = wpc;

        }

        private string[] notes = new string[12] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };

    }
}
