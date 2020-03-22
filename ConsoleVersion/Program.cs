using System;
using NAudio;
using NAudio.Wave;
using NAudio.Midi;

namespace ConsoleVersion
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string[] driverNames = AsioOut.GetDriverNames();
            foreach (string driverName in driverNames) Console.WriteLine(driverName);
            int selectedDriverIndex = int.Parse(Console.ReadLine());
            AsioOut @asio = new AsioOut(selectedDriverIndex);
            Console.WriteLine(@asio.DriverInputChannelCount);
            @asio.AudioAvailable += asio_AudioAvailable;
            Console.ReadLine();
            @asio.InitRecordAndPlayback(null, @asio.DriverInputChannelCount, 44100);
            @asio.Play();
            Console.ReadLine();
            @asio.Stop();
            @asio.Dispose();
        }

        static float maxVal = 0;
        private static void asio_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            var samples = e.GetAsInterleavedSamples();
            float ls = samples[samples.Length - 1];
            if (ls > maxVal){
                maxVal = ls;
                Console.WriteLine(ls);
            };

        }
    }
}
